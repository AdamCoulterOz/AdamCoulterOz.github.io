using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Archive.Functions;

public static class ArchiveProtocol
{
    public const int SchemaVersion = 2;
    public const string QueryVersion = "v3-row-safety-guard-500000";
    public const int MaximumRowsPerQuery = 500_000;
}

public sealed record ArchiveOptions(
    string WorkspaceId,
    string SiteApplicationInsightsResourceId,
    Uri ArchiveBlobServiceUri,
    string ManagedIdentityClientId,
    TimeSpan LateArrivalOverlap,
    TimeSpan IncompleteIntervalFloor)
{
    public const string RawContainer = "raw";
    public const string ControlContainer = "control";

    public static ArchiveOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("ARCHIVE");
        var value = new ArchiveOptions(
            Required(section, "WorkspaceId"),
            Required(section, "SiteApplicationInsightsResourceId"),
            new Uri(Required(section, "ArchiveBlobServiceUri")),
            Required(section, "ManagedIdentityClientId"),
            TimeSpan.FromDays(7),
            TimeSpan.FromMinutes(1));

        return value;
    }

    private static string Required(IConfiguration section, string key) =>
        section[key] ?? throw new InvalidOperationException($"Missing required ARCHIVE__{key} setting.");
}

public sealed record ArchiveWindow(DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public ArchiveWindow SplitLeft() => new(StartUtc, StartUtc.AddTicks((EndUtc - StartUtc).Ticks / 2));
    public ArchiveWindow SplitRight() => new(StartUtc.AddTicks((EndUtc - StartUtc).Ticks / 2), EndUtc);
    public bool CanSplit(TimeSpan floor) => EndUtc - StartUtc > floor;
}

public sealed record ArchiveRow(
    string Table,
    string RecordIdentity,
    DateTimeOffset TimeReceived,
    IReadOnlyDictionary<string, JsonElement> SourceColumns);

public enum ArchiveQueryStatus { Complete, SplitRequired, Fatal }

public sealed record ArchiveQueryResult(ArchiveQueryStatus Status, IReadOnlyList<ArchiveRow> Rows, string? Diagnostic = null)
{
    public static ArchiveQueryResult Complete(IReadOnlyList<ArchiveRow> rows) => new(ArchiveQueryStatus.Complete, rows);
    public static ArchiveQueryResult SplitRequired() => new(ArchiveQueryStatus.SplitRequired, Array.Empty<ArchiveRow>());
    public static ArchiveQueryResult Fatal(string diagnostic) => new(ArchiveQueryStatus.Fatal, Array.Empty<ArchiveRow>(), diagnostic);
    public static ArchiveQueryResult CompleteUnlessSafetyGuard(IReadOnlyList<ArchiveRow> rows, int maximumRows = ArchiveProtocol.MaximumRowsPerQuery) =>
        rows.Count >= maximumRows ? SplitRequired() : Complete(rows);
}

public sealed record ArchiveCheckpoint(DateTimeOffset BoundaryUtc, string ETag, string? ManifestBlob, int SchemaVersion, string QueryVersion);
public sealed record ArchiveManifest(int SchemaVersion, string QueryVersion, string Table, ArchiveWindow Window, int RowCount, string NextBoundaryUtc, IReadOnlyList<string> RecordHashes);

public interface IArchiveLogSource
{
    Task<ArchiveQueryResult> QueryAsync(string table, ArchiveWindow window, CancellationToken cancellationToken);
}

public interface IArchiveStore
{
    Task<ArchiveCheckpoint?> ReadCheckpointAsync(string table, CancellationToken cancellationToken);
    Task PutRecordCreateOnlyAsync(ArchiveRow row, CancellationToken cancellationToken);
    Task<string> PutManifestCreateOnlyAsync(ArchiveManifest manifest, CancellationToken cancellationToken);
    Task AdvanceCheckpointAsync(string table, ArchiveCheckpoint? expected, DateTimeOffset nextBoundaryUtc, string manifestBlob, CancellationToken cancellationToken);
}

public interface IArchiveClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemArchiveClock : IArchiveClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
