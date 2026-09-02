using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Archive.Functions;

public sealed class WeeklyArchiveFunction(ArchiveCoordinator coordinator, ILogger<WeeklyArchiveFunction> logger)
{
    [Function(nameof(WeeklyArchiveFunction))]
    [FixedDelayRetry(5, "00:01:00")]
    public async Task RunTimerAsync(
        [TimerTrigger("0 15 2 * * 1", UseMonitor = true, RunOnStartup = false)] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting monitored weekly archive. IsPastDue={IsPastDue}", timer.IsPastDue);
        try
        {
            await coordinator.RunAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Archive run failed.");
            throw;
        }
    }

}
