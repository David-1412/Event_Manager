using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportMeet.Application.Ingestion;

namespace SportMeet.Infrastructure.Ingestion;

/// <summary>
/// Polls the mailbox on an interval. Registered with <c>AddHostedService</c> only when
/// <see cref="IngestionOptions.Enabled"/> is true, so an off switch means no timer, no
/// token request and no thread — not a worker that wakes up and decides to do nothing.
///
/// <b>Everything is scoped except this.</b> <c>AppDbContext</c> is scoped and not
/// thread-safe, so one scope is created *per cycle* and disposed after it. Holding a
/// context for the app's lifetime is the classic background-service bug: the change
/// tracker grows forever and eventually serves stale rows.
///
/// The loop catches broadly and keeps going. A poller that dies on a transient AAD or
/// database hiccup turns a 30-second outage into ingestion being off until somebody
/// notices — and nothing in the API reports on it, so "notices" means a user asking where
/// their events are. Failures log at Error each cycle, which is the signal that matters.
/// </summary>
public sealed class EmailPollingHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> ingestionOptions,
    IOptions<GraphOptions> graphOptions,
    ILogger<EmailPollingHostedService> logger) : BackgroundService
{
    private readonly IngestionOptions _ingestion = ingestionOptions.Value;
    private readonly GraphOptions _graph = graphOptions.Value;

    /// <summary>Backoff ceiling. Graph throttles with 429s under load; without a cap a
    /// tight retry loop against a throttled tenant is how you turn one bad mailbox into a
    /// tenant-wide slowdown.</summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(30, _ingestion.PollIntervalSeconds));

        if (!_graph.IsConfigured)
        {
            // Reached only when Enabled is true but credentials are absent — i.e. someone
            // turned the feature on without finishing setup. Loud, and then it stops
            // rather than retrying forever against a mailbox it cannot address.
            logger.LogError(
                "Ingestion: Enabled is true but AzureAd is incomplete (missing {Missing}). " +
                "Polling will not start. Set Ingestion:Enabled=false or complete the AzureAd section.",
                _graph.DescribeMissing());
            return;
        }

        logger.LogInformation(
            "Ingestion: polling {Mailbox} every {Interval} via Graph. Drafts are never auto-published.",
            _graph.MailboxAddress, interval);

        // First cycle waits rather than firing immediately. Startup already runs the
        // seeder and any migration; a Graph call in the same instant couples a mailbox
        // outage to application startup, and a slow token request delays the API binding
        // its port for no reason.
        if (!await SleepAsync(interval, stoppingToken)) return;

        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IEmailProcessor>();

                var result = await processor.PollOnceAsync(stoppingToken);
                consecutiveFailures = 0;

                if (!result.IsQuiet)
                    logger.LogInformation("Ingestion cycle: {Summary}", result.Summary);
                else
                    logger.LogDebug("Ingestion cycle: idle (0 unread)");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Normal shutdown.
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                var backoff = Backoff(consecutiveFailures);
                logger.LogError(ex,
                    "Ingestion cycle failed (attempt {Attempt}); next try in {Delay}",
                    consecutiveFailures, backoff);

                if (!await SleepAsync(backoff, stoppingToken)) break;
                continue;
            }

            if (!await SleepAsync(interval, stoppingToken)) break;
        }

        logger.LogInformation("Ingestion: polling stopped");
    }

    /// <summary>Exponential 5m→10m cap, reached in two steps rather than a long ramp: the
    /// point is to stop hammering a service that is clearly unhappy, not to grade the
    /// severity of the outage.</summary>
    private TimeSpan Backoff(int failures)
    {
        var seconds = _ingestion.PollIntervalSeconds * Math.Pow(2, Math.Min(failures, 3));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxDelay.TotalSeconds));
    }

    /// <summary>Task.Delay honouring shutdown, returning false when cancelled. Without
    /// the linked token, stopping the host blocks for the remainder of the interval and
    /// container shutdown waits on the 30-second grace period instead of exiting clean.</summary>
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
