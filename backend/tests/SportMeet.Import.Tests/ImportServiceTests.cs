using System.Text.Json;
using Microsoft.Extensions.Options;
using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Application.Imports;
using SportMeet.Application.Ingestion;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

public class ImportServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddDays(30);

    private const string Text = "Social badminton 14 Nov 7:30pm at Seddon Park, 42 Railway Ave, Seddon. 12 spots.";

    private sealed class Rig
    {
        public FakeExtractor Extractor = new();
        public FakeGeocoder Geocoder = new();
        public FakeRepo Repo = new();
        public FakeEvents Events = new();
        public Guid? CurrentUser = UserId;
        public ImportOptions Options = new();
        public int MaxBodyChars = 12_000;

        public ImportService Service => new(
            Extractor, Geocoder, Repo, Events,
            new FakeUser(CurrentUser),
            Microsoft.Extensions.Options.Options.Create(new IngestionOptions { MaxBodyChars = MaxBodyChars }),
            Microsoft.Extensions.Options.Options.Create(Options));
    }

    private static ExtractedEvent Proposal() => new()
    {
        Title = "Social badminton",
        StartAt = Start,
        EndAt = Start.AddHours(2),
        VenueName = "Seddon Park",
        Address = "42 Railway Ave, Seddon",
        MaxParticipants = 12,
        Confidence = 0.9m,
        Model = "gpt-4o-mini",
        PromptVersion = "llm-1",
    };

    // ---- import -------------------------------------------------------------

    [Fact]
    public async Task Text_is_extracted_geocoded_flagged_and_recorded()
    {
        var rig = new Rig();
        rig.Extractor.Result = Proposal();
        rig.Geocoder.Result = new GeocodeResult(-37.81, 144.89, "ROOFTOP", false);

        var dto = await rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, Text));

        Assert.Equal("extracted", dto.Kind);
        Assert.Equal(-37.81, dto.Payload!.Latitude);
        Assert.Equal(144.89, dto.Payload.Longitude);
        Assert.False(dto.Basic);

        var saved = Assert.Single(rig.Repo.Imports);
        Assert.Equal(dto.ImportId, saved.Id);
        Assert.Equal(UserId, saved.UserId);
        Assert.Equal(ImportInputKind.Text, saved.InputKind);
        Assert.Equal(Text, saved.SourceText);
        Assert.Equal("gpt-4o-mini", saved.Model);
        Assert.Contains("Social badminton", saved.ExtractedData);
        Assert.Equal(1, rig.Repo.Saves);
    }

    [Fact]
    public async Task The_extractor_sees_the_text_and_nothing_input_specific_leaks_past_it()
    {
        var rig = new Rig();
        rig.Extractor.Result = Proposal();
        await rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, "  " + Text + "  "));
        Assert.Equal(Text, rig.Extractor.LastBody);
    }

    [Fact]
    public async Task The_first_line_is_passed_as_the_subject_because_pasted_text_has_none()
    {
        var rig = new Rig();
        rig.Extractor.Result = Proposal();
        await rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, "Friday badminton\nSeddon Park 7:30pm"));
        Assert.Equal("Friday badminton", rig.Extractor.LastSubject);
    }

    [Fact]
    public async Task A_heuristic_answer_is_marked_basic()
    {
        var rig = new Rig();
        rig.Extractor.Result = new ExtractedEvent { Title = "x", Model = "heuristic-2" };
        var dto = await rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, Text));
        Assert.True(dto.Basic);
    }

    [Fact]
    public async Task No_event_is_a_normal_outcome_and_is_still_recorded()
    {
        var rig = new Rig();
        rig.Extractor.Result = null;
        var dto = await rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, "Your invoice is attached"));
        Assert.Equal("no_event", dto.Kind);
        Assert.Null(dto.Payload);
        Assert.Equal(ImportStatus.NoEvent, Assert.Single(rig.Repo.Imports).Status);
    }

    [Fact]
    public async Task A_failed_geocode_still_returns_the_draft_with_the_venue_flagged()
    {
        var rig = new Rig();
        rig.Extractor.Result = Proposal();
        rig.Geocoder.Result = null;
        var dto = await rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, Text));
        Assert.Null(dto.Payload!.Latitude);
        Assert.Contains(dto.Flags, f => f is { Field: "venue", Reason: "unconfirmed" });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Empty_input_is_refused_before_the_extractor_runs(string? text)
    {
        var rig = new Rig();
        var ex = await Assert.ThrowsAsync<ImportException>(
            () => rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, text)));
        Assert.Equal(ImportErrorCode.EmptyInput, ex.Code);
        Assert.Equal(0, rig.Extractor.Calls);
        Assert.Empty(rig.Repo.Imports);
    }

    [Theory]
    [InlineData("https://www.eventbrite.com.au/e/some-event-12345", "https://www.eventbrite.com.au/e/some-event-12345")]
    [InlineData("https://www.meetup.com/g/events/1/?utm_source=x&token=secret", "https://www.meetup.com/g/events/1/")]
    [InlineData("  www.humanitix.com/au/e/foo  ", "https://www.humanitix.com/au/e/foo")]
    public async Task A_lone_link_is_refused_and_recorded_as_demand_without_its_query_string(string link, string recorded)
    {
        var rig = new Rig();
        var ex = await Assert.ThrowsAsync<ImportException>(
            () => rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, link)));
        Assert.Equal(ImportErrorCode.UrlNotSupported, ex.Code);
        Assert.Equal(0, rig.Extractor.Calls);
        var row = Assert.Single(rig.Repo.Imports);
        Assert.Equal(ImportStatus.LinkRefused, row.Status);
        Assert.Equal(recorded, row.SourceText);
        Assert.Null(row.ExtractedData);
    }

    [Fact]
    public async Task A_link_inside_real_text_is_fine_and_is_never_fetched()
    {
        var rig = new Rig();
        rig.Extractor.Result = Proposal();
        await rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, Text + " Details: https://example.com/x"));
        Assert.Equal(1, rig.Extractor.Calls);
    }

    [Fact]
    public async Task Text_over_the_cap_is_refused()
    {
        var rig = new Rig { MaxBodyChars = 50 };
        var ex = await Assert.ThrowsAsync<ImportException>(
            () => rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, new string('a', 51))));
        Assert.Equal(ImportErrorCode.TooLong, ex.Code);
    }

    [Fact]
    public async Task The_global_daily_ceiling_refuses_further_imports()
    {
        var rig = new Rig { Options = new ImportOptions { MaxPerDay = 2 } };
        rig.Repo.CountToReturn = 2;
        var ex = await Assert.ThrowsAsync<ImportException>(
            () => rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, Text)));
        Assert.Equal(ImportErrorCode.Busy, ex.Code);
        Assert.Equal(0, rig.Extractor.Calls);
    }

    [Fact]
    public async Task Old_pasted_text_is_cleared_on_import_using_the_retention_window()
    {
        var rig = new Rig { Options = new ImportOptions { SourceTextRetentionDays = 7 } };
        rig.Extractor.Result = Proposal();
        await rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, Text));
        var age = DateTimeOffset.UtcNow - rig.Repo.ClearedBefore!.Value;
        Assert.InRange(age.TotalDays, 6.99, 7.01);
    }

    [Fact]
    public async Task Anonymous_callers_cannot_import()
    {
        var rig = new Rig { CurrentUser = null };
        await Assert.ThrowsAsync<DomainRuleException>(
            () => rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, Text)));
    }

    [Fact]
    public async Task An_input_kind_nobody_implemented_fails_loudly()
    {
        var rig = new Rig();
        await Assert.ThrowsAnyAsync<Exception>(
            () => rig.Service.ImportAsync(new ImportInput((ImportInputKind)99, Text)));
        Assert.Empty(rig.Repo.Imports);
    }

    // ---- publish metrics ----------------------------------------------------

    private static async Task<(Rig rig, ImportDraftDto dto)> ImportedAsync(GeocodeResult? geo = null)
    {
        var rig = new Rig();
        rig.Extractor.Result = Proposal();
        rig.Geocoder.Result = geo ?? new GeocodeResult(-37.81, 144.89, "ROOFTOP", false);
        return (rig, await rig.Service.ImportAsync(new ImportInput(ImportInputKind.Text, Text)));
    }

    [Fact]
    public async Task Publishing_an_import_records_the_final_values_and_which_fields_changed()
    {
        var (rig, dto) = await ImportedAsync();
        var eventId = Guid.NewGuid();
        // The user kept everything except the title and the spot count.
        rig.Events.Published = Published(eventId, title: "Friday badminton", max: 8);

        await rig.Service.RecordPublishAsync(new PublishMetricsRequest(eventId, 21_500, "import", dto.ImportId));

        var import = rig.Repo.Imports.Single();
        Assert.Equal(eventId, import.PublishedEventId);
        var outcomes = JsonSerializer.Deserialize<Dictionary<string, string>>(import.FieldOutcomes!)!;
        Assert.Equal("changed", outcomes["title"]);
        Assert.Equal("changed", outcomes["maxParticipants"]);
        Assert.Equal("kept", outcomes["startAt"]);
        Assert.Equal("kept", outcomes["venueName"]);
        Assert.Contains("Friday badminton", import.FinalData);

        var metric = Assert.Single(rig.Repo.Metrics);
        Assert.Equal("import", metric.Path);
        Assert.Equal(21_500, metric.DurationMs);
        Assert.Equal(dto.ImportId, metric.ImportId);
    }

    [Fact]
    public async Task A_manual_publish_records_only_the_time()
    {
        var rig = new Rig();
        var eventId = Guid.NewGuid();
        rig.Events.Published = Published(eventId);
        await rig.Service.RecordPublishAsync(new PublishMetricsRequest(eventId, 90_000, "manual", null));
        var metric = Assert.Single(rig.Repo.Metrics);
        Assert.Equal("manual", metric.Path);
        Assert.Null(metric.ImportId);
    }

    [Fact]
    public async Task The_word_import_without_a_real_import_is_recorded_as_manual()
    {
        var rig = new Rig();
        var eventId = Guid.NewGuid();
        rig.Events.Published = Published(eventId);
        await rig.Service.RecordPublishAsync(new PublishMetricsRequest(eventId, 5_000, "import", Guid.NewGuid()));
        Assert.Equal("manual", Assert.Single(rig.Repo.Metrics).Path);
    }

    [Fact]
    public async Task Someone_elses_import_cannot_be_attached()
    {
        var (rig, dto) = await ImportedAsync();
        rig.Repo.Imports.Single().UserId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        rig.Events.Published = Published(eventId);
        await rig.Service.RecordPublishAsync(new PublishMetricsRequest(eventId, 5_000, "import", dto.ImportId));
        Assert.Null(rig.Repo.Imports.Single().FinalData);
        Assert.Equal("manual", Assert.Single(rig.Repo.Metrics).Path);
    }

    [Fact]
    public async Task Reporting_twice_counts_once()
    {
        var rig = new Rig();
        var eventId = Guid.NewGuid();
        rig.Events.Published = Published(eventId);
        var request = new PublishMetricsRequest(eventId, 5_000, "manual", null);
        await rig.Service.RecordPublishAsync(request);
        await rig.Service.RecordPublishAsync(request);
        Assert.Single(rig.Repo.Metrics);
    }

    [Fact]
    public async Task An_event_the_caller_does_not_host_is_not_found()
    {
        var rig = new Rig();
        var eventId = Guid.NewGuid();
        rig.Events.Published = Published(eventId, isHost: false);
        await Assert.ThrowsAsync<NotFoundException>(
            () => rig.Service.RecordPublishAsync(new PublishMetricsRequest(eventId, 5_000, "manual", null)));
        Assert.Empty(rig.Repo.Metrics);
    }

    [Fact]
    public async Task An_absurd_duration_is_clamped_and_an_unknown_path_becomes_manual()
    {
        var rig = new Rig();
        var eventId = Guid.NewGuid();
        rig.Events.Published = Published(eventId);
        await rig.Service.RecordPublishAsync(new PublishMetricsRequest(eventId, int.MaxValue, "hax", null));
        var metric = Assert.Single(rig.Repo.Metrics);
        Assert.Equal(86_400_000, metric.DurationMs);
        Assert.Equal("manual", metric.Path);
    }

    private static EventDetailDto Published(
        Guid id, string title = "Social badminton", int max = 12, bool isHost = true) => new()
    {
        Id = id,
        Title = title,
        Tags = [],
        StartAt = Start,
        EndAt = Start.AddHours(2),
        Timezone = "Australia/Melbourne",
        VenueName = "Seddon Park",
        Address = "42 Railway Ave, Seddon",
        Latitude = -37.81,
        Longitude = 144.89,
        MaxParticipants = max,
        JoinedCount = 1,
        InterestedCount = 0,
        Status = "Scheduled",
        IsCancelled = false,
        IsPublished = true,
        Visibility = "Public",
        Host = new ParticipantDto(UserId, "Host", null),
        IsHost = isHost,
        IsJoined = true,
        IsInterested = false,
        Participants = [],
    };

    // ---- fakes --------------------------------------------------------------

    private sealed class FakeUser(Guid? id) : ICurrentUser
    {
        public Guid? UserId => id;
        public bool IsDemo => false;
        // Imports run as ordinary members; nothing in this file tests a privilege.
        public UserRole Role => UserRole.Member;
    }

    private sealed class FakeExtractor : IEventExtractor
    {
        public ExtractedEvent? Result;
        public int Calls;
        public string? LastBody;
        public string? LastSubject;
        public string PromptVersion => "test";
        public string Model => "test";

        public Task<ExtractedEvent?> ExtractAsync(
            string subject, string? sender, string bodyText, DateTimeOffset receivedAt, CancellationToken ct = default)
        {
            Calls++;
            LastBody = bodyText;
            LastSubject = subject;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeGeocoder : IGeocoder
    {
        public GeocodeResult? Result;
        public Task<GeocodeResult?> GeocodeAsync(string? venueName, string? address, CancellationToken ct = default)
            => Task.FromResult(Result);
    }

    private sealed class FakeRepo : IImportRepository
    {
        public readonly List<EventImport> Imports = [];
        public readonly List<EventPublishMetric> Metrics = [];
        public int CountToReturn;
        public int Saves;

        public Task AddAsync(EventImport import, CancellationToken ct = default) { Imports.Add(import); return Task.CompletedTask; }
        public Task<EventImport?> FindAsync(Guid id, Guid userId, CancellationToken ct = default)
            => Task.FromResult(Imports.FirstOrDefault(i => i.Id == id && i.UserId == userId));
        public Task<int> CountSinceAsync(DateTimeOffset since, CancellationToken ct = default) => Task.FromResult(CountToReturn);
        public DateTimeOffset? ClearedBefore;
        public Task ClearSourceTextBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        {
            ClearedBefore = cutoff;
            return Task.CompletedTask;
        }
        public Task<bool> MetricExistsAsync(Guid eventId, CancellationToken ct = default)
            => Task.FromResult(Metrics.Any(m => m.EventId == eventId));
        public Task AddMetricAsync(EventPublishMetric metric, CancellationToken ct = default) { Metrics.Add(metric); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.CompletedTask; }
    }

    private sealed class FakeEvents : IEventService
    {
        public EventDetailDto? Published;

        public Task<EventDetailDto> GetAsync(Guid id, CancellationToken ct = default)
            => Published is { } p && p.Id == id ? Task.FromResult(p) : throw new NotFoundException("Event", id, id);

        public Task<PagedResult<EventListItemDto>> ListAsync(EventQueryModel query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EventDetailDto> CreateAsync(CreateEventDto dto, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EventDetailDto> JoinAsync(Guid eventId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EventDetailDto> LeaveAsync(Guid eventId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> ListMyJoinedAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ToggleInterestAsync(Guid eventId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> ListMyInterestedAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<EventListItemDto>> ListMyHostedAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EventDetailDto> CancelAsync(Guid eventId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EventDetailDto> ReopenAsync(Guid eventId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<EventListItemDto>> ListPendingReviewAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EventDetailDto> ApproveAsync(Guid eventId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EventDetailDto> RejectAsync(Guid eventId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<PopularTag>> ListPopularTagsAsync(int limit = 12, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> SuggestTagsAsync(string? prefix, int limit = 8, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
