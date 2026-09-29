using System.Text;

namespace SportMeet.ExtractionEval;

/// <summary>
/// Renders a run.
///
/// Three views rather than one number, because a single accuracy figure cannot answer the
/// question actually asked after a bad run: <i>which field, and in which direction?</i>
/// "78% accurate" says nothing about whether to loosen or tighten the prompt; "startAt 94%,
/// cost hallucinated on 6 cases" is the whole instruction.
///
/// Markdown, so a run pastes into a PR description and prompt changes get reviewed with the
/// numbers that justified them.
/// </summary>
public static class EvalReport
{
    private static readonly string[] FieldOrder =
    [
        "title", "startAt", "endAt", "venueName", "address",
        "maxParticipants", "cost", "skillLevel", "tags",
    ];

    public static string Render(IReadOnlyList<CaseScore> allScores, string extractorLabel, TimeSpan took)
    {
        // Excluded cases are reported in their own section at the end and never enter a
        // denominator. A relative-date case's correct answer moves with the real clock, so it
        // can show a defect today and not tomorrow with no code change; letting that into a
        // trend line is how a golden set teaches people to ignore the trend line.
        var excluded = allScores.Where(s => s.Excluded).ToList();
        var scores = allScores.Where(s => !s.Excluded).ToList();

        var sb = new StringBuilder();
        var casesWithDraft = scores.Count(s => s.Actual is not null);

        var totalCompared = scores.Sum(s => s.ComparedCount);
        var totalCorrect = scores.Sum(s => s.CorrectFields.Count);
        var totalWrong = scores.Sum(s => s.WrongFields.Count);
        var totalMissed = scores.Sum(s => s.MissedFields.Count);
        var totalHallucinated = scores.Sum(s => s.HallucinatedFields.Count);
        var outcomeErrors = scores.Count(s => !s.OutcomeCorrect);
        var cleanCases = scores.Count(s => s.Clean);

        sb.AppendLine("# Extraction evaluation");
        sb.AppendLine();
        sb.AppendLine($"- Extractor: `{extractorLabel}`");
        sb.AppendLine($"- Cases: {scores.Count} scored, {excluded.Count} excluded as time-sensitive " +
                      $"({casesWithDraft} produced a draft, " +
                      $"{scores.Count - casesWithDraft} produced none)");
        sb.AppendLine($"- Duration: {took.TotalSeconds:0.0}s");
        sb.AppendLine();

        sb.AppendLine("## Headline");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | ---: |");
        sb.AppendLine($"| **Field accuracy** | **{Pct(totalCorrect, totalCompared)}** |");
        sb.AppendLine($"| Fields compared | {totalCompared} |");
        sb.AppendLine($"| Correct | {totalCorrect} |");
        sb.AppendLine($"| Wrong value | {totalWrong} |");
        sb.AppendLine($"| Missed (email stated it, draft left it null) | {totalMissed} |");
        sb.AppendLine($"| **Hallucinated (draft filled a field the email never stated)** | {totalHallucinated} |");
        sb.AppendLine($"| Outcome errors (event vs no_event) | {outcomeErrors} |");
        sb.AppendLine($"| Cases with no defect of any kind | {cleanCases} / {scores.Count} |");
        sb.AppendLine();

        // Called out on its own because it is the one number that should block a prompt
        // change by itself. Accuracy can rise while this rises, which is a regression wearing
        // an improvement's clothes: fewer blanks for the reviewer, and the invented values
        // read as extracted rather than guessed.
        if (totalHallucinated > 0)
        {
            sb.AppendLine($"> **{totalHallucinated} hallucinated field(s).** Each one became a");
            sb.AppendLine("> specific-looking value in a draft. Read these before the accuracy");
            sb.AppendLine("> figure, which usually improves when they do.");
            sb.AppendLine();
        }

        sb.AppendLine("## By field");
        sb.AppendLine();
        sb.AppendLine("| Field | Compared | Correct | Accuracy | Missed | Wrong | Hallucinated |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var field in FieldOrder)
        {
            var compared = scores.Sum(s => Count(s, field));
            if (compared == 0) continue;

            var correct = scores.Sum(s => s.CorrectFields.Count(f => f == field));
            sb.AppendLine(
                $"| {field} | {compared} | {correct} | {Pct(correct, compared)} | " +
                $"{scores.Sum(s => s.MissedFields.Count(f => f == field))} | " +
                $"{scores.Sum(s => s.WrongFields.Count(f => f == field))} | " +
                $"{scores.Sum(s => s.HallucinatedFields.Count(f => f == field))} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Not measured");
        sb.AppendLine();
        sb.AppendLine("Fields the answer key constrains that this extractor declares it cannot");
        sb.AppendLine("determine. Excluded from accuracy above, because they describe its scope");
        sb.AppendLine("rather than its quality - but they are real gaps a reviewer still has to");
        sb.AppendLine("fill by hand, so they are listed rather than dropped.");
        sb.AppendLine();
        var unattemptedTally = scores
            .SelectMany(s => s.UnattemptedFields.Select(Normalize))
            .GroupBy(f => f)
            .OrderByDescending(g => g.Count())
            .ToList();
        if (unattemptedTally.Count == 0)
        {
            sb.AppendLine("_None - every constrained field was attempted._");
        }
        else
        {
            sb.AppendLine("| Field | Cases where not attempted |");
            sb.AppendLine("| --- | ---: |");
            foreach (var group in unattemptedTally)
                sb.AppendLine($"| {group.Key} | {group.Count()} |");
        }
        sb.AppendLine();

        sb.AppendLine("## By case kind");
        sb.AppendLine();
        sb.AppendLine("| Kind | Cases | Correct | Accuracy | Hallucinated | Outcome errors |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var group in scores.GroupBy(s => s.Case.Kind).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var compared = group.Sum(s => s.ComparedCount);
            var correct = group.Sum(s => s.CorrectFields.Count);
            sb.AppendLine(
                $"| {group.Key} | {group.Count()} | {correct} | {Pct(correct, compared)} | " +
                $"{group.Sum(s => s.HallucinatedFields.Count)} | {group.Count(s => !s.OutcomeCorrect)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Cases");
        sb.AppendLine();
        sb.AppendLine("Defective cases first; a starred outcome is the extractor calling an " +
                      "invitation junk, or junk an invitation.");
        sb.AppendLine();
        sb.AppendLine("| Case | Kind | Outcome | Correct | Missed | Wrong | Hallucinated |");
        sb.AppendLine("| --- | --- | :---: | ---: | --- | --- | --- |");
        foreach (var s in scores.OrderBy(s => s.Clean).ThenBy(s => s.Case.Id, StringComparer.Ordinal))
        {
            var outcome = s.OutcomeCorrect
                ? s.ActualOutcome
                : $"**{s.ActualOutcome}** (want {s.Case.ExpectOutcome})";
            sb.AppendLine(
                $"| {s.Case.Id} | {s.Case.Kind} | {outcome} | {s.CorrectFields.Count}/{s.ComparedCount} | " +
                $"{Join(s.MissedFields)} | {Join(s.WrongFields)} | {Join(s.HallucinatedFields)} |");
        }
        sb.AppendLine();

        var failures = scores.Where(s => !s.Clean).ToList();
        if (failures.Count > 0)
        {
            sb.AppendLine("## Defect detail");
            sb.AppendLine();
            sb.AppendLine("Expected against produced, for every case carrying a defect. This is");
            sb.AppendLine("the part worth reading, and the part a percentage hides.");
            sb.AppendLine();
            foreach (var s in failures)
            {
                sb.AppendLine($"### {s.Case.Id} ({s.Case.Kind})");
                sb.AppendLine();
                sb.AppendLine("```json");
                sb.AppendLine(Detail.For(s));
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        if (excluded.Count > 0)
        {
            sb.AppendLine("## Excluded (time-sensitive)");
            sb.AppendLine();
            sb.AppendLine("Cases whose body states a relative date. Their correct answer moves as");
            sb.AppendLine("the real clock advances, and the pipeline refuses to draft an event that");
            sb.AppendLine("has already finished, so these will report a defect purely through the");
            sb.AppendLine("passage of time. Kept as readable probes and as --probe material; not");
            sb.AppendLine("part of any figure above.");
            sb.AppendLine();
            sb.AppendLine("| Case | Kind | Outcome | Missed | Wrong | Hallucinated |");
            sb.AppendLine("| --- | --- | :---: | --- | --- | --- |");
            foreach (var s in excluded.OrderBy(s => s.Case.Id, StringComparer.Ordinal))
            {
                sb.AppendLine(
                    $"| {s.Case.Id} | {s.Case.Kind} | {s.ActualOutcome} | {Join(s.MissedFields)} | " +
                    $"{Join(s.WrongFields)} | {Join(s.HallucinatedFields)} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Join(IReadOnlyList<string> values)
        => values.Count == 0 ? "—" : string.Join(", ", values);

    /// <summary>Report the bare field name, dropping any explanation the extractor attached
    /// ("maxParticipants (500) outside 2-50" groups under "maxParticipants").</summary>
    private static string Normalize(string field)
        => new(field.TakeWhile(char.IsLetterOrDigit).ToArray());

    private static int Count(CaseScore s, string field)
        => s.CorrectFields.Count(f => f == field)
           + s.WrongFields.Count(f => f == field)
           + s.MissedFields.Count(f => f == field)
           + s.HallucinatedFields.Count(f => f == field);

    /// <summary>An empty denominator prints "n/a", not 0%. A field no case constrains is not
    /// a field the extractor failed, and conflating the two is how a gap in the golden set
    /// gets read as a result.</summary>
    private static string Pct(int part, int whole)
        => whole == 0 ? "n/a" : $"{100.0 * part / whole:0.0}%";
}

