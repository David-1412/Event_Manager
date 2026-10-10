using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SportMeet.Api.Common;
using SportMeet.Application.Common;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// The HTTP-layer gate on the admin user endpoints and the review queue: which
/// roles get through for each permission, and that refusals are 401 (no identity)
/// or 404 (signed in, not allowed) — never a 403 that confirms the surface exists.
/// </summary>
public class RequirePermissionAttributeTests
{
    private sealed class FakeUser(Guid? userId, UserRole role) : ICurrentUser
    {
        public Guid? UserId => userId;
        public bool IsDemo => false;
        public UserRole Role => role;
    }

    private static int? Run(Permission permission, Guid? userId, UserRole role)
    {
        var services = new ServiceCollection()
            .AddSingleton<ICurrentUser>(new FakeUser(userId, role))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());

        new RequirePermissionAttribute(permission).OnAuthorization(context);

        return (context.Result as ObjectResult)?.StatusCode;
    }

    [Theory]
    [InlineData(Permission.ManageUsers, UserRole.Member, 404)]
    [InlineData(Permission.ManageUsers, UserRole.Creator, 404)]
    [InlineData(Permission.ManageUsers, UserRole.Admin, null)]
    [InlineData(Permission.ReviewEvents, UserRole.Member, 404)]
    [InlineData(Permission.ReviewEvents, UserRole.Creator, 404)]
    [InlineData(Permission.ReviewEvents, UserRole.Admin, null)]
    public void Signed_in_caller_passes_only_with_the_permission(Permission permission, UserRole role, int? expected)
        => Assert.Equal(expected, Run(permission, Guid.NewGuid(), role));

    [Fact]
    public void Anonymous_caller_gets_401()
        => Assert.Equal(401, Run(Permission.ManageUsers, null, UserRole.Admin));
}
