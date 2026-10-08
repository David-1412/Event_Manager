using SportMeet.Application.Events;

namespace SportMeet.Application.Imports;

/// <summary>
/// Compares what the extractor proposed with what the user published, field by field.
/// This is the real-world accuracy measurement: a field the user left alone was right;
/// one they edited was wrong or incomplete.
///
/// Outcomes: <c>kept</c> (same value), <c>changed</c> (different value), <c>filled</c>
/// (the extractor found nothing and the published event has a value, which includes a
/// form default that was accepted) and <c>cleared</c> (the extractor found something and
/// the published event has nothing). A field empty on both sides is not reported.
/// </summary>
public static class ImportDiff
{
    public const string Kept = "kept";
    public const string Changed = "changed";
    public const string Filled = "filled";
    public const string Cleared = "cleared";

    /// <summary>Two pins closer than this are the same place. The picker reverse-geocodes
    /// on pick, so a nudged pin is not an extraction error.</summary>
    private const double SamePlaceMetres = 100;

    public static IReadOnlyDictionary<string, string> Compare(CreateEventDto extracted, CreateEventDto final)
    {
        var o = new Dictionary<string, string>(StringComparer.Ordinal);

        Text(o, "title", extracted.Title, final.Title);
        Text(o, "description", extracted.Description, final.Description);
        Time(o, "startAt", extracted.StartAt, final.StartAt);
        Time(o, "endAt", extracted.EndAt, final.EndAt);
        Text(o, "venueName", extracted.VenueName, final.VenueName);
        Text(o, "address", extracted.Address, final.Address);
        Place(o, extracted, final);
        Compare(o, "maxParticipants", extracted.MaxParticipants, final.MaxParticipants, (a, b) => a == b);
        // null and 0 both mean "free", so a form that writes 0 for an empty box is not a change.
        Compare(o, "cost", Free(extracted.Cost), Free(final.Cost), (a, b) => a == b);
        Compare(o, "tags", Tags(extracted.Tags), Tags(final.Tags), (a, b) => a!.SetEquals(b!));
        return o;
    }

    private static decimal? Free(decimal? cost) => cost is null or 0 ? null : cost;

    private static HashSet<string>? Tags(IEnumerable<string>? tags)
    {
        var set = (tags ?? []).Select(t => t.Trim().TrimStart('#').ToLowerInvariant())
            .Where(t => t.Length > 0).ToHashSet();
        return set.Count == 0 ? null : set;
    }

    private static void Text(Dictionary<string, string> o, string field, string? a, string? b)
        => Compare(o, field, Normalize(a), Normalize(b), (x, y) => x == y);

    private static string? Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void Time(Dictionary<string, string> o, string field, DateTimeOffset? a, DateTimeOffset? b)
        => Compare(o, field, a, b, (x, y) => Math.Abs((x!.Value - y!.Value).TotalMinutes) < 1);

    private static void Place(Dictionary<string, string> o, CreateEventDto a, CreateEventDto b)
    {
        // A missing pin is (0, 0) on the form, never a real place.
        static bool Has(double? lat, double? lon) => lat is { } la && lon is { } lo && !(la == 0 && lo == 0);
        var hasA = Has(a.Latitude, a.Longitude);
        var hasB = Has(b.Latitude, b.Longitude);
        if (!hasA && !hasB) return;
        if (!hasA) o["location"] = Filled;
        else if (!hasB) o["location"] = Cleared;
        else
            o["location"] = GeocodePolicy.DistanceMetres(
                a.Latitude!.Value, a.Longitude!.Value, b.Latitude!.Value, b.Longitude!.Value) <= SamePlaceMetres
                ? Kept
                : Changed;
    }

    private static void Compare<T>(
        Dictionary<string, string> o, string field, T? a, T? b, Func<T, T, bool> same)
    {
        var hasA = a is not null;
        var hasB = b is not null;
        if (!hasA && !hasB) return;
        o[field] = !hasA ? Filled : !hasB ? Cleared : same(a!, b!) ? Kept : Changed;
    }
}
