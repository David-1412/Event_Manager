using SportMeet.Application.Events;

namespace SportMeet.Application.Imports;

/// <summary>A field the user should look at before publishing. <c>Field</c> is one of
/// title, startAt, endAt, venue, maxParticipants; <c>Reason</c> is one of the
/// <see cref="FlagReason"/> values.</summary>
public sealed record FieldFlagDto(string Field, string Reason);

public sealed record GeocodeDto(string LocationType, bool NeedsConfirm);

/// <summary>
/// The import response. <c>Payload</c> is a <c>CreateEventDto</c>, the same body the
/// create form already posts, so the client drops it straight into the form.
/// <c>Basic</c> is true when the heuristic extractor answered (no AI configured, or the
/// model failed): it cannot read venue or address, and the UI says so.
/// </summary>
public sealed record ImportDraftDto(
    Guid ImportId,
    string Kind,
    decimal? Confidence,
    IReadOnlyList<string> MissingFields,
    CreateEventDto? Payload,
    IReadOnlyList<FieldFlagDto> Flags,
    GeocodeDto? Geocode,
    bool Basic,
    string? Detail);

/// <summary>Sent by the create form after a successful publish. <c>Path</c> is
/// manual, import or draft; <c>DurationMs</c> runs from opening Create Event to the
/// publish succeeding.</summary>
public sealed record PublishMetricsRequest(Guid EventId, int DurationMs, string Path, Guid? ImportId);

public enum ImportErrorCode
{
    EmptyInput,
    TooLong,
    UrlNotSupported,
    Busy,
}

/// <summary>An expected refusal the user can act on. The Api maps each code to a
/// status and a Problem Details <c>code</c>.</summary>
public sealed class ImportException(ImportErrorCode code, string message) : Exception(message)
{
    public ImportErrorCode Code { get; } = code;
}

public sealed class ImportOptions
{
    public const string SectionName = "Import";

    /// <summary>Global ceiling on imports in any rolling 24 hours. Each one can spend
    /// money (model plus geocoder), and per-user limits do not bound the total. Counted
    /// from the table, so it survives restarts and needs no extra state.</summary>
    public int MaxPerDay { get; init; } = 2000;

    /// <summary>Per-user ceiling per rolling hour, enforced by the rate limiter. Generous
    /// enough that a person fixing a paste and retrying never meets it.</summary>
    public int MaxPerUserPerHour { get; init; } = 30;

    /// <summary>How long the pasted text is kept. It can contain personal correspondence,
    /// so it does not live forever just because the row is convenient. Cleared on the next
    /// import after the window passes, so there is no separate sweeper to forget to run.</summary>
    public int SourceTextRetentionDays { get; init; } = 30;
}
