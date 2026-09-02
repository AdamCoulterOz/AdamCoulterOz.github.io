using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Archive.Functions;

public sealed class ArchiveCoordinator(
    ArchiveOptions options,
    IArchiveLogSource source,
    IArchiveStore store,
    IArchiveClock clock,
    ILogger<ArchiveCoordinator> logger)
{
    private static readonly string[] Tables = ["AppPageViews", "AppEvents"];
    // Keep normal batches well below the Logs API result-size and ten-minute execution ceilings.
    private static readonly TimeSpan MaximumQueryWindow = TimeSpan.FromDays(7);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var fixedUpperBound = clock.UtcNow.AddDays(-1);
        foreach (var table in Tables)
        {
            await ArchiveTableAsync(table, fixedUpperBound, cancellationToken);
        }
        logger.LogInformation("Archive run completed successfully.");
    }

    private async Task ArchiveTableAsync(string table, DateTimeOffset fixedUpperBound, CancellationToken cancellationToken)
    {
        var checkpoint = await store.ReadCheckpointAsync(table, cancellationToken);
        var committedBoundary = checkpoint?.BoundaryUtc ?? fixedUpperBound.AddDays(-30);
        var queryStart = committedBoundary.Add(-options.LateArrivalOverlap);
        var window = new ArchiveWindow(queryStart, fixedUpperBound);
        var rows = await ReadRecursivelyAsync(table, window, cancellationToken);

        var hashes = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            await store.PutRecordCreateOnlyAsync(row, cancellationToken);
            hashes.Add(RecordHash(row));
        }

        var manifest = new ArchiveManifest(ArchiveProtocol.SchemaVersion, ArchiveProtocol.QueryVersion, table, window, rows.Count, fixedUpperBound.ToString("O"), hashes);
        var manifestBlob = await store.PutManifestCreateOnlyAsync(manifest, cancellationToken);
        await store.AdvanceCheckpointAsync(table, checkpoint, fixedUpperBound, manifestBlob, cancellationToken);
        logger.LogInformation("Archived {RowCount} {Table} rows through {BoundaryUtc}.", rows.Count, table, fixedUpperBound);
    }

    private async Task<List<ArchiveRow>> ReadRecursivelyAsync(string table, ArchiveWindow window, CancellationToken cancellationToken)
    {
        if (window.EndUtc - window.StartUtc > MaximumQueryWindow)
        {
            var boundedLeft = await ReadRecursivelyAsync(table, window.SplitLeft(), cancellationToken);
            var boundedRight = await ReadRecursivelyAsync(table, window.SplitRight(), cancellationToken);
            boundedLeft.AddRange(boundedRight);
            return boundedLeft;
        }

        var response = await source.QueryAsync(table, window, cancellationToken);
        if (response.Status == ArchiveQueryStatus.Complete)
        {
            return [.. response.Rows];
        }

        if (response.Status == ArchiveQueryStatus.Fatal)
        {
            throw new InvalidOperationException($"{table} archive query failed: {response.Diagnostic}");
        }

        if (!window.CanSplit(options.IncompleteIntervalFloor))
        {
            throw new InvalidOperationException($"{table} query remains over an Azure Monitor limit at the minimum interval.");
        }

        logger.LogWarning("Splitting {Table} archive query from {StartUtc} to {EndUtc} after a service limit.", table, window.StartUtc, window.EndUtc);
        var left = await ReadRecursivelyAsync(table, window.SplitLeft(), cancellationToken);
        var right = await ReadRecursivelyAsync(table, window.SplitRight(), cancellationToken);
        left.AddRange(right);
        return left;
    }

    public static string RecordHash(ArchiveRow row)
    {
        var payload = ArchiveJson.SerializeRecord(row);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}

internal static class ArchiveJson
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static byte[] SerializeRecord(ArchiveRow row)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("archive_format_version", ArchiveProtocol.SchemaVersion);
        writer.WriteString("source_table", row.Table);
        writer.WriteString("source_record_identity", row.RecordIdentity);
        writer.WriteString("source_time_received_utc", row.TimeReceived);
        writer.WritePropertyName("source_columns");
        writer.WriteStartObject();
        foreach (var column in row.SourceColumns.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(column.Key);
            column.Value.WriteTo(writer);
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
}
