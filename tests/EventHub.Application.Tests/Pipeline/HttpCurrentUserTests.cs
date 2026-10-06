using System.Security.Claims;
using EventHub.Api.Hosting;
using EventHub.Application.Common.Ports;
using Microsoft.AspNetCore.Http;

namespace EventHub.Application.Tests.Pipeline;

/// <summary>AD-26: the HTTP caller comes from session claims only; anything unexpected is anonymous.</summary>
public sealed class HttpCurrentUserTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Read_WhenSystemAdministratorClaims_IsThatActorWithoutOrganization()
    {
        var user = For(Principal(("sub", UserId.ToString()), ("role", "SystemAdministrator"), ("name", "Sara Khan")));

        Assert.Equal(ActorKind.SystemAdministrator, user.Kind);
        Assert.Equal(UserId, user.UserId);
        Assert.Equal("Sara Khan", user.DisplayName);
        Assert.Null(user.OrganizationId);
    }

    [Theory]
    [InlineData("Superuser")]
    [InlineData("systemadministrator")]
    [InlineData("")]
    public void Read_WhenRoleClaimIsUnknownOrTampered_IsAnonymous(string role)
    {
        var user = For(Principal(("sub", UserId.ToString()), ("role", role)));

        Assert.Equal(ActorKind.Anonymous, user.Kind);
        Assert.Null(user.UserId);
    }

    [Fact]
    public void Read_WhenNotAuthenticatedOrSubjectMissing_IsAnonymous()
    {
        Assert.Equal(ActorKind.Anonymous, For(new ClaimsPrincipal(new ClaimsIdentity())).Kind);
        Assert.Equal(ActorKind.Anonymous, For(Principal(("role", "SystemAdministrator"))).Kind);
        Assert.Equal(ActorKind.Anonymous, new HttpCurrentUser(new HttpContextAccessor()).Kind);
    }

    private static HttpCurrentUser For(ClaimsPrincipal principal) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), AuthSetup.Scheme, "name", "role"));
}
