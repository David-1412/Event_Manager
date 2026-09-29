using Microsoft.EntityFrameworkCore;
using SportMeet.Infrastructure;

namespace SportMeet.Api;

/// <summary>
/// Applies migrations and seeds demo data before the first request, gated on
/// configuration.
///
/// Auto-migrating at startup is a development convenience, not a deployment
/// strategy: production applies the idempotent SQL script (IMPLEMENTATION_PLAN.md
/// §8), because several replicas each calling Migrate() is a race on the same
/// schema. Both flags therefore default to false and are enabled only in
/// appsettings.Development.json, so an environment that forgets to set them fails
/// loudly at the health check instead of silently altering a shared database.
///
/// A failure here is fatal on purpose. Starting an API that then answers every
/// request with a 500 is harder to diagnose from the outside than refusing to
/// start with the migration error in the log.
/// </summary>
public static class StartupTasks
{
    public static async Task RunAsync(WebApplication app)
    {
        var autoMigrate = app.Configuration.GetValue<bool>("Database:AutoMigrate");
        var seedDemoData = app.Configuration.GetValue<bool>("Database:SeedDemoData");

        if (!autoMigrate && !seedDemoData)
        {
            return;
        }

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

        if (autoMigrate)
        {
            logger.LogInformation("Database:AutoMigrate is on; applying pending migrations.");

            // EnableRetryOnFailure wraps the first migration statement in an
            // execution strategy that cannot be combined with a user transaction,
            // so migration runs must opt out or startup dies on an unrelated error.
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SportMeet.Infrastructure.Persistence.AppDbContext>();
            await db.Database.MigrateAsync();

            if (seedDemoData)
            {
                await scope.ServiceProvider
                    .GetRequiredService<SportMeet.Infrastructure.Seeding.DemoSeeder>()
                    .SeedAsync();
            }
        }
        else if (seedDemoData)
        {
            // Seeding without migrating would fail on a missing schema, so it is
            // reported rather than attempted.
            logger.LogWarning("Database:SeedDemoData is set but Database:AutoMigrate is off; skipping seed.");
        }

        // Read from the options rather than ICurrentUser: that service is scoped
        // (one per request, tied to a token later), and resolving it from the root
        // provider throws under scope validation. The flag is pure configuration,
        // so the singleton options say exactly the same thing.
        var demoOptions = app.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<SportMeet.Infrastructure.Identity.DemoUserOptions>>()
            .Value;
        if (demoOptions.AsUserId is not null)
        {
            logger.LogWarning(
                "DEMO MODE: Demo:AsUserId is {UserId}, so isHost/isJoined are computed for that identity " +
                "and POST /api/events is reachable without authentication. " +
                "Remove Demo:AsUserId before this configuration reaches any real deployment.",
                demoOptions.AsUserId);
        }
    }
}
