namespace SportMeet.Application.Ingestion;

/// <summary>
/// Tally of one poll cycle. A record rather than log lines because the numbers are what
/// make the feature debuggable: "nothing happened" is either zero unread, all duplicates,
/// or all no-event, and those three need completely different responses.
/// </summary>
public sealed record EmailProcessingResult(
    int Fetched,
    int Created,
    int Duplicates,
    int NoEvent,
    int Failed,
    int SkippedSender)
{
    public static EmailProcessingResult Empty { get; } = new(0, 0, 0, 0, 0, 0);

    /// <summary>Nothing worth a log line. Used to keep an idle poller quiet: at a
    /// five-minute interval, an "idle" line every cycle is 288 lines a day that say
    /// nothing, and real signal gets scrolled away.</summary>
    public bool IsQuiet => Fetched == 0;

    /// <summary>Single-line form for the cycle's Information log. Built here so the
    /// hosted service stays about control flow and the vocabulary is one place.</summary>
    public string Summary =>
        $"fetched={Fetched} created={Created} duplicate={Duplicates} " +
        $"no_event={NoEvent} skipped_sender={SkippedSender} failed={Failed}";
}

/// <summary>
/// One poll cycle: fetch, pre-filter, hand each body to the existing ingestion service.
///
/// Extracted from the hosted service so the whole policy — sender pre-filter, duplicate
/// skipping, failure isolation, counting — is reachable without a mailbox, a token, or a
/// running host. The BackgroundService below is then only a timer and a DI scope.
/// </summary>
public interface IEmailProcessor
{
    Task<EmailProcessingResult> PollOnceAsync(CancellationToken ct = default);
}
