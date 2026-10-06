using System.Security.Claims;
using EventHub.Application.Common.Ports;

namespace EventHub.Api.Hosting;

/// <summary>
/// The HTTP caller (AD-26), read lazily from the claims of <c>HttpContext.User</c> that cookie authentication set:
/// <c>sub</c>, <c>role</c>, <c>name</c> and, for Organization users (Epic 2), <c>org_id</c>. Claims only: it never
/// calls <c>AuthenticateAsync</c> and never touches Data Protection. That matters because the chain
/// <c>AppDbContext → ScopeAccessor → CurrentUserTenantContext → ICurrentUser</c> also runs while the key ring
/// (stored in <c>AppDbContext</c>) is loaded to unprotect the very cookie being authenticated; at that point the
/// user is still anonymous and this class just says so. No valid session → anonymous.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public ActorKind Kind => Read().Kind;

    public Guid? UserId => Read().UserId;

    public string DisplayName => Read().Name;

    public Guid? OrganizationId => Read().OrganizationId;

    private (ActorKind Kind, Guid? UserId, string Name, Guid? OrganizationId) Read()
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity is not { IsAuthenticated: true }
            || !Guid.TryParse(principal.FindFirstValue(SessionClaims.Subject), out var userId)
            || userId == Guid.Empty)
        {
            return Anonymous;
        }

        var kind = principal.FindFirstValue(SessionClaims.Role) switch
        {
            SessionClaims.Roles.SystemAdministrator => ActorKind.SystemAdministrator,
            SessionClaims.Roles.OrgAdministrator => ActorKind.OrgAdministrator,
            SessionClaims.Roles.EventManager => ActorKind.EventManager,
            _ => ActorKind.Anonymous,
        };
        if (kind == ActorKind.Anonymous)
        {
            return Anonymous;
        }

        Guid? organizationId = Guid.TryParse(principal.FindFirstValue(SessionClaims.OrganizationId), out var org) ? org : null;
        return (kind, userId, principal.FindFirstValue(SessionClaims.Name) ?? string.Empty, organizationId);
    }

    private static readonly (ActorKind, Guid?, string, Guid?) Anonymous = (ActorKind.Anonymous, null, string.Empty, null);
}
