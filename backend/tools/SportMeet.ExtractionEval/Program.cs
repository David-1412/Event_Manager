using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SportMeet.Application.Ingestion;
using SportMeet.ExtractionEval;
using SportMeet.Infrastructure.Ingestion;

// Runs the golden set and prints field-level accuracy.
//
//   dotnet run --project backend/tools/SportMeet.ExtractionEval -- --heuristic
//   dotnet run --project backend/tools/SportMeet.ExtractionEval -- --llm
//   ... -- --heuristic --out report.md --cases <dir> --allow-defects
//
// Two extractors, one report format, because the only way to know whether the LLM earns its
// latency and its cost is to measure it against the thing that is free. --heuristic needs no
// key, so the harness is runnable in CI and on a fresh clone.
//
// Deliberately outside the solution build: it spends API quota, so it should only ever run
// when someone names it.

var parsed = Args.Parse(args);

if (parsed.ShowHelp)
{
    Console.WriteLine(Args.Help);
    return 0;
}

if (parsed.WriteStarterCase)
{
    Console.WriteLine($"Wrote starter case: {Args.WriteStarter(parsed.CasesDir)}");
    Console.WriteLine("Edit the body, then fill expected{} with what it actually states.");
    return 0;
}

if (parsed.ProbePath is { } probePath)
{
    return Probe.Run(probePath);
}

List<EvalCase> cases;
try
{
    cases = EvalCase.LoadDirectory(parsed.CasesDir);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Cannot load cases: {ex.Message}");
    return 2;
}

if (cases.Count == 0)
{
    Console.Error.WriteLine($"No usable cases under {Path.GetFullPath(parsed.CasesDir)}");
    return 2;
}

// Exactly the fallback chain production uses. An evaluator that reached for
// LlmExtractionResponse.Parse directly would quietly measure a copy of the mapping and drift
// from what the API does, which is the usual way these tools stop meaning anything.
var heuristic = new HeuristicEventExtractor();
IEventExtractor extractor;
string label;

if (parsed.UseLlm)
{
    var openAi = WithKeyFromEnvironment(new OpenAiOptions());
    if (!openAi.IsConfigured)
    {
        // Refusing rather than proceeding is the important behaviour here: the extractor
        // falls back on a missing key, so a run like this would otherwise report heuristic
        // numbers under an llm heading and read as a measurement of the model.
        Console.Error.WriteLine(
            "--llm requested but OpenAI is not configured. Set OPENAI_API_KEY, or run --heuristic.\n" +
            "Refusing to run rather than reporting fallback results as model results.");
        return 2;
    }

    extractor = new LlmEventExtractor(
        new SingleClientFactory(new HttpClient { Timeout = TimeSpan.FromSeconds(openAi.TimeoutSeconds + 12) }),
        Options.Create(openAi),
        heuristic,
        NullLogger<LlmEventExtractor>.Instance);
    label = $"{openAi.Model} / {openAi.PromptVersion}";
}
else
{
    extractor = heuristic;
    label = $"{heuristic.Model} / {heuristic.PromptVersion} (no API calls)";
}

Console.WriteLine($"Evaluator: {label}");
Console.WriteLine($"Cases:     {cases.Count} from {Path.GetFullPath(parsed.CasesDir)}");
Console.WriteLine();

var stopwatch = Stopwatch.StartNew();
var scores = new List<CaseScore>(cases.Count);
var exceptions = 0;

foreach (var evalCase in cases)
{
    ExtractedEvent? result;
    try
    {
        // One call shape for both extractors: sender and arrival time are part of
        // IEventExtractor now, so the evaluator cannot accidentally measure a run where the
        // LLM got the arrival date and the heuristic did not.
        result = await extractor.ExtractAsync(
            evalCase.Subject, evalCase.From, evalCase.Body,
            evalCase.ReceivedAt ?? DateTimeOffset.UtcNow);
    }
    catch (Exception ex)
    {
        // A throw is a result, not a crash. Production records an error row and moves on, and
        // a run that dies on case 41 of 50 throws away the 40 it already paid for. It scores
        // as no draft, which is what the pipeline effectively produced.
        exceptions++;
        Console.Error.WriteLine($"  {evalCase.Id}: threw {ex.GetType().Name}: {ex.Message}");
        result = null;
    }

    var score = CaseScorer.Score(evalCase, result);
    scores.Add(score);
    Console.WriteLine($"  {evalCase.Id,-30} {(score.Clean ? "clean" : DetailSummary(score))}");
}

stopwatch.Stop();

var report = EvalReport.Render(scores, label, stopwatch.Elapsed);
Console.WriteLine();
Console.WriteLine(report);

if (parsed.OutPath is { } outPath)
{
    // Explicit UTF-8 without BOM. Windows PowerShell 5.1 has already corrupted this repo's
    // UTF-8 sources once by writing them as ANSI; a tool must not repeat that, since the
    // report contains email bodies that will contain non-ASCII venue and player names.
    File.WriteAllText(outPath, report, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine($"Report written to {Path.GetFullPath(outPath)}");
}

var scored = scores.Where(s => !s.Excluded).ToList();
var compared = scored.Sum(s => s.ComparedCount);
var accuracy = compared == 0 ? 0 : 100.0 * scored.Sum(s => s.CorrectFields.Count) / compared;

// Counted rather than assumed, because this is the number that decides whether the run means
// anything. A run that fell back on every message reports the heuristic's answers under the
// model's name, so the accuracy looks perfectly fine while measuring nothing at all - which is
// what happens when a key is valid but rate-limited, or the endpoint has moved. The extractor
// cannot signal a fallback to its caller (the proposal stays a plain proposal, and an "I
// actually fell back" flag would push a provider concern into every draft consumer), so the
// provenance it *does* record - Model on the result - is what can be counted here.
var viaFallback = scores.Count(s => s.Actual?.Model == heuristic.Model);

Console.WriteLine();
Console.WriteLine(
    $"Field accuracy: {accuracy:0.0}% over {compared} fields in {scored.Count} scored cases   " +
    $"hallucinated: {scored.Sum(s => s.HallucinatedFields.Count)}   " +
    $"exceptions: {exceptions}");

if (parsed.UseLlm)
{
    Console.WriteLine(
        $"Fallbacks:    {viaFallback} of {scores.Count} cases produced by the heuristic, not the model");
    if (viaFallback == scores.Count)
    {
        // Stated plainly, because the alternative is a report that reads like a model result.
        Console.Error.WriteLine(
            "\nWARNING: every case fell back to the heuristic. The accuracy above is the " +
            "heuristic's, printed under the model's name, and says nothing about the model. " +
            "Check the API key, the quota and the endpoint before believing any of it.");
    }
}

// Non-zero when a *scored* case carries a defect, so the heuristic run (deterministic, free)
// can gate CI, while an LLM run opts out with --allow-defects rather than pretending to be
// green. Excluded cases never affect it, so the gate cannot go red on its own.
return parsed.AllowDefects || scored.All(s => s.Clean) ? 0 : 1;

static string DetailSummary(CaseScore s)
{
    var parts = new List<string>();
    if (!s.OutcomeCorrect) parts.Add($"outcome={s.ActualOutcome}");
    if (s.MissedFields.Count > 0) parts.Add($"miss:{string.Join('/', s.MissedFields)}");
    if (s.WrongFields.Count > 0) parts.Add($"wrong:{string.Join('/', s.WrongFields)}");
    if (s.HallucinatedFields.Count > 0) parts.Add($"HALLUCINATED:{string.Join('/', s.HallucinatedFields)}");
    return string.Join("  ", parts);
}

/// <summary>The key comes from the environment only. Never a command-line argument: it would
/// land in shell history and in the CI log, and this tool prints its invocation in reports.
/// Same reason the API reads it from user-secrets rather than appsettings.json.</summary>
static OpenAiOptions WithKeyFromEnvironment(OpenAiOptions source)
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
    return new OpenAiOptions
    {
        BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? source.BaseUrl,
        ApiKey = apiKey,
        Model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? source.Model,
        MaxTokensPerCall = source.MaxTokensPerCall,
        Temperature = source.Temperature,
        TimeoutSeconds = source.TimeoutSeconds,
        PromptVersion = source.PromptVersion,
    };
}

/// <summary>Minimal factory so the evaluator hands the real extractor a real
/// <see cref="IHttpClientFactory"/> without standing up a host. One shared client, so
/// connection pooling across a 50-case run behaves as it does in production.</summary>
internal sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

/// <summary>
/// Prints what the heuristic actually did with one case.
///
/// This exists because the alternative while writing the golden set was reading regexes and
/// reasoning about which of five of them fired first, and that reasoning produced several
/// wrong answer keys before anything ran. Showing the day word, the time span and the clock
/// time the extractor really matched turns "why is startAt wrong" into a five-second look.
///
/// It deliberately reports intermediate matches rather than only the final ExtractedEvent:
/// the final event is what the scored run already shows.
/// </summary>
internal static class Probe
{
    public static int Run(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No case file at {Path.GetFullPath(path)}");
            return 2;
        }

        var one = EvalCase.LoadDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var evalCase = one.FirstOrDefault(c =>
            string.Equals(c.SourceFile, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

        if (evalCase is null)
        {
            Console.Error.WriteLine($"{path} is not a loadable case (or is a _-prefixed template).");
            return 2;
        }

        var body = evalCase.Body;
        var head = evalCase.Subject;

        Console.WriteLine($"case:     {evalCase.Id}");
        Console.WriteLine($"subject:  {head}");
        Console.WriteLine($"received: {evalCase.ReceivedAt:O}");
        Console.WriteLine($"bodyLen:  {body.Length}");
        Console.WriteLine();

        var day = Regex.Match(body,
            @"\b(mon|tues|wednes|thurs|frid|satur|sund)day\b|\btoday\b|\btomorrow\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Console.WriteLine($"DayWord:  {(day.Success ? $"'{day.Value}' @ {day.Index}" : "NO MATCH -> startAt cannot be resolved")}");

        // Same anchoring requirement the production pattern has: a leading boundary is part of
        // the match, which is exactly the thing that silently fails when a time butts up
        // against a word, as in "9:30pm at" versus "9:30pm-9:30pmat".
        var time = Regex.Match(body,
            @"(?:at\s+|^|\s)(\d{1,2})(?::(\d{2}))?\s*(am|pm)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Console.WriteLine(time.Success
            ? $"TimeOfDay:  '{time.Value}' @ {time.Index}  hour={time.Groups[1].Value} min={time.Groups[2].Value} mer={time.Groups[3].Value}"
            : "TimeOfDay:  NO MATCH -> startAt cannot be resolved");

        var span = Regex.Match(body,
            @"(\d{1,2})(?::(\d{2}))?\s*(am|pm)?\s*(?:-|--|to|until|through)\s*(\d{1,2})(?::(\d{2}))?\s*(am|pm)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Console.WriteLine(span.Success
            ? $"TimeRange:  '{span.Value}' -> end {span.Groups[4].Value}:{span.Groups[5].Value}{span.Groups[6].Value}"
            : "TimeRange:  NO MATCH -> endAt will be missing");

        var cap = Regex.Match(body,
            @"\b(?:max(?:imum)?|up to|capped at|limit)\D{0,12}(\d{1,3})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Console.WriteLine(cap.Success
            ? $"Capacity:   '{cap.Value}' -> {cap.Groups[1].Value}"
            : "Capacity:   NO MATCH (extractor supplies its floor value, not a stated one)");

        Console.WriteLine();

        var heuristic = new HeuristicEventExtractor();
        var result = heuristic
            .ExtractAsync(head, null, body, evalCase.ReceivedAt ?? DateTimeOffset.UtcNow)
            .GetAwaiter().GetResult();
        if (result is null)
        {
            Console.WriteLine("RESULT: no event (invitation-signal pre-filter or a hard rule rejected it)");
            return 0;
        }

        Console.WriteLine($"RESULT: title={Show(result.Title)}");
        Console.WriteLine($"        startAt={Show(result.StartAt?.ToString("yyyy-MM-dd HH:mm zzz"))}");
        Console.WriteLine($"        endAt={Show(result.EndAt?.ToString("yyyy-MM-dd HH:mm zzz"))}");
        Console.WriteLine($"        maxParticipants={Show(result.MaxParticipants?.ToString(CultureInfo.InvariantCulture))}");
        Console.WriteLine($"        skillLevel={Show(result.SkillLevel?.ToString())}");
        Console.WriteLine($"        tags={(result.Tags is null ? "(none)" : string.Join(", ", result.Tags))}");
        Console.WriteLine($"        confidence={result.Confidence}");
        Console.WriteLine($"        missing={string.Join(", ", result.MissingFields)}");
        return 0;
    }

    private static string Show(string? value) => value ?? "(null)";
}

/// <summary>Argument handling.
///
/// Hand-rolled rather than a parser package: four switches, and a tool that pulls a CLI
/// framework into the lock file for them makes the lock file the reason it is hard to run on
/// a machine with no network.
///
/// Unknown switches are an error rather than ignored. A typo'd <c>--lle</c> would otherwise
/// run the heuristic and print it as the run someone asked for, which is the same failure as
/// the unconfigured-key case and equally expensive to notice afterwards.</summary>
internal sealed record Args(
    bool UseLlm,
    string CasesDir,
    string? OutPath,
    bool AllowDefects,
    bool ShowHelp,
    bool WriteStarterCase,
    string? ProbePath)
{
    public const string Help = """
        Extraction golden-set evaluator.

        Usage:
          dotnet run --project backend/tools/SportMeet.ExtractionEval -- [switches]

        Switches:
          --heuristic          Score the deterministic extractor. Default; needs no API key.
          --llm                Score the LLM extractor. Requires OPENAI_API_KEY; falls back to
                               the heuristic per message exactly as production does, and the
                               report marks which cases came from the fallback.
          --cases <dir>        Case directory. Default: ./cases next to the executable.
          --out <file>         Also write the markdown report to this path.
          --allow-defects      Always exit 0. Default exits 1 when any case is not clean.
          --init               Write a starter case and exit, for bootstrapping the set.
          --help               This text.

        Environment (only read for --llm):
          OPENAI_API_KEY       Required. Never accepted as an argument.
          OPENAI_BASE_URL      Optional, for an Azure OpenAI or gateway endpoint.
          OPENAI_MODEL         Optional override of the model under test.

        Cases are one JSON file per email under <dir>. See cases/_TEMPLATE.json.
        """;

    public static Args Parse(string[] args)
    {
        var useLlm = false;
        string? casesDir = null;
        string? outPath = null;
        var allowDefects = false;
        var help = false;
        var init = false;
        string? probe = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--llm": useLlm = true; break;
                case "--heuristic": useLlm = false; break;
                case "--allow-defects": allowDefects = true; break;
                case "--help": case "-h": help = true; break;
                case "--init": init = true; break;
                case "--cases":
                    casesDir = Next(args, ref i, "--cases");
                    break;
                case "--out":
                    outPath = Next(args, ref i, "--out");
                    break;
                case "--probe":
                    probe = Next(args, ref i, "--probe");
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown switch '{args[i]}'. Run with --help for the list.");
            }
        }

        // Default resolved relative to the executable rather than the current directory, so
        // `dotnet run` from the repo root finds the golden set. cases\ is copied to the output
        // directory by the project file.
        casesDir ??= Path.Combine(AppContext.BaseDirectory, "cases");

        return new Args(useLlm, casesDir, outPath, allowDefects, help, init, probe);
    }

    private static string Next(string[] args, ref int i, string name)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{name} needs a value");
        return args[++i];
    }

    /// <summary>Starter case, written once and never overwritten. Pointing --cases at an empty
    /// directory otherwise produces "no usable cases" with nothing to go on, and a real
    /// skeleton answers the two questions that actually stall someone adding the first case:
    /// what does an unstated field look like, and where does receivedAt go.</summary>
    public static string WriteStarter(string casesDir)
    {
        Directory.CreateDirectory(casesDir);
        var path = Path.Combine(casesDir, "_TEMPLATE.json");
        if (File.Exists(path)) return path + "  (already existed, left untouched)";

        File.WriteAllText(path, Template, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private const string Template = """
    {
      "_comment": [
        "One file per email. Copy, rename, and edit.",
        "Anything left out of expected{} is a claim that the email does NOT state it:",
        "producing a value there scores as a hallucination, not as unmeasured.",
        "receivedAt is what makes relative dates ('next Friday') have one right answer.",
        "expectOutcome: 'event' | 'no_event' | 'either'. Use no_event for newsletters,",
        "receipts, out-of-office and so on, with expected{} left empty."
      ],
      "id": "template-descriptive-name",
      "kind": "social-invite",
      "subject": "Badminton Friday 7:30pm",
      "from": "club@example.com",
      "receivedAt": "2026-09-28T09:15:00+10:00",
      "body": "Hi all,\n\nBadminton this Friday 7:30pm-9:30pm at Seddon Park, 42 Railway Ave, Seddon.\n$5 a head, badminton provided. Beginners welcome - we need about 12 to make it work.\n\nCheers,\nJo",
      "expectOutcome": "event",
      "expected": {
        "title": "Badminton",
        "startAt": "2026-10-02T19:30:00+10:00",
        "endAt": "2026-10-02T21:30:00+10:00",
        "venueName": "Seddon Park",
        "address": "42 Railway Ave, Seddon",
        "maxParticipants": 12,
        "cost": 5,
        "skillLevel": "Beginner",
        "tags": ["badminton"]
      }
    }
    """;
}

