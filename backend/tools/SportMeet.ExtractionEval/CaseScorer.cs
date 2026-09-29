using SportMeet.Application.Ingestion;

namespace SportMeet.ExtractionEval;

/// <summary>Score of one case.</summary>
public sealed record CaseScore(
    EvalCase Case,
    ExtractedEvent? Actual,
    string ActualOutcome,
    bool OutcomeCorrect,
    IReadOnlyList<string> CorrectFields,
    IReadOnlyList<string> WrongFields,
    IReadOnlyList<string> MissedFields,
    IReadOnlyList<string> HallucinatedFields,
    IReadOnlyList<string> UnattemptedFields)
{
    /// <summary>Fields the answer key constrains <em>and</em> the extractor is capable of
    /// producing. This is the denominator for accuracy — a three-field email is not penalised
    /// for the seven it does not state, and an extractor is not penalised for a field it was
    /// designed not to read.</summary>
    public int ComparedCount => CorrectFields.Count + WrongFields.Count
                                + MissedFields.Count + HallucinatedFields.Count;

    /// <summary>Excluded from every accuracy denominator. Its defects still print, because a
    /// relative-date case that starts failing is usually a real regression and someone should
    /// see it — it just cannot be a number in a trend, since its correct answer moves.</summary>
    public bool Excluded => Case.TimeSensitive;

    public bool Clean => WrongFields.Count == 0 && HallucinatedFields.Count == 0
                         && MissedFields.Count == 0 && OutcomeCorrect;
}

/// <summary>
/// Compares a produced draft against the answer key.
///
/// <para><b>Four verdicts per field, because two are not enough.</b> Correct / wrong would
/// fold away the distinction that matters most in this feature. A <i>miss</i> is the email
/// stating a value and the extractor leaving it null; a <i>hallucination</i> is the extractor
/// producing a value the key says the email never stated. Both count as incorrect, but they
/// are opposite failures with opposite fixes: misses are answered by a better prompt,
/// hallucinations by tightening it. A pipeline at 95% accuracy with a 20% hallucination rate
/// is a worse product than one at 80% with none, because the first one's errors reach the
/// queue inside a confident-looking draft that gives the reviewer no reason to look.</para>
///
/// <para><b>Times compare as instants, strings compare loosely.</b> <c>18:30+10</c> and
/// <c>08:30Z</c> are the same moment, and an evaluator that failed that would be reporting a
/// bug that is not one. Titles and venues are case-folded and punctuation-insensitive — a
/// model writing "Trail Run, Royal Park" for "Trail Run - Royal Park" is right. Loose
/// comparison is deliberately not applied to numbers, dates or skill level.</para>
///
/// <para><b>Percentages to one decimal.</b> A 50-case set moves in steps of two points;
/// integer rounding hides the whole of what a prompt change did.</para>
///
/// <para><b>Fields an extractor cannot produce are reported, not scored.</b> The heuristic
/// never reads a venue or a timezone - that is a design decision documented on the class, not
/// a mistake in a run. Scoring those fields as misses would depress accuracy to a number that
/// describes the extractor's scope rather than its quality, and would hide real movement.
/// They surface instead as an explicit "cannot extract" list, so the gap stays visible without
/// corrupting the metric. An extractor's <c>MissingFields</c> is what reveals the scope: a
/// field present in every single entry is one that extractor does not attempt.</para>
/// </summary>
public static class CaseScorer
{
    /// <summary>Timestamps inside this window are the same answer. A model that resolves
    /// "7:30pm" an hour off across a DST boundary is a real error, so the window is minutes,
    /// not hours.</summary>
    private static readonly TimeSpan InstantTolerance = TimeSpan.FromMinutes(5);

    public static CaseScore Score(EvalCase expected, ExtractedEvent? actual)
    {
        var outcome = actual is null ? "no_event" : "event";
        var outcomeCorrect = expected.ExpectOutcome.Equals("either", StringComparison.OrdinalIgnoreCase)
                             || outcome.Equals(expected.ExpectOutcome, StringComparison.OrdinalIgnoreCase);

        var correct = new List<string>();
        var wrong = new List<string>();
        var missed = new List<string>();
        var hallucinated = new List<string>();

        // An extractor reports the fields it did not determine; a field it reports for every
        // case is one it never attempts. Split those out before comparing so scope does not
        // masquerade as inaccuracy.
        var unattempted = actual?.MissingFields ?? [];

        if (actual is not null)
        {
            // A case whose key leaves a field null constrains that field: producing a value
            // for it is scored, not skipped. That is what measures invention.
            CompareText("title", expected.Expected.Title, actual.Title, correct, wrong, missed, hallucinated, unattempted);
            CompareInstant("startAt", expected.Expected.StartAt, actual.StartAt, correct, wrong, missed, hallucinated, unattempted);
            CompareInstant("endAt", expected.Expected.EndAt, actual.EndAt, correct, wrong, missed, hallucinated, unattempted);
            CompareText("venueName", expected.Expected.VenueName, actual.VenueName, correct, wrong, missed, hallucinated, unattempted);
            CompareText("address", expected.Expected.Address, actual.Address, correct, wrong, missed, hallucinated, unattempted);
            CompareNumber("maxParticipants", expected.Expected.MaxParticipants?.ToString(),
                actual.MaxParticipants?.ToString(), correct, wrong, missed, hallucinated, unattempted);
            CompareNumber("cost", expected.Expected.Cost?.ToString("0.##"), actual.Cost?.ToString("0.##"),
                correct, wrong, missed, hallucinated, unattempted);
            CompareText("skillLevel", expected.Expected.SkillLevel, actual.SkillLevel?.ToString(),
                correct, wrong, missed, hallucinated, unattempted);
            CompareTags(expected.Expected.Tags, actual.Tags, correct, wrong, missed, hallucinated, unattempted);
        }
        else if (expected.ExpectOutcome == "event")
        {
            // Every stated field becomes a miss. A run that produces nothing must show up in
            // the per-field numbers and not only in the outcome column, otherwise a
            // regress-to-silence prompt looks like a precision improvement.
            foreach (var field in ExpectedFieldNames(expected))
                missed.Add(field);
        }

        return new CaseScore(expected, actual, outcome, outcomeCorrect,
            correct, wrong, missed, hallucinated,
            ExpectedFieldNames(expected).Intersect(unattempted, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>The fields the key populates, used when no draft was produced.</summary>
    private static IEnumerable<string> ExpectedFieldNames(EvalCase c)
    {
        if (c.Expected.Title is not null) yield return "title";
        if (c.Expected.StartAt is not null) yield return "startAt";
        if (c.Expected.EndAt is not null) yield return "endAt";
        if (c.Expected.VenueName is not null) yield return "venueName";
        if (c.Expected.Address is not null) yield return "address";
        if (c.Expected.MaxParticipants is not null) yield return "maxParticipants";
        if (c.Expected.Cost is not null) yield return "cost";
        if (c.Expected.SkillLevel is not null) yield return "skillLevel";
        if (c.Expected.Tags.Count > 0) yield return "tags";
    }

    /// <summary>A field the extractor declared it could not determine is not scored: the
    /// "cannot extract" line reports it instead. This is deliberately gated on the extractor's
    /// own <c>MissingFields</c> rather than on whether the value came back null, so a field it
    /// <em>did</em> attempt and simply failed to read still counts as a miss.</summary>
    private static bool Skipped(string field, IReadOnlyList<string> unattempted)
        => unattempted.Any(f => NormalizeField(f).Equals(field, StringComparison.OrdinalIgnoreCase));

    /// <summary>MissingFields entries can carry an explanation, e.g.
    /// "maxParticipants (500) outside 2-50", so only the leading identifier is compared.</summary>
    private static string NormalizeField(string field)
        => new(field.TakeWhile(char.IsLetterOrDigit).ToArray());

    private static void CompareText(
        string field, string? expected, string? actual,
        List<string> correct, List<string> wrong, List<string> missed, List<string> hallucinated,
        IReadOnlyList<string> unattempted)
    {
        var hasExpected = !string.IsNullOrWhiteSpace(expected);
        var hasActual = !string.IsNullOrWhiteSpace(actual);

        if (!hasExpected && !hasActual) return;
        if (!hasExpected) { hallucinated.Add(field); return; }
        if (Skipped(field, unattempted)) return;
        if (!hasActual) { missed.Add(field); return; }
        (LooseEquals(expected!, actual!) ? correct : wrong).Add(field);
    }

    private static void CompareInstant(
        string field, DateTimeOffset? expected, DateTimeOffset? actual,
        List<string> correct, List<string> wrong, List<string> missed, List<string> hallucinated,
        IReadOnlyList<string> unattempted)
    {
        if (expected is null && actual is null) return;
        if (expected is null) { hallucinated.Add(field); return; }
        if (Skipped(field, unattempted)) return;
        if (actual is null) { missed.Add(field); return; }
        (Math.Abs((expected.Value - actual.Value).TotalMinutes) <= InstantTolerance.TotalMinutes
            ? correct : wrong).Add(field);
    }

    private static void CompareNumber(
        string field, string? expected, string? actual,
        List<string> correct, List<string> wrong, List<string> missed, List<string> hallucinated,
        IReadOnlyList<string> unattempted)
    {
        if (expected is null && actual is null) return;
        if (expected is null) { hallucinated.Add(field); return; }
        if (Skipped(field, unattempted)) return;
        if (actual is null) { missed.Add(field); return; }
        (expected == actual ? correct : wrong).Add(field);
    }

    private static void CompareTags(
        List<string> expected, List<string>? actual,
        List<string> correct, List<string> wrong, List<string> missed, List<string> hallucinated,
        IReadOnlyList<string> unattempted)
    {
        var want = expected.Select(NormalizeTag).Where(t => t.Length > 0).ToHashSet();
        var got = (actual ?? []).Select(NormalizeTag).Where(t => t.Length > 0).ToHashSet();

        if (want.Count == 0 && got.Count == 0) return;
        if (want.Count == 0) { hallucinated.Add("tags"); return; }
        if (Skipped("tags", unattempted)) return;
        if (got.Count == 0) { missed.Add("tags"); return; }

        // Overlap rather than set equality, so a model that returns the email's tags plus one
        // plausible extra is not zeroed out; the extra is visible in the row detail. The
        // precision cost of extras is reported by the report's per-case tags column rather
        // than by failing the field, because tag vocabulary is normalised server-side anyway.
        (got.Overlaps(want) ? correct : wrong).Add("tags");
    }

    private static string NormalizeTag(string tag) => tag.Trim().TrimStart('#').ToLowerInvariant();

    private static bool LooseEquals(string expected, string actual)
        => NormalizeText(expected).Equals(NormalizeText(actual), StringComparison.OrdinalIgnoreCase);

    /// <summary>Collapse whitespace, drop punctuation. So "O'Brien St", "OBrien St" and
    /// " Royal   Park " all read alike. Deliberately blunt — this is scoring, not entity
    /// resolution — and its sloppiness runs the safe direction: it can call two strings equal
    /// that differ cosmetically, never the reverse.</summary>
    private static string NormalizeText(string value)
        => string.Concat(value.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ')).Trim();
}

