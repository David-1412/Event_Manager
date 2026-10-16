using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SportMeet.Infrastructure;
using SportMeet.Infrastructure.Identity;

namespace SportMeet.Import.Tests;

/// <summary>
/// Production turns the demo identity off with an empty <c>Demo:AsUserId</c>
/// (docker-compose.prod.yml sets <c>Demo__AsUserId: ""</c>). An empty value has to
/// mean "no demo user", not "keep the code default": the default is the seeded
/// participant, so falling back to it would let every anonymous request on the
/// public site act as that user.
/// </summary>
public class DemoUserOptionsBindingTests
{
    private static DemoUserOptions Bind(Dictionary<string, string?> values)
    {
        // AddInfrastructure insists on a connection string; nothing here connects.
        values["ConnectionStrings:Default"] = "Host=unused;Database=unused";
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(config);
        return services.BuildServiceProvider().GetRequiredService<IOptions<DemoUserOptions>>().Value;
    }

    [Fact]
    public void Empty_value_disables_the_demo_identity()
    {
        var options = Bind(new() { ["Demo:AsUserId"] = "" });
        Assert.Null(options.AsUserId);
    }

    [Fact]
    public void Explicit_id_is_used()
    {
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var options = Bind(new() { ["Demo:AsUserId"] = id.ToString() });
        Assert.Equal(id, options.AsUserId);
    }
}
