using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportMeet.Api.Controllers;
using SportMeet.Application.Common;
using SportMeet.Application.Imports;
using SportMeet.Infrastructure.Identity;

namespace SportMeet.Import.Tests;

/// <summary>
/// Regression tests for the gate on private endpoints.
///
/// The bug: <c>RequireAuthenticatedUserAttribute</c> was a reusable filter factory that
/// captured the scoped <c>ICurrentUser</c> of the FIRST request it served. That object
/// caches its answer, so the first caller of each endpoint decided the verdict for every
/// later caller until the API restarted. An anonymous first request locked everyone out
/// (a signed-in user got 401); an authenticated first request let anonymous callers
/// through. Neither is visible in a test that makes one request, which is why these make
/// several, in both orders, against one running host.
///
/// Real: the controllers, the attribute and <c>TokenCurrentUser</c>, on a real Kestrel port.
/// Replaced: Firebase JWT validation (a test scheme trusting an <c>X-Test-Uid</c> header),
/// the uid-to-user store, and the import service (so no model or database is involved).
/// </summary>
public class RequireAuthenticatedUserTests
{
    private const string Import = "/api/imports/text";
    private const string Metrics = "/api/publish-metrics";

    [Theory]
    [InlineData(Import)]
    [InlineData(Metrics)]
    public async Task Anonymous_then_authenticated_is_refused_then_allowed(string route)
    {
        await using var api = await TestApi.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, await api.Post(route, uid: null));
        Assert.Equal(Allowed(route), await api.Post(route, uid: "alice"));
    }

    [Theory]
    [InlineData(Import)]
    [InlineData(Metrics)]
    public async Task Authenticated_then_anonymous_is_allowed_then_refused(string route)
    {
        await using var api = await TestApi.StartAsync();
        Assert.Equal(Allowed(route), await api.Post(route, uid: "alice"));
        Assert.Equal(HttpStatusCode.Unauthorized, await api.Post(route, uid: null));
    }

    [Fact]
    public async Task The_verdict_follows_each_request_not_the_first_one()
    {
        await using var api = await TestApi.StartAsync();
        var sequence = new (string? Uid, HttpStatusCode Expected)[]
        {
            (null, HttpStatusCode.Unauthorized),
            ("alice", HttpStatusCode.OK),
            (null, HttpStatusCode.Unauthorized),
            ("bob", HttpStatusCode.OK),
            ("alice", HttpStatusCode.OK),
            (null, HttpStatusCode.Unauthorized),
        };
        foreach (var (uid, expected) in sequence)
            Assert.Equal(expected, await api.Post(Import, uid));
    }

    [Fact]
    public async Task One_users_identity_never_reaches_the_next_request()
    {
        await using var api = await TestApi.StartAsync();
        await api.Post(Import, "alice");
        await api.Post(Import, "bob");

        // The service saw each caller as themselves: two different, verified users, and
        // never the configured demo identity.
        Assert.Equal(2, api.Seen.Count);
        Assert.NotEqual(api.Seen[0], api.Seen[1]);
        Assert.Equal(TestApi.IdOf("alice"), api.Seen[0]);
        Assert.Equal(TestApi.IdOf("bob"), api.Seen[1]);
        Assert.DoesNotContain(TestApi.DemoId, api.Seen);
    }

    [Fact]
    public async Task A_refused_request_never_reaches_the_service()
    {
        await using var api = await TestApi.StartAsync();
        await api.Post(Import, "alice");
        await api.Post(Import, uid: null);
        await api.Post(Import, uid: null);
        Assert.Single(api.Seen);
    }

    private static HttpStatusCode Allowed(string route)
        => route == Metrics ? HttpStatusCode.NoContent : HttpStatusCode.OK;

    // ---- host ---------------------------------------------------------------

    private sealed class TestApi : IAsyncDisposable
    {
        public static readonly Guid DemoId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        private readonly WebApplication _app;
        private readonly HttpClient _http = new();
        private readonly string _baseUrl;

        /// <summary>The user id the import service observed on each call it received.</summary>
        public List<Guid?> Seen { get; }

        private TestApi(WebApplication app, string baseUrl, List<Guid?> seen)
        {
            _app = app;
            _baseUrl = baseUrl;
            Seen = seen;
        }

        public static Guid IdOf(string uid)
            => new(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(uid)));

        public static async Task<TestApi> StartAsync()
        {
            var seen = new List<Guid?>();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            // As in production: an anonymous caller resolves to the configured demo identity.
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Demo:AsUserId"] = DemoId.ToString(),
            });

            builder.Services.AddControllers().AddApplicationPart(typeof(ImportsController).Assembly);
            builder.Services.AddHttpContextAccessor();
            builder.Services.Configure<DemoUserOptions>(builder.Configuration.GetSection("Demo"));
            builder.Services.AddScoped<ICurrentUser, TokenCurrentUser>();
            builder.Services.AddScoped<IUserStore, FakeUserStore>();
            builder.Services.AddSingleton(seen);
            builder.Services.AddScoped<IImportService, RecordingImportService>();
            builder.Services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, HeaderAuth>("Test", _ => { });

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();

            var url = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return new TestApi(app, url, seen);
        }

        public async Task<HttpStatusCode> Post(string route, string? uid)
        {
            var body = route == Metrics
                ? """{"eventId":"00000000-0000-0000-0000-000000000001","durationMs":1,"path":"manual"}"""
                : """{"text":"x"}""";
            var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + route)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (uid is not null) request.Headers.Add("X-Test-Uid", uid);
            using var response = await _http.SendAsync(request);
            return response.StatusCode;
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class HeaderAuth(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Uid", out var uid))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim(FirebaseJwtBearerEvents.AuthUidClaimType, uid.ToString())], "Test");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private sealed class FakeUserStore : IUserStore
    {
        public Task<Guid> ResolveUserIdAsync(VerifiedIdentity identity, CancellationToken ct = default)
            => Task.FromResult(TestApi.IdOf(identity.AuthUid));
    }

    /// <summary>Records who the service believes the caller is. It receives the same scoped
    /// <c>ICurrentUser</c> the real service would, so a stale identity shows up here.</summary>
    private sealed class RecordingImportService(ICurrentUser user, List<Guid?> seen) : IImportService
    {
        public Task<ImportDraftDto> ImportAsync(ImportInput input, CancellationToken ct = default)
        {
            seen.Add(user.UserId);
            return Task.FromResult(new ImportDraftDto(Guid.NewGuid(), "extracted", 0.9m, [], null, [], null, false, null));
        }

        public Task RecordPublishAsync(PublishMetricsRequest request, CancellationToken ct = default)
        {
            seen.Add(user.UserId);
            return Task.CompletedTask;
        }
    }
}
