using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Application.Ingestion;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Imports;

/// <summary>
/// The pipeline: extract, geocode, flag, record. Only the first step knows what kind of
/// input arrived. After <see cref="ExtractAsync"/> everything works on the
/// <see cref="ExtractedEvent"/>, so adding an input kind touches that one method.
/// </summary>
public sealed partial class ImportService(
    IEventExtractor extractor,
    IGeocoder geocoder,
    IImportRepository imports,
    IEventService events,
    ICurrentUser currentUser,
    IOptions<IngestionOptions> ingestionOptions,
    IOptions<ImportOptions> importOptions) : IImportService
{
    private static readonly string[] Paths = ["manual", "import", "draft"];
    private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // A lone link, which we deliberately do not fetch: link import is not available.
    [GeneratedRegex(@"^\s*(?:https?://|www\.)\S+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LoneUrl();

    public async Task<ImportDraftDto> ImportAsync(ImportInput input, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new DomainRuleException("Sign-in is required to import an event.");
        var now = DateTimeOffset.UtcNow;
        string text;
        try
        {
            text = PrepareText(input);
        }
        catch (ImportException ex) when (ex.Code == ImportErrorCode.UrlNotSupported)
        {
            await Save(NewImport(userId, input.Kind, LinkWithoutQuery(input.Text!), ImportStatus.LinkRefused, 0, now), ct);
            throw;
        }
        if (await imports.CountSinceAsync(now.AddDays(-1), ct) >= importOptions.Value.MaxPerDay)
            throw new ImportException(ImportErrorCode.Busy, "Import is busy right now. Try again shortly, or fill the form in directly.");

        await imports.ClearSourceTextBeforeAsync(now.AddDays(-importOptions.Value.SourceTextRetentionDays), ct);

        var clock = Stopwatch.StartNew();
        var extracted = await ExtractAsync(input.Kind, text, now, ct);

        if (extracted is null)
        {
            var empty = NewImport(userId, input.Kind, text, ImportStatus.NoEvent, clock.ElapsedMilliseconds, now);
            await Save(empty, ct);
            return new ImportDraftDto(empty.Id, "no_event", null, [], null, [], null, false,
                "That doesn't look like an event. Paste the invitation text, or fill the form in directly.");
        }

        // Geocode whatever was read. Failure is an ordinary outcome (null), never an error.
        var geo = extracted.Latitude is null && extracted.Longitude is null
            ? await geocoder.GeocodeAsync(extracted.VenueName, extracted.Address, ct)
            : null;
        var payload = extracted.ToPayload(ingestionOptions.Value.DefaultTimezone, geo?.Latitude, geo?.Longitude);
        var flags = FieldFlagger.Flag(extracted, geo, text, now);

        var import = NewImport(userId, input.Kind, text, ImportStatus.Extracted, clock.ElapsedMilliseconds, now);
        import.ExtractedData = JsonSerializer.Serialize(payload, Json);
        import.Flags = JsonSerializer.Serialize(flags, Json);
        import.Geocode = geo is null ? null : JsonSerializer.Serialize(new GeocodeDto(geo.LocationType, geo.NeedsConfirm), Json);
        import.Confidence = extracted.Confidence;
        import.MissingFields = [.. extracted.MissingFields];
        import.Model = extracted.Model;
        import.PromptVersion = extracted.PromptVersion;
        await Save(import, ct);

        return new ImportDraftDto(
            import.Id, "extracted", extracted.Confidence, extracted.MissingFields, payload, flags,
            geo is null ? null : new GeocodeDto(geo.LocationType, geo.NeedsConfirm),
            Basic: extracted.Model.StartsWith("heuristic", StringComparison.OrdinalIgnoreCase),
            Detail: extracted.Reasoning);
    }

    public async Task RecordPublishAsync(PublishMetricsRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new DomainRuleException("Sign-in is required.");

        // Not the host (or no such event) is a 404 either way: whether someone else's event
        // exists is not this endpoint's to reveal.
        var published = await events.GetAsync(request.EventId, ct);
        if (!published.IsHost) throw new NotFoundException("Event", request.EventId, request.EventId);
        if (await imports.MetricExistsAsync(request.EventId, ct)) return;

        var now = DateTimeOffset.UtcNow;
        var import = request.ImportId is { } importId ? await imports.FindAsync(importId, userId, ct) : null;
        if (import is { PublishedEventId: null, Status: ImportStatus.Extracted, ExtractedData: not null })
        {
            var extracted = JsonSerializer.Deserialize<CreateEventDto>(import.ExtractedData, Json)!;
            var final = FromPublished(published);
            import.PublishedEventId = published.Id;
            import.PublishedAt = now;
            import.FinalData = JsonSerializer.Serialize(final, Json);
            import.FieldOutcomes = JsonSerializer.Serialize(ImportDiff.Compare(extracted, final), Json);
        }
        else
        {
            import = null;
        }

        var path = Array.Find(Paths, p => p == request.Path) ?? "manual";
        await imports.AddMetricAsync(new EventPublishMetric
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventId = published.Id,
            // "import" only when the import was real and ours, so the metric cannot be
            // labelled as an import by a caller who merely sent the word.
            Path = path == "import" && import is null ? "manual" : path,
            DurationMs = Math.Clamp(request.DurationMs, 0, (int)MaxDuration.TotalMilliseconds),
            ImportId = import?.Id,
            CreatedAt = now,
        }, ct);
        await imports.SaveChangesAsync(ct);
    }

    /// <summary>The one place that knows what kind of input this is. Add a case for a new
    /// kind; nothing after this call changes.</summary>
    private Task<ExtractedEvent?> ExtractAsync(
        ImportInputKind kind, string text, DateTimeOffset now, CancellationToken ct)
        => kind switch
        {
            ImportInputKind.Text => extractor.ExtractAsync(FirstLine(text), null, text, now, ct),
            _ => throw new NotSupportedException($"Import input '{kind}' is not supported."),
        };

    /// <summary>The link as scheme, host and path. The query string is dropped: it is where
    /// tracking ids and tokens live, and the host is all the demand measure needs.</summary>
    private static string LinkWithoutQuery(string link)
    {
        var text = link.Trim();
        var absolute = text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + text : text;
        return Uri.TryCreate(absolute, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Path)
            : text[..Math.Min(text.Length, 200)];
    }

    /// <summary>An invitation usually leads with its title, and the fallback extractor takes
    /// its title from the subject, so the first line stands in for one. Pasted text has no
    /// subject of its own.</summary>
    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length <= 200 ? line : line[..200];
    }

    private string PrepareText(ImportInput input)
    {
        var text = input.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            throw new ImportException(ImportErrorCode.EmptyInput, "Paste the event text first.");
        if (LoneUrl().IsMatch(text))
            throw new ImportException(ImportErrorCode.UrlNotSupported,
                "Link import isn't available. Paste the event text instead, or fill the form in directly.");
        var max = ingestionOptions.Value.MaxBodyChars;
        if (text.Length > max)
            throw new ImportException(ImportErrorCode.TooLong, $"That's too long. Paste up to {max:N0} characters.");
        return text;
    }

    private static EventImport NewImport(
        Guid userId, ImportInputKind kind, string text, ImportStatus status, long latencyMs, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        InputKind = kind,
        Status = status,
        SourceText = text,
        LatencyMs = (int)Math.Min(latencyMs, int.MaxValue),
        CreatedAt = now,
    };

    private async Task Save(EventImport import, CancellationToken ct)
    {
        await imports.AddAsync(import, ct);
        await imports.SaveChangesAsync(ct);
    }

    /// <summary>The published event in the extractor's own shape, for comparison.</summary>
    private static CreateEventDto FromPublished(EventDetailDto e) => new()
    {
        Title = e.Title,
        Tags = [.. e.Tags],
        StartAt = e.StartAt,
        EndAt = e.EndAt,
        Timezone = e.Timezone,
        VenueName = e.VenueName,
        Address = e.Address,
        Latitude = e.Latitude,
        Longitude = e.Longitude,
        MaxParticipants = e.MaxParticipants,
        Cost = e.Cost,
        Description = e.Description,
        SkillLevel = e.SkillLevel,
        ThumbnailUrl = e.ThumbnailUrl,
    };
}
