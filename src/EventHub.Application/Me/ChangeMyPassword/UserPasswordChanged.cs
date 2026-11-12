using EventHub.Application.Audit;
using EventHub.Contracts.Audit;

namespace EventHub.Application.Me.ChangeMyPassword;

/// <summary>
/// <c>user.passwordChanged</c> (AD-14, FR32); the actor is the signed-in caller, and the <c>ByActor</c> rule makes a
/// System Administrator's row Platform-visible with no Organization.
/// </summary>
public sealed record UserPasswordChanged(Guid UserId) : IAuditableEvent
{
    public AuditAction Action => AuditAction.UserPasswordChanged;

    public string EntityType => "User";

    public Guid EntityId => UserId;

    /// <summary>The <c>ByActor</c> rule takes the actor's Organization (null for a System Administrator).</summary>
    public Guid? OrganizationId => null;
}
