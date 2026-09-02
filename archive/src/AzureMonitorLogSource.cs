using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;

namespace Archive.Functions;

public sealed class AzureMonitorLogSource : IArchiveLogSource
{
    private readonly ArchiveOptions _options;
    private readonly LogsQueryClient _client;

    public AzureMonitorLogSource(ArchiveOptions options)
    {
        _options = options;
        _client = new LogsQueryClient(new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId)));
    }

    public async Task<ArchiveQueryResult> QueryAsync(string table, ArchiveWindow window, CancellationToken cancellationToken)
    {
        var query = BuildQuery(table, window);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Response<LogsQueryResult> response = await _client.QueryWorkspaceAsync(
                    _options.WorkspaceId,
                    query,
                    new QueryTimeRange(window.StartUtc, window.EndUtc),
                    new LogsQueryOptions { ServerTimeout = TimeSpan.FromMinutes(10), AllowPartialErrors = true },
                    cancellationToken);

                if (response.Value.Status != LogsQueryResultStatus.Success)
                {
                    return ClassifyPartialResult(
                        response.Value.Status,
                        response.Value.Error?.Code,
                        response.Value.Error?.Message);
                }

                return ArchiveQueryResult.CompleteUnlessSafetyGuard(ToArchiveRows(table, response.Value.Table));
            }
            catch (RequestFailedException exception) when (IsLimit(exception) && attempt == 1)
            {
                return ArchiveQueryResult.SplitRequired();
            }
            catch (RequestFailedException exception) when (IsTransient(exception) && attempt < 4)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }
        }
    }

    private string BuildQuery(string table, ArchiveWindow window)
        => BuildKql(table, window, _options.SiteApplicationInsightsResourceId);

    public static string BuildKql(string table, ArchiveWindow window, string siteApplicationInsightsResourceId)
    {
        if (table is not "AppPageViews" and not "AppEvents")
        {
            throw new ArgumentOutOfRangeException(nameof(table));
        }

        var sourceResource = siteApplicationInsightsResourceId.Replace("'", "''", StringComparison.Ordinal);
        var start = window.StartUtc.UtcDateTime.ToString("O");
        var end = window.EndUtc.UtcDateTime.ToString("O");
        var identityExtension = table == "AppEvents"
            ? "| extend archive_event_id = tostring(Properties['archive_event_id'])\n| where isnotempty(archive_event_id)\n| order by _TimeReceived asc, archive_event_id asc"
            : "| where isnotempty(Id)\n| order by _TimeReceived asc, Id asc";

        return $"""
            {table}
            | where _ResourceId == '{sourceResource}'
            | where _TimeReceived >= datetime({start}) and _TimeReceived < datetime({end})
            {identityExtension}
            | take {ArchiveProtocol.MaximumRowsPerQuery + 1}
            """;
    }

    /// <summary>
    /// Decides whether a non-successful Logs Query result can safely be retried as smaller intervals.
    /// Only an explicit row, response-size, or query-time limit is safe to split; semantic,
    /// authorization, and schema failures must retain their diagnostics and fail the run.
    /// </summary>
    public static ArchiveQueryResult ClassifyPartialResult(
        LogsQueryResultStatus status,
        string? errorCode,
        string? errorMessage)
    {
        if (status == LogsQueryResultStatus.PartialFailure && IsConfirmedResultLimit(errorCode, errorMessage))
        {
            return ArchiveQueryResult.SplitRequired();
        }

        var code = string.IsNullOrWhiteSpace(errorCode) ? "no error code" : errorCode;
        var message = string.IsNullOrWhiteSpace(errorMessage) ? "no error message" : errorMessage;
        return ArchiveQueryResult.Fatal($"Azure Monitor returned {status} ({code}): {message}");
    }

    private static IReadOnlyList<ArchiveRow> ToArchiveRows(string table, LogsTable result)
    {
        var names = result.Columns.Select(column => column.Name).ToArray();
        var idIndex = Array.IndexOf(names, table == "AppEvents" ? "archive_event_id" : "Id");
        var receivedIndex = Array.IndexOf(names, "_TimeReceived");
        if (idIndex < 0 || receivedIndex < 0)
        {
            throw new InvalidOperationException($"{table} query did not return its required identity or _TimeReceived column.");
        }

        return result.Rows.Select(row =>
        {
            var values = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
            for (var index = 0; index < names.Length; index++)
            {
                values.Add(names[index], JsonSerializer.SerializeToElement(row[index]));
            }

            var identity = row[idIndex]?.ToString();
            var received = row[receivedIndex] is DateTimeOffset offset
                ? offset
                : DateTimeOffset.Parse(row[receivedIndex]?.ToString() ?? throw new InvalidOperationException("_TimeReceived was null."));
            return new ArchiveRow(table, identity ?? throw new InvalidOperationException("Archive identity was null."), received, values);
        }).ToArray();
    }

    private static bool IsLimit(RequestFailedException exception) =>
        exception.Status is 408 or 413 or 429 or 500 or 502 or 503 or 504 &&
        (exception.Message.Contains("500000", StringComparison.OrdinalIgnoreCase) ||
         exception.Message.Contains("100 MiB", StringComparison.OrdinalIgnoreCase) ||
         exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
         exception.Status is 413 or 504);

    private static bool IsConfirmedResultLimit(string? errorCode, string? errorMessage)
    {
        var text = $"{errorCode} {errorMessage}";
        return errorCode is not null && (errorCode.Equals("ResponseTooLarge", StringComparison.OrdinalIgnoreCase) ||
                                         errorCode.Equals("TooManyRecords", StringComparison.OrdinalIgnoreCase) ||
                                         errorCode.Equals("QueryTimeout", StringComparison.OrdinalIgnoreCase)) ||
               text.Contains("500000", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("500,000", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("100 MiB", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("100MB", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("104857600", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("response too large", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("result size", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("query timeout", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("timed out", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransient(RequestFailedException exception) => exception.Status is 408 or 429 or 500 or 502 or 503 or 504;
}
