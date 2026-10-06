using EventHub.Application.Audit;
using EventHub.Contracts.Audit;

namespace EventHub.Application.Auth.Logout;

/// <summary><c>user.signedOut</c> (AD-14, SM-7); the actor is the signed-in caller.</summary>
public sealed record UserSignedOut(Guid UserId) : IAuditableEvent
{
    public AuditAction Action => AuditAction.UserSignedOut;

    public string EntityType => "User";

    public Guid EntityId => UserId;

    /// <summary>The <c>ByActor</c> rule takes the actor's Organization (null for a System Administrator).</summary>
    public Guid? OrganizationId => null;
}
