using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportMeet.Application.Events;

namespace SportMeet.Infrastructure.Background;

/// <summary>
/// Runs the event reminders on an interval.
///
/// The shape is <see cref="Ingestion.EmailPollingHostedService"/>'s, for the reasons
/// that shape exists: one scope per cycle because <c>AppDbContext</c> is scoped and
/// holding one for the app's lifetime is the classic background-service bug (the
/// change tracker grows forever and eventually serves stale rows); a broad catch so a
/// transient database hiccup does not turn a 30-second outage into reminders being off
/// until somebody notices; and a first cycle that waits rather than firing, so a
/// database that is slow to accept connections at startup does not log a reminder
/// failure that means nothing.
///
/// Unlike the ingestion worker, this one is registered by default - see
/// <see cref="EventReminderOptions.Enabled"/>. Nothing here reaches outside the
/// database, so there is no credential to be missing and no mailbox to be reading.
///
/// Failures are logged per cycle and the loop continues. A reminder that fails to send
/// is retryable on the next pass precisely because the sentinel is only written after a
/// successful claim, and a claim that threw left it null.
/// </summary>
public sealed class EventReminderHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<EventReminderOptions> options,
    ILogger<EventReminderHostedService> logger) : BackgroundService
{
    private readonly EventReminderOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Floored at a minute: the option is operator-settable, and an interval of
        // zero or a negative value would otherwise spin the loop against the database.
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));

        logger.LogInformation(
            "Reminders: sending tomorrow/one-hour notifications every {Interval}.", interval);

        if (!await SleepAsync(interval, stoppingToken)) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<EventReminderService>();

                var result = await service.RunOnceAsync(DateTimeOffset.UtcNow, _options, stoppingToken);

                if (!result.IsQuiet)
                {
                    logger.LogInformation(
                        "Reminder cycle: {Tomorrow} tomorrow, {Soon} one-hour notifications.",
                        result.TomorrowNotifications, result.OneHourNotifications);
                }
                else
                {
                    logger.LogDebug("Reminder cycle: nothing due.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Normal shutdown.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder cycle failed; will retry next interval.");
            }

            if (!await SleepAsync(interval, stoppingToken)) break;
        }

        logger.LogInformation("Reminders: stopped.");
    }

    /// <summary>Task.Delay honouring shutdown, returning false when cancelled. Without
    /// the token, stopping the host waits out the whole interval and container shutdown
    /// pays the grace period instead of exiting clean.</summary>
    private static async Task<bool> SleepAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
