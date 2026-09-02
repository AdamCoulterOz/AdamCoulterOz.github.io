using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Archive.Functions;

public sealed class BlobArchiveStore : IArchiveStore
{
    private readonly BlobContainerClient _raw;
    private readonly BlobContainerClient _control;

    public BlobArchiveStore(ArchiveOptions options)
    {
        var credential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId));
        var service = new BlobServiceClient(options.ArchiveBlobServiceUri, credential);
        _raw = service.GetBlobContainerClient(ArchiveOptions.RawContainer);
        _control = service.GetBlobContainerClient(ArchiveOptions.ControlContainer);
    }

    public async Task<ArchiveCheckpoint?> ReadCheckpointAsync(string table, CancellationToken cancellationToken)
    {
        var blob = _control.GetBlobClient($"checkpoints/{table}.json");
        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken);
            var stored = response.Value.Content.ToObjectFromJson<StoredCheckpoint>()
                ?? throw new InvalidOperationException($"Checkpoint for {table} was invalid JSON.");
            if (stored.SchemaVersion != ArchiveProtocol.SchemaVersion || stored.QueryVersion != ArchiveProtocol.QueryVersion)
            {
                throw new InvalidOperationException($"Checkpoint for {table} has unsupported schema/query versions {stored.SchemaVersion}/{stored.QueryVersion}.");
            }
            return new ArchiveCheckpoint(stored.BoundaryUtc, response.Value.Details.ETag.ToString(), stored.ManifestBlob, stored.SchemaVersion, stored.QueryVersion);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task PutRecordCreateOnlyAsync(ArchiveRow row, CancellationToken cancellationToken)
    {
        var content = ArchiveJson.SerializeRecord(row);
        var blobName = GetRecordBlobName(row);
        await CreateOnlyOrVerifyAsync(_raw.GetBlobClient(blobName), content, cancellationToken);
    }

    public async Task<string> PutManifestCreateOnlyAsync(ArchiveManifest manifest, CancellationToken cancellationToken)
    {
        var content = ArchiveJson.Serialize(manifest);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()[..16];
        var name = $"manifests/{manifest.Table}/{manifest.Window.EndUtc:yyyyMMddTHHmmssZ}-{hash}.json";
        await CreateOnlyOrVerifyAsync(_control.GetBlobClient(name), content, cancellationToken);
        return name;
    }

    public async Task AdvanceCheckpointAsync(string table, ArchiveCheckpoint? expected, DateTimeOffset nextBoundaryUtc, string manifestBlob, CancellationToken cancellationToken)
    {
        var conditions = expected is null
            ? new BlobRequestConditions { IfNoneMatch = ETag.All }
            : new BlobRequestConditions { IfMatch = new ETag(expected.ETag) };
        var data = ArchiveJson.Serialize(new StoredCheckpoint(nextBoundaryUtc, manifestBlob, ArchiveProtocol.SchemaVersion, ArchiveProtocol.QueryVersion));
        await _control.GetBlobClient($"checkpoints/{table}.json").UploadAsync(
            BinaryData.FromBytes(data),
            new BlobUploadOptions { Conditions = conditions },
            cancellationToken);
    }

    public static string GetRecordBlobName(ArchiveRow row)
    {
        var resource = row.SourceColumns.TryGetValue("_ResourceId", out var resourceId) ? resourceId.GetString() : null;
        var resourceToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resource ?? "unknown"))).ToLowerInvariant()[..16];
        var identityToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.RecordIdentity))).ToLowerInvariant();
        return $"{row.Table}/{row.TimeReceived:yyyy/MM/dd}/{resourceToken}/{identityToken}.json";
    }

    private static async Task CreateOnlyOrVerifyAsync(BlobClient blob, byte[] expected, CancellationToken cancellationToken)
    {
        try
        {
            await blob.UploadAsync(BinaryData.FromBytes(expected), new BlobUploadOptions
            {
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            }, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            var actual = (await blob.DownloadContentAsync(cancellationToken)).Value.Content.ToMemory().ToArray();
            EnsureExistingContentMatches(blob.Name, expected, actual, exception);
        }
    }

    public static void EnsureExistingContentMatches(string blobName, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, Exception? innerException = null)
    {
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            throw new InvalidOperationException($"Existing archive blob {blobName} does not match its deterministic payload.", innerException);
        }
    }

    private sealed record StoredCheckpoint(DateTimeOffset BoundaryUtc, string ManifestBlob, int SchemaVersion, string QueryVersion);
}
