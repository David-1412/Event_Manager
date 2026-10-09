using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Application.Ingestion;
using SportMeet.Infrastructure.Identity;
using SportMeet.Infrastructure.Persistence;
using SportMeet.Infrastructure.Seeding;

namespace SportMeet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured. Start the database with `docker compose up -d postgres`.");

        services.AddDbContext<AppDbContext>(options =>
            options
                // The retry strategy suits the read/write queries the app issues.
                // Migrations are applied outside it (see StartupTasks), because it
                // cannot be combined with their transaction.
                .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
                // Title -> title, EventParticipant -> event_participants, so the
                // generated DDL matches the schema in IMPLEMENTATION_PLAN.md §2 and
                // camelCase JSON needs no per-property attributes.
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IEventRepository, EventRepository>();

        // Administrator user management. Paired with the service registered in
        // AddApplication; the two FKs on user_role_audit are RESTRICT, so this
        // repository is the only writer the audit table has.
        services.AddScoped<SportMeet.Application.Admin.IUserAdminRepository, UserAdminRepository>();

        // Maps mailbox addresses (the polled mailbox's owner) to users rows,
        // creating the row on first sight so a polled invitation always has an
        // owner to be private to. See IMailboxOwnerResolver.
        services.AddScoped<SportMeet.Application.Common.IMailboxOwnerResolver,
            SportMeet.Infrastructure.Identity.MailboxOwnerResolver>();

        // --- Email ingestion -------------------------------------------------
        services.Configure<SportMeet.Application.Ingestion.IngestionOptions>(
            config.GetSection(SportMeet.Application.Ingestion.IngestionOptions.SectionName));

        services.AddScoped<SportMeet.Application.Ingestion.IEventDraftRepository, EventDraftRepository>();

        // The extractor is chosen by whether an API key exists, which is the only way this
        // can be decided without a second config flag to drift out of sync with the first:
        // "no key" and "use the LLM" are the same setting read twice otherwise. Both are
        // registered so the concrete type resolves either way, and the heuristic is the
        // LLM's constructor dependency rather than a runtime lookup — a missing key or a
        // dead endpoint degrades to it inside the extractor, so no caller has to know the
        // fallback exists.
        //
        // Decision happens at resolve time via the factory, not at AddInfrastructure time:
        // user-secrets and environment variables are already in `config` when this runs, so
        // an early read would be correct, but a factory keeps the choice honest if the
        // provider is ever rebuilt (tests, hosted reuse) after configuration changes.
        services.AddSingleton<Ingestion.HeuristicEventExtractor>();
        services.AddSingleton<Ingestion.LlmEventExtractor>();
        services.AddSingleton<Ingestion.ClaudeEventExtractor>();
        services.AddSingleton<SportMeet.Application.Ingestion.IEventExtractor>(sp =>
        {
            var heuristic = sp.GetRequiredService<Ingestion.HeuristicEventExtractor>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DependencyInjection));

            // Provider priority: OpenAI, then Claude, then the heuristic. Both LLM providers
            // keep the heuristic as their internal fallback, so a dead endpoint still
            // degrades; this factory only decides which provider is primary. The check is
            // `IsConfigured` (key + model + well-formed base URL), not key presence alone,
            // so a half-filled section cannot select a provider that would fail every call.
            var openAi = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            if (openAi.IsConfigured)
            {
                logger.LogInformation(
                    "Extraction: using {Model} (prompt {Prompt}); heuristic remains the fallback",
                    openAi.Model, openAi.PromptVersion);
                return sp.GetRequiredService<Ingestion.LlmEventExtractor>();
            }

            var claude = sp.GetRequiredService<IOptions<ClaudeOptions>>().Value;
            if (claude.IsConfigured)
            {
                logger.LogInformation(
                    "Extraction: OpenAI not configured, using {Model} (prompt {Prompt}); "
                    + "heuristic remains the fallback",
                    claude.Model, claude.PromptVersion);
                return sp.GetRequiredService<Ingestion.ClaudeEventExtractor>();
            }

            // Neither key present: the heuristic answers alone. Logged at Information so a
            // deployed container that expected an LLM is not silently heuristic-only.
            logger.LogInformation(
                "Extraction: no LLM key configured (OpenAI or Claude); using {Model}",
                heuristic.Model);
            return heuristic;
        });

        // Bound here for the same reason as IngestionOptions: IConfiguration belongs to
        // this layer. ApiKey must stay empty in appsettings.json — user-secrets in
        // Development, environment or Key Vault in deployment.
        services.Configure<OpenAiOptions>(config.GetSection(OpenAiOptions.SectionName));
        services.Configure<ClaudeOptions>(config.GetSection(ClaudeOptions.SectionName));

        // Named client so the extractor gets its own timeout and handler pool: a shared
        // default client would inherit whatever timeout the next feature configures, and a
        // stalled LLM call would then hold a poll cycle open indefinitely. Twelve seconds
        // above the configured call timeout, so the per-call option stays the setting that
        // matters and the transport only catches a request that never returns at all.
        var callTimeout = TimeSpan.FromSeconds(Math.Max(
            config.GetValue($"{OpenAiOptions.SectionName}:TimeoutSeconds", 30), 5));
        services.AddHttpClient("openai", client =>
        {
            client.Timeout = callTimeout + TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        // Same shape as the OpenAI client; a separate named client so the two providers keep
        // independent handler pools and timeouts. Anthropic needs no default auth header -
        // the extractor sets x-api-key and anthropic-version per request.
        var claudeTimeout = TimeSpan.FromSeconds(Math.Max(
            config.GetValue($"{ClaudeOptions.SectionName}:TimeoutSeconds", 30), 5));
        services.AddHttpClient("claude", client =>
        {
            client.Timeout = claudeTimeout + TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        // The demo identity is configuration, never a constant: unset Demo:AsUserId
        // and the API has no identity at all.
        services.Configure<DemoUserOptions>(config.GetSection(DemoUserOptions.SectionName));
        services.Configure<SportMeet.Application.Ingestion.FirebaseOptions>(
            config.GetSection(SportMeet.Application.Ingestion.FirebaseOptions.SectionName));
        services.AddHttpContextAccessor();
        services.AddScoped<IUserStore, Identity.EfUserStore>();
        // Replaces ConfigurationDemoUser as the acting identity. It reads the verified
        // bearer token when one is present (real, per-user identity) and falls back to
        // the configured demo id only when there is no token and a demo id is set, so
        // anonymous dev/tests keep working and production (demo unset) never resolves
        // anyone it cannot verify.
        services.AddScoped<ICurrentUser, Identity.TokenCurrentUser>();

        // Firebase ID-token verification, installed only when a project is configured —
        // the same opt-in shape as the extractor and the poller. When present, the
        // browser's raw Firebase ID token is the API's bearer credential; the raw
        // validation lives in the Api layer's UseAuthentication pipeline and in
        // AddFirebaseJwtBearer below (the handler type is an AspNetCore package, so the
        // registration is called from Program rather than buried in the data layer).
        services.AddSingleton<Identity.FirebasePublicKeyProvider>();
        services.AddMemoryCache();
        services.AddHttpClient("firebase", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        services.AddScoped<DemoSeeder>();

        // --- Paste-to-event import ---------------------------------------
        services.Configure<SportMeet.Application.Imports.ImportOptions>(
            config.GetSection(SportMeet.Application.Imports.ImportOptions.SectionName));
        services.Configure<Imports.GoogleGeocodingOptions>(
            config.GetSection(Imports.GoogleGeocodingOptions.SectionName));
        // RemoveAllLoggers: the geocoding request URL carries the API key, and the default
        // HttpClient logging writes every URL at Information.
        services.AddHttpClient(Imports.GoogleGeocoder.ClientName, client => client.Timeout = TimeSpan.FromSeconds(4))
            .RemoveAllLoggers();
        services.AddScoped<SportMeet.Application.Imports.IGeocoder, Imports.GoogleGeocoder>();
        services.AddScoped<SportMeet.Application.Imports.IImportRepository, Persistence.EfImportRepository>();

        // --- Graph mail transport -----------------------------------------
        // Bound here rather than in Application for the same reason as IngestionOptions:
        // IConfiguration belongs to this layer.
        services.Configure<SportMeet.Application.Ingestion.GraphOptions>(
            config.GetSection(SportMeet.Application.Ingestion.GraphOptions.SectionName));

        // Singleton: the Graph client owns an HttpClient and a token cache, and building
        // one per poll would re-authenticate every cycle. Safe to share because it holds
        // nothing per-message and is only ever called from the single polling thread.
        services.AddSingleton<SportMeet.Application.Ingestion.IEmailReader, Ingestion.GraphEmailReader>();

        // Scoped with the DbContext and repositories it consumes, and resolved through a
        // scope by the hosted service — never injected into the singleton worker directly,
        // which would either captive the context for the app's lifetime or trip the
        // scoped-captured-by-singleton validation the Web SDK enables in Development.
        // EmailProcessor itself is registered by AddApplication: it is an Application type whose
        // dependencies (IEmailReader, IEmailIngestionService) are both abstractions declared
        // there, so the application layer composes it without help from this one.
        return services;
    }

    /// <summary>
    /// Registers the polling loop, and only when the feature is switched on.
    ///
    /// Separate from <see cref="AddInfrastructure"/> because <c>AddHostedService</c> is
    /// the host's concern rather than the data layer's — and because the point is that a
    /// container with <c>Ingestion:Enabled=false</c> never creates the worker at all,
    /// rather than running one that wakes up and does nothing. The check reads
    /// configuration rather than <c>IOptions</c> because this runs during composition,
    /// before any provider has been built.
    /// </summary>
    public static IServiceCollection AddEmailIngestionWorker(
        this IServiceCollection services, IConfiguration config)
    {
        if (!config.GetValue(
                $"{SportMeet.Application.Ingestion.IngestionOptions.SectionName}:Enabled", false))
            return services;

        services.AddHostedService<Ingestion.EmailPollingHostedService>();
        return services;
    }

    /// <summary>
    /// Installs Firebase ID-token bearer authentication, but only when a project is
    /// configured. Lives in Infrastructure (which references the JwtBearer package) and
    /// is called explicitly from Program so the Api does not need the handler type in
    /// scope.
    ///
    /// Unconfigured, this is a no-op and the API stays exactly as anonymous as it was,
    /// with the configured demo identity answering for everyone — which is the
    /// Milestone-1 behaviour and what the integration tests assume.
    /// </summary>
    public static IServiceCollection AddFirebaseJwtBearer(
        this IServiceCollection services, IConfiguration config)

    {
        var firebase = config.GetSection(SportMeet.Application.Ingestion.FirebaseOptions.SectionName)
            .Get<SportMeet.Application.Ingestion.FirebaseOptions>()
            ?? new SportMeet.Application.Ingestion.FirebaseOptions();
        if (!firebase.IsConfigured) return services;

        services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, options =>

            {
                options.RequireHttpsMetadata = false; // Firebase issues over HTTPS; metadata endpoint is HTTPS. Trust the configured scheme in dev.
                // Signature key resolution is done in TokenValidated via the provider,
                // so the handler's built-in key retrieval is disabled and issuer/audience
                // are checked against the configured project.
                options.MapInboundClaims = true;
                options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = firebase.ExpectedIssuer(firebase.ProjectId),
                    ValidateAudience = true,
                    ValidAudience = firebase.ProjectId,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
                options.EventsType = typeof(Identity.FirebaseJwtBearerEvents);
            });

        services.AddOptions<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
            .Configure<Identity.FirebasePublicKeyProvider>((options, keys) =>
            {
                options.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) =>
                {
                    var key = keys.ResolveAsync(kid).GetAwaiter().GetResult();
                    return key is null ? [] : [key];
                };
            });

        services.AddScoped<Identity.FirebaseJwtBearerEvents>();
        services.AddAuthorization();
        return services;
    }
}

