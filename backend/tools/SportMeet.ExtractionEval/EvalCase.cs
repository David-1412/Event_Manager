using System.Text.Json;
using System.Text.Json.Serialization;
using SportMeet.Application.Ingestion;

namespace SportMeet.ExtractionEval;

/// <summary>
/// One golden-set case: a real email plus the fields a human decided it states.
///
/// The expected block is deliberately sparse. Every omitted field means "the email does not
/// state this", so a model that invents a venue for an email without one is scored as wrong
/// rather than going unmeasured — the failure mode the whole pipeline is built around. An
/// empty expected block is therefore a strong claim: it says "nothing here is extractable",
/// which is exactly right for a receipt or a newsletter.
/// </summary>
public sealed class EvalCase
{
    public required string Id { get; init; }

    /// <summary>What it is, for the report's grouping, and to stop a folder of invitations
    /// quietly becoming the whole set.</summary>
    public required string Kind { get; init; }

    public string Subject { get; init; } = "";
    public string From { get; init; } = "";
    public string Body { get; init; } = "";

    /// <summary>When the mail arrived, so relative dates ("next Friday") have one correct
    /// answer <em>relative to the message</em>.
    ///
    /// <b>Cases that rely on it cannot be scored, and the file should say so.</b> An email
    /// naming a weekday or "tomorrow" resolves to a date, but whether that date is still ahead
    /// of us changes as the real clock advances, and the pipeline deliberately refuses to draft
    /// an event that has already finished. So a relative-date case starts with a correct key
    /// and becomes a stale-date case a few days later without anything being edited. Setting
    /// <c>"timeSensitive": true</c> keeps the file as a readable probe while excluding it from
    /// the accuracy figure; a scored case must state an absolute date in its body instead.
    ///
    /// Cases with no relative date in the body are unaffected: their answer never moves.</summary>
    public DateTimeOffset? ReceivedAt { get; init; }

    /// <summary>Excluded from accuracy; see <see cref="ReceivedAt"/>.</summary>
    public bool TimeSensitive { get; init; }

    public ExpectedFields Expected { get; init; } = new();

    /// <summary>Expected extraction outcome: "event", "no_event", or "either".</summary>
    public string ExpectOutcome { get; init; } = "event";

    /// <summary>Why this case exists, and for a known-failing case *why it is allowed to
    /// fail*. Printed in the defect detail, because a red case with no explanation gets its
    /// expected value edited until it goes green - which silently deletes the only record that
    /// the behaviour was deliberate. A case that documents its own failure is the one that
    /// survives as a measurement instead of becoming a decoration.</summary>
    public string? Note { get; init; }

    /// <summary>Where this case was loaded from, for error messages. Ignored on read by the
    /// attribute below, so a case file cannot forge its own path. Needs a setter rather than
    /// <c>init</c> because the value is only known after the deserializer has constructed the
    /// object.</summary>
    [JsonIgnore]
    public string SourceFile { get; set; } = "";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static List<EvalCase> LoadDirectory(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(
                $"No case directory at {Path.GetFullPath(path)}. Pass --init to create a starter case.");

        var cases = new List<EvalCase>();
        foreach (var file in Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories)
                     // _ prefixes a file out of the run, so a documented template can live in
                     // the same directory as the set instead of somewhere it will be forgotten.
                     .Where(f => !Path.GetFileName(f).StartsWith('_'))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            EvalCase? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<EvalCase>(File.ReadAllText(file), ReadOptions);
            }
            catch (JsonException ex)
            {
                // One malformed case must not abort a run that may have spent real quota
                // already; it is reported and skipped like any other unusable input.
                Console.Error.WriteLine($"  skipping {Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Body))
            {
                Console.Error.WriteLine($"  skipping {Path.GetFileName(file)}: no body");
                continue;
            }

            parsed.SourceFile = file;
            cases.Add(parsed);
        }

        return cases;
    }
}

/// <summary>The human-authored answer key. Null means "not stated in the email".</summary>
public sealed class ExpectedFields
{
    public string? Title { get; init; }
    public DateTimeOffset? StartAt { get; init; }
    public DateTimeOffset? EndAt { get; init; }
    public string? VenueName { get; init; }
    public string? Address { get; init; }
    public int? MaxParticipants { get; init; }
    public decimal? Cost { get; init; }
    public string? SkillLevel { get; init; }

    /// <summary>Compared as a set, case- and order-insensitively, and only against the
    /// subset the email genuinely names. Empty means "no tags expected" — a model that tags
    /// an email with no activity word in it is wrong here.</summary>
    public List<string> Tags { get; init; } = [];
}
