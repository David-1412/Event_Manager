using System.Text.RegularExpressions;
using SportMeet.Application.Ingestion;

namespace SportMeet.Application.Imports;

/// <summary>
/// Decides which fields get a "check this" marker. Deterministic rules, because the model
/// reports one overall confidence and nothing per field.
///
/// The aim is a short list. A marker on everything teaches people to ignore markers, so a
/// field is flagged only for a concrete, checkable reason: it is absent (and a default is
/// showing), the source was vague about it, or its pin is unconfirmed. An explicit date
/// like "14 Nov" is not flagged; "Friday" is, because it was resolved against today.
/// </summary>
public static class FieldFlagger
{
    /// <summary>Below this the model is guessing at the title, which is the one field
    /// everything else hangs off.</summary>
    private const decimal TitleConfidenceFloor = 0.6m;

    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private const string Month = "(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*";

    // "14 Nov", "14th of November", "Nov 14", "14/11", "14/11/2026", "2026-11-14".
    // Dashes and dots are deliberately not date separators: "7-9pm" is a time range.
    private static readonly Regex ExplicitDate = new(
        $@"\b\d{{1,2}}(?:st|nd|rd|th)?\s+(?:of\s+)?{Month}\b"
        + $@"|\b{Month}\.?\s+\d{{1,2}}(?:st|nd|rd|th)?\b"
        + @"|\b\d{1,2}/\d{1,2}(?:/\d{2,4})?\b"
        + @"|\b\d{4}-\d{2}-\d{2}\b",
        Opts);

    // "7pm", "7:30 pm", "19:30", "7.30pm", "noon".
    private static readonly Regex ExplicitTime = new(
        @"\b\d{1,2}(?:[:.]\d{2})?\s?(?:am|pm)\b|\b\d{1,2}:\d{2}\b|\b(?:noon|midday)\b",
        Opts);

    public static IReadOnlyList<FieldFlagDto> Flag(
        ExtractedEvent e, GeocodeResult? geo, string sourceText, DateTimeOffset now)
    {
        var flags = new List<FieldFlagDto>();

        if (string.IsNullOrWhiteSpace(e.Title))
            flags.Add(new("title", FlagReason.Missing));
        else if (e.Confidence < TitleConfidenceFloor)
            flags.Add(new("title", FlagReason.Unclear));

        if (e.StartAt is not { } start)
            flags.Add(new("startAt", FlagReason.Missing));
        else if (start < now)
            flags.Add(new("startAt", FlagReason.Past));
        else if (!ExplicitDate.IsMatch(sourceText) || !ExplicitTime.IsMatch(sourceText))
            flags.Add(new("startAt", FlagReason.Unclear));

        // The form shows a default end time when none was found, and a default that
        // looks extracted is worse than an empty field.
        if (e.EndAt is null)
            flags.Add(new("endAt", FlagReason.Assumed));

        var hasPlace = !string.IsNullOrWhiteSpace(e.VenueName) || !string.IsNullOrWhiteSpace(e.Address);
        if (!hasPlace)
            flags.Add(new("venue", FlagReason.Missing));
        else if (geo is null || geo.NeedsConfirm)
            flags.Add(new("venue", FlagReason.Unconfirmed));

        if (e.MaxParticipants is null)
            flags.Add(new("maxParticipants", FlagReason.Assumed));

        return flags;
    }
}
