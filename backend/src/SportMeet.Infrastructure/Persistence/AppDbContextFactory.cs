using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SportMeet.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef` build the model without booting the API. Reads the same
/// environment variable the app falls back to, so the designer and the running
/// app can never disagree about which database they are pointed at.
///
/// The connection string is irrelevant to scaffolding (no live connection is
/// needed to write a migration) but it must parse, hence a complete default.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public const string ConnectionStringEnvVar = "SPORTMEET_CONNECTIONSTRING";

    /// <summary>The local compose database. Exporting the env var overrides it for
    /// a designer pointed somewhere else; nothing here reads appsettings, because
    /// a design-time file lookup from the tools' working directory is exactly the
    /// kind of thing that silently points migrations at the wrong server.</summary>
    public const string LocalConnectionString =
        "Host=localhost;Port=5432;Database=sportmeet;Username=sportmeet;Password=sportmeet";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvVar) ?? LocalConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
