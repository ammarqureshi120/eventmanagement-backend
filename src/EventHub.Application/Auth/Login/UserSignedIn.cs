using EventHub.Application.Audit;
using EventHub.Application.Common.Ports;
using EventHub.Contracts.Audit;

namespace EventHub.Application.Auth.Login;

/// <summary>
/// <c>user.signedIn</c> (AD-14, SM-7). The caller is still anonymous, so the signed-in user is the subject actor:
/// a System Administrator's row lands Platform-visible with a null Organization.
/// </summary>
public sealed record UserSignedIn(SessionAccount Account) : IAuditableEvent
{
    public AuditAction Action => AuditAction.UserSignedIn;

    public string EntityType => "User";

    public Guid EntityId => Account.UserId;

    public Guid? OrganizationId => Account.OrganizationId;

    public AuditSubjectActor SubjectActor =>
        new(Account.UserId, Account.ActorKind, Account.OrganizationId, Account.DisplayName);
}
