using System.Text.Json;
using Archive.Functions;
using Azure.Monitor.Query.Models;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Archive.Functions.Tests;

public sealed class ArchiveCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Cursor_uses_fixed_upper_boundary_and_seven_day_overlap()
    {
        var source = new TestSource();
        var store = new TestStore { Checkpoints = { ["AppPageViews"] = Checkpoint(Now.AddDays(-10), "one") } };
        await Coordinator(source, store).RunAsync(CancellationToken.None);

        Assert.Contains(source.Windows, pair => pair.Table == "AppPageViews" && pair.Window.StartUtc == Now.AddDays(-17));
        Assert.All(source.Windows.Where(pair => pair.Table == "AppPageViews"), pair => Assert.True(pair.Window.EndUtc - pair.Window.StartUtc <= TimeSpan.FromDays(7)));
        Assert.Equal(Now.AddDays(-1), store.Advances["AppPageViews"].Boundary);
        Assert.All(store.Manifests, manifest =>
        {
            Assert.Equal(ArchiveProtocol.SchemaVersion, manifest.SchemaVersion);
            Assert.Equal(ArchiveProtocol.QueryVersion, manifest.QueryVersion);
        });
    }

    [Fact]
    public async Task Replayed_rows_are_idempotent_at_the_store_boundary()
    {
        var source = new TestSource { Rows = [Row("AppPageViews", "page-view-id", Now.AddDays(-2))] };
        var store = new TestStore();
        var coordinator = Coordinator(source, store);
        await coordinator.RunAsync(CancellationToken.None);
        await coordinator.RunAsync(CancellationToken.None);

        Assert.Single(store.DistinctRecordIds);
        Assert.True(store.RecordWrites > 1);
    }

    [Fact]
    public async Task Oversized_interval_is_split_before_rows_are_accepted()
    {
        var source = new TestSource { SplitAbove = TimeSpan.FromDays(3) };
        var store = new TestStore();
        await Coordinator(source, store).RunAsync(CancellationToken.None);

        Assert.True(source.Windows.Count(pair => pair.Table == "AppPageViews") > 1);
        Assert.Contains(source.Windows.Where(pair => pair.Table == "AppPageViews"), pair => pair.Window.EndUtc - pair.Window.StartUtc <= TimeSpan.FromDays(3));
    }

    [Fact]
    public void Conservative_row_safety_guard_requires_interval_split_before_archiving()
    {
        var rows = Enumerable.Repeat(Row("AppPageViews", "row-guard", Now), 4).ToArray();
        var result = ArchiveQueryResult.CompleteUnlessSafetyGuard(rows, maximumRows: 3);

        Assert.Equal(ArchiveQueryStatus.SplitRequired, result.Status);
        Assert.Empty(result.Rows);
        Assert.Equal(ArchiveQueryStatus.SplitRequired, ArchiveQueryResult.CompleteUnlessSafetyGuard(rows.Take(3).ToArray(), maximumRows: 3).Status);
    }

    [Theory]
    [InlineData("ResponseTooLarge", "The query response exceeded its limit.")]
    [InlineData("QueryTimeout", "The query timed out after 10 minutes.")]
    public void Partial_limit_diagnostic_requires_interval_split(string errorCode, string errorMessage)
    {
        var result = AzureMonitorLogSource.ClassifyPartialResult(
            LogsQueryResultStatus.PartialFailure,
            errorCode,
            errorMessage);

        Assert.Equal(ArchiveQueryStatus.SplitRequired, result.Status);
    }

    [Theory]
    [InlineData("BadArgumentError", "Semantic error: a column does not exist.")]
    [InlineData("AuthorizationFailed", "The client is not authorized to query this workspace.")]
    [InlineData("SchemaError", "The table schema is invalid.")]
    public void Partial_semantic_or_authorization_diagnostic_is_fatal(string errorCode, string errorMessage)
    {
        var result = AzureMonitorLogSource.ClassifyPartialResult(
            LogsQueryResultStatus.PartialFailure,
            errorCode,
            errorMessage);

        Assert.Equal(ArchiveQueryStatus.Fatal, result.Status);
        Assert.Contains(errorCode, result.Diagnostic);
        Assert.Contains(errorMessage, result.Diagnostic);
    }

    [Fact]
    public void App_events_kql_orders_by_archive_identity_not_missing_id_column()
    {
        var query = AzureMonitorLogSource.BuildKql("AppEvents", new ArchiveWindow(Now.AddDays(-2), Now.AddDays(-1)), "/resource/site");

        Assert.Contains("order by _TimeReceived asc, archive_event_id asc", query);
        Assert.Contains("take 500001", query);
        Assert.DoesNotContain("order by _TimeReceived asc, Id asc", query);
    }

    [Fact]
    public void Resource_id_kql_filter_is_case_insensitive()
    {
        var query = AzureMonitorLogSource.BuildKql("AppPageViews", new ArchiveWindow(Now.AddDays(-2), Now.AddDays(-1)), "/SUBSCRIPTIONS/ABC/RESOURCEGROUPS/SITE");

        Assert.Contains("| where _ResourceId =~ '/SUBSCRIPTIONS/ABC/RESOURCEGROUPS/SITE'", query);
        Assert.DoesNotContain("| where _ResourceId ==", query);
    }

    [Fact]
    public async Task Functions_application_insights_registers_compatible_telemetry_initializers()
    {
        var services = new ServiceCollection();

        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        await using var provider = services.BuildServiceProvider();
        Assert.NotEmpty(provider.GetServices<ITelemetryInitializer>());
    }

    [Fact]
    public void Raw_blob_path_is_stable_for_an_identity_and_never_uses_payload_hash()
    {
        var original = Row("AppPageViews", "stable-id", Now);
        var changed = original with
        {
            SourceColumns = new Dictionary<string, JsonElement>(original.SourceColumns)
            {
                ["Name"] = JsonSerializer.SerializeToElement("changed source data")
            }
        };

        Assert.Equal(BlobArchiveStore.GetRecordBlobName(original), BlobArchiveStore.GetRecordBlobName(changed));
        Assert.NotEqual(ArchiveCoordinator.RecordHash(original), ArchiveCoordinator.RecordHash(changed));
        Assert.Throws<InvalidOperationException>(() => BlobArchiveStore.EnsureExistingContentMatches("raw/collision.json", [1, 2], [1, 3]));
    }

    [Fact]
    public async Task Fatal_query_diagnostic_does_not_advance_checkpoint_or_split()
    {
        var source = new TestSource { FatalDiagnostic = "KQL semantic error" };
        var store = new TestStore();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(source, store).RunAsync(CancellationToken.None));
        Assert.Contains("KQL semantic error", exception.Message);
        Assert.Empty(store.Advances);
        Assert.Single(source.Windows);
    }

    [Fact]
    public async Task Failure_before_manifest_or_checkpoint_leaves_cursor_unchanged()
    {
        var source = new TestSource { Rows = [Row("AppPageViews", "will-fail", Now.AddDays(-2))] };
        var store = new TestStore { FailRecordWrite = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(source, store).RunAsync(CancellationToken.None));
        Assert.Empty(store.Advances);
        Assert.Empty(store.Manifests);
    }

    [Fact]
    public async Task Competing_checkpoint_etag_is_not_overwritten()
    {
        var source = new TestSource();
        var store = new TestStore
        {
            Checkpoints = { ["AppPageViews"] = Checkpoint(Now.AddDays(-8), "stale") },
            RejectCheckpointAdvance = true
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(source, store).RunAsync(CancellationToken.None));
        Assert.Empty(store.Advances);
    }

    [Fact]
    public async Task Late_arrival_window_reaches_back_seven_days_from_checkpoint()
    {
        var source = new TestSource();
        var store = new TestStore { Checkpoints = { ["AppEvents"] = Checkpoint(Now.AddDays(-20), "event") } };
        await Coordinator(source, store).RunAsync(CancellationToken.None);

        Assert.Contains(source.Windows, pair => pair.Table == "AppEvents" && pair.Window.StartUtc == Now.AddDays(-27));
    }

    private static ArchiveCoordinator Coordinator(TestSource source, TestStore store) => new(
        new ArchiveOptions("workspace", "/resource/site", new Uri("https://archive.blob.core.windows.net/"), "identity", TimeSpan.FromDays(7), TimeSpan.FromMinutes(1)),
        source,
        store,
        new FixedClock(Now),
        NullLogger<ArchiveCoordinator>.Instance);

    private static ArchiveCheckpoint Checkpoint(DateTimeOffset boundary, string etag) =>
        new(boundary, etag, null, ArchiveProtocol.SchemaVersion, ArchiveProtocol.QueryVersion);

    private static ArchiveRow Row(string table, string identity, DateTimeOffset received) => new(
        table,
        identity,
        received,
        new Dictionary<string, JsonElement>
        {
            ["_ResourceId"] = JsonSerializer.SerializeToElement("/resource/site"),
            ["_TimeReceived"] = JsonSerializer.SerializeToElement(received),
            ["Id"] = JsonSerializer.SerializeToElement(identity)
        });

    private sealed class FixedClock(DateTimeOffset now) : IArchiveClock { public DateTimeOffset UtcNow => now; }

    private sealed class TestSource : IArchiveLogSource
    {
        public List<(string Table, ArchiveWindow Window)> Windows { get; } = [];
        public IReadOnlyList<ArchiveRow> Rows { get; init; } = [];
        public TimeSpan? SplitAbove { get; init; }
        public string? FatalDiagnostic { get; init; }

        public Task<ArchiveQueryResult> QueryAsync(string table, ArchiveWindow window, CancellationToken cancellationToken)
        {
            Windows.Add((table, window));
            if (FatalDiagnostic is not null)
            {
                return Task.FromResult(ArchiveQueryResult.Fatal(FatalDiagnostic));
            }
            if (SplitAbove is { } split && window.EndUtc - window.StartUtc > split)
            {
                return Task.FromResult(ArchiveQueryResult.SplitRequired());
            }
            return Task.FromResult(ArchiveQueryResult.Complete(Rows.Where(row => row.Table == table).ToArray()));
        }
    }

    private sealed class TestStore : IArchiveStore
    {
        public Dictionary<string, ArchiveCheckpoint> Checkpoints { get; } = [];
        public Dictionary<string, (DateTimeOffset Boundary, string Manifest)> Advances { get; } = [];
        public HashSet<string> DistinctRecordIds { get; } = [];
        public List<ArchiveManifest> Manifests { get; } = [];
        public int RecordWrites { get; private set; }
        public bool FailRecordWrite { get; init; }
        public bool RejectCheckpointAdvance { get; init; }

        public Task<ArchiveCheckpoint?> ReadCheckpointAsync(string table, CancellationToken cancellationToken) =>
            Task.FromResult(Checkpoints.TryGetValue(table, out var checkpoint) ? checkpoint : null);

        public Task PutRecordCreateOnlyAsync(ArchiveRow row, CancellationToken cancellationToken)
        {
            if (FailRecordWrite) throw new InvalidOperationException("forced record write failure");
            RecordWrites++;
            DistinctRecordIds.Add($"{row.Table}/{row.RecordIdentity}");
            return Task.CompletedTask;
        }

        public Task<string> PutManifestCreateOnlyAsync(ArchiveManifest manifest, CancellationToken cancellationToken)
        {
            Manifests.Add(manifest);
            return Task.FromResult($"manifests/{manifest.Table}.json");
        }

        public Task AdvanceCheckpointAsync(string table, ArchiveCheckpoint? expected, DateTimeOffset nextBoundaryUtc, string manifestBlob, CancellationToken cancellationToken)
        {
            if (RejectCheckpointAdvance) throw new InvalidOperationException("ETag precondition failed");
            Advances[table] = (nextBoundaryUtc, manifestBlob);
            Checkpoints[table] = new ArchiveCheckpoint(nextBoundaryUtc, "next", manifestBlob, ArchiveProtocol.SchemaVersion, ArchiveProtocol.QueryVersion);
            return Task.CompletedTask;
        }
    }
}
