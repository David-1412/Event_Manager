using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Application.Notifications;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;
using Xunit;

namespace SportMeet.Import.Tests;

/// <summary>
/// The shape of an actual HTTP response body from
/// <c>GET /api/events/{id}/join-requests</c>, read as text off the wire.
///
/// <see cref="JoinRequestStatusWireTests"/> serializes the DTO with options built
/// the way Program.cs builds them, which catches a missing converter but not a
/// converter that exists and is registered somewhere the MVC pipeline never reads.
/// This goes through the real pipeline — routing, the controller, the MVC output
/// formatter — so the assertion covers the bytes a browser's <c>fetch</c> sees,
/// which is the only surface where the original bug was observable at all.
///
/// The service is the real <see cref="EventService"/> over an in-memory repository,
/// so the projection that produces the DTO is exercised and not only its
/// serialization. No database: migrations and seeding are configuration-gated off
/// and the repository the service would resolve is replaced.
///
/// Requires the ASP.NET Core 8 shared framework. A machine with only a newer one
/// still compiles these net8.0 projects and rolls the app forward onto it, but
/// System.Text.Json from the newer framework refuses the TestHost response pipe
/// ("ResponseBodyPipeWriter does not implement PipeWriter.UnflushedBytes") and every
/// response body fails to write — the first request the factory makes, whatever it
/// is, including one that touches no database. That is a property of the machine and
/// not of the wire contract. CI installs 8.0.x alongside 10.0.x (see
/// .github/workflows/ci.yml), so these run for real there.
/// </summary>
public sealed class JoinRequestWireShapeApiTests : IDisposable
{
    private static readonly Guid EventId = Guid.Parse("11111111-1111-4111-8111-000000000007");
    private static readonly Guid HostId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid RequesterId = Guid.Parse("aba69028-221e-4bf8-8731-f77a9023eb8b");
    private static readonly DateTimeOffset Asked = new(2030, 5, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public JoinRequestWireShapeApiTests() => _factory = CreateFactory();

    public void Dispose() => _factory.Dispose();

    /// <summary>A host over an in-memory queue holding one pending request, built per
    /// test instance rather than shared: approving consumes that request, so a test
    /// asserting on a pending row must not depend on which test ran first.
    ///
    /// <paramref name="sharedRepo"/> registers the queue as a singleton instead of per
    /// scope, which the approve tests need — they approve in one request and read the
    /// result in the next, and a fresh fake per scope would forget the decision.
    ///
    /// Overrides the acting identity with <paramref name="asUserId"/>; null keeps the
    /// stack's own fallback, which is <c>Demo:AsUserId</c>. Needed because the detail
    /// projection is deliberately host-blind on <c>myJoinRequestStatus</c> — a host
    /// never has a join request of their own — so asserting that field means looking
    /// at the event as the member who asked.</summary>
    private static WebApplicationFactory<Program> CreateFactory(
        bool sharedRepo = false, Guid? asUserId = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Migrations and seeding stay off so startup never reaches for Postgres,
            // and Firebase/ingestion stay unconfigured so neither installs itself.
            // Demo:AsUserId is how the real stack supplies an identity without a
            // token, which is exactly the path this endpoint runs on locally.
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Database:AutoMigrate"] = "false",
                    ["Database:SeedDemoData"] = "false",
                    ["Demo:AsUserId"] = (asUserId ?? HostId).ToString(),
                }));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEventRepository>();
                services.RemoveAll<INotificationRepository>();
                if (sharedRepo)
                {
                    services.AddSingleton<IEventRepository>(_ => Repo());
                }
                else
                {
                    services.AddScoped<IEventRepository>(_ => Repo());
                }
                services.AddScoped<INotificationRepository>(_ => new NoNotifications());
            });

            // The environment's default is Development, whose developer exception page
            // writes its own response and dies on that under the test host, masking
            // the real cause. Production routes failures through ExceptionMiddleware.
            builder.UseEnvironment("Production");
        });

    /// <summary>A gated event owned by the demo identity, with one pending request
    /// against it — the exact state the host's panel renders.</summary>
    private static FakeJoinRepo Repo()
    {
        var entity = new Event
        {
            Id = EventId,
            HostId = HostId,
            Host = new User { Id = HostId, Name = "Demo Host" },
            Title = "Tennis Saturday",
            VenueName = "Court 3",
            Timezone = "Australia/Melbourne",
            StartAt = Asked.AddDays(1),
            EndAt = Asked.AddDays(1).AddHours(2),
            MaxParticipants = 4,
            Status = EventStatus.Scheduled,
            Visibility = EventVisibility.Public,
            RequiresJoinApproval = true,
        };

        var repo = new FakeJoinRepo(entity) { ActingHost = HostId };
        repo.Requests.Add(new EventJoinRequest
        {
            EventId = EventId,
            UserId = RequesterId,
            Status = JoinRequestStatus.Pending,
            RequestedAt = Asked,
        });
        return repo;
    }

    // ---- The response a browser would receive ------------------------------

    /// <summary>The bug, asserted against the real response body: this property was
    /// <c>"status": 0</c>. The frontend compares against the name, so the equality
    /// was false and join-requests-panel.tsx rendered a badge where the host's
    /// Approve/Reject buttons belong.</summary>
    [Fact]
    public async Task Join_requests_response_carries_status_as_a_string()
    {
        var client = _factory.CreateClient();

        var body = await client.GetStringAsync($"/api/events/{EventId}/join-requests");

        using var json = JsonDocument.Parse(body);
        var items = json.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());

        var status = items[0].GetProperty("status");
        Assert.Equal(JsonValueKind.String, status.ValueKind);
        Assert.Equal("Pending", status.GetString());
    }

    /// <summary>The whole row as the client consumes it, so a fix that moved the
    /// breakage to a neighbouring field would fail here. camelCase naming is part of
    /// the contract and is asserted rather than assumed.</summary>
    [Fact]
    public async Task The_row_keeps_every_field_the_panel_reads()
    {
        var client = _factory.CreateClient();

        var body = await client.GetStringAsync($"/api/events/{EventId}/join-requests");

        using var json = JsonDocument.Parse(body);
        Assert.Equal(1, json.RootElement.GetProperty("pendingCount").GetInt32());

        var row = json.RootElement.GetProperty("items")[0];
        Assert.Equal(RequesterId, row.GetProperty("requester").GetProperty("id").GetGuid());
        Assert.Equal("Requester", row.GetProperty("requester").GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("requester").GetProperty("avatarUrl").ValueKind);
        Assert.Equal(Asked, row.GetProperty("requestedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("decidedAt").ValueKind);
    }

    /// <summary>Detail-page half of the same contract: a member with an open request
    /// must read <c>"myJoinRequestStatus": "Pending"</c>, or the "Request submitted"
    /// state never appears and the Join button stays live next to a request that
    /// already exists.</summary>
    [Fact]
    public async Task Event_detail_carries_my_join_request_status_as_a_string()
    {
        // Seen as the member who asked: the projection answers null for the host by
        // design, so a host-viewing test would prove nothing about this field.
        using var factory = CreateFactory(asUserId: RequesterId);
        var client = factory.CreateClient();

        var body = await client.GetStringAsync($"/api/events/{EventId}");

        using var json = JsonDocument.Parse(body);
        var mine = json.RootElement.GetProperty("myJoinRequestStatus");

        Assert.Equal(JsonValueKind.String, mine.ValueKind);
        Assert.Equal("Pending", mine.GetString());
    }

    /// <summary>A decided request must say so by name too — the panel shows a badge
    /// whose colour is chosen by comparing this value.</summary>
    [Fact]
    public async Task An_approved_request_reads_as_Approved_when_decided_are_included()
    {
        // A single client over a single repository instance: approving has to survive
        // to the next request in the same test, which a per-scope fake would drop.
        using var factory = CreateFactory(sharedRepo: true);
        var client = factory.CreateClient();

        await client.PostAsync(
            $"/api/events/{EventId}/join-requests/{RequesterId}/approve", null);
        var body = await client.GetStringAsync(
            $"/api/events/{EventId}/join-requests?includeDecided=true");

        using var json = JsonDocument.Parse(body);
        var status = json.RootElement.GetProperty("items")[0].GetProperty("status");

        Assert.Equal(JsonValueKind.String, status.ValueKind);
        Assert.Equal("Approved", status.GetString());
        // Decided rows are no longer waiting on anyone.
        Assert.Equal(0, json.RootElement.GetProperty("pendingCount").GetInt32());
    }

    /// <summary>The approve response is itself the new list, and the panel re-renders
    /// from it — so the verb's response body has to carry the same wire form.</summary>
    [Fact]
    public async Task The_approve_response_uses_the_same_wire_form()
    {
        using var factory = CreateFactory(sharedRepo: true);
        var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/events/{EventId}/join-requests/{RequesterId}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"status\":\"Approved\"", body);
        Assert.DoesNotContain("\"status\":1", body);
    }

    private sealed class NoNotifications : INotificationRepository
    {
        public Task<(IReadOnlyList<NotificationDto> Items, int UnreadCount)> ListForUserAsync(
            Guid userId, int limit, CancellationToken ct = default)
            => Task.FromResult<(IReadOnlyList<NotificationDto> Items, int UnreadCount)>(
                (Array.Empty<NotificationDto>(), 0));

        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task AddAsync(Notification notification, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
