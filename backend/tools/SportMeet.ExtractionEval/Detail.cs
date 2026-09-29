using System.Text.Json;
using System.Text.Json.Nodes;

namespace SportMeet.ExtractionEval;

/// <summary>The expected/produced pair for one defective case.
///
/// Both sides in one object, keyed identically, so the eye diffs them without a script.
/// Produced values are the extractor's, unedited — including the ones it should not have
/// produced, which is the point of printing them next to a null in the expected block.</summary>
internal static class Detail
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static string For(CaseScore s)
    {
        var actual = s.Actual;
        var detail = new JsonObject
        {
            ["case"] = s.Case.Id,
            // Printed first and above the diff: it is the reason the defect is still here
            // rather than a number someone should chase.
            ["note"] = s.Case.Note,
            ["missed"] = ToArray(s.MissedFields),
            ["hallucinated"] = ToArray(s.HallucinatedFields),
            ["wrong"] = ToArray(s.WrongFields),
            ["expected"] = new JsonObject
            {
                ["outcome"] = s.Case.ExpectOutcome,
                ["title"] = s.Case.Expected.Title,
                ["startAt"] = Format(s.Case.Expected.StartAt),
                ["endAt"] = Format(s.Case.Expected.EndAt),
                ["venueName"] = s.Case.Expected.VenueName,
                ["address"] = s.Case.Expected.Address,
                ["maxParticipants"] = s.Case.Expected.MaxParticipants,
                ["cost"] = s.Case.Expected.Cost,
                ["skillLevel"] = s.Case.Expected.SkillLevel,
                ["tags"] = ToArray(s.Case.Expected.Tags),
            },
            ["produced"] = actual is null
                ? JsonValue.Create("no_event")
                : new JsonObject
                {
                    ["outcome"] = "event",
                    ["title"] = actual.Title,
                    ["startAt"] = Format(actual.StartAt),
                    ["endAt"] = Format(actual.EndAt),
                    ["venueName"] = actual.VenueName,
                    ["address"] = actual.Address,
                    ["maxParticipants"] = actual.MaxParticipants,
                    ["cost"] = actual.Cost,
                    ["skillLevel"] = actual.SkillLevel?.ToString(),
                    ["tags"] = ToArray(actual.Tags ?? []),
                    ["confidence"] = actual.Confidence,
                    ["model"] = actual.Model,
                    // The reviewer of a bad run needs this: it distinguishes "the model said
                    // this" from "the fallback said this", which changes the fix entirely.
                    ["viaFallback"] = actual.Model == "heuristic",
                },
        };

        return detail.ToJsonString(WriteOptions);
    }

    /// <summary>ISO with offset preserved. Printed as an offset-bearing instant rather than
    /// UTC so a wrong-zone answer is visible as a different offset instead of looking like a
    /// same-day time slip.</summary>
    private static string? Format(DateTimeOffset? value)
        => value?.ToString("yyyy-MM-ddTHH:mm:sszzz");

    private static JsonArray ToArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values) array.Add(JsonValue.Create(value));
        return array;
    }
}
