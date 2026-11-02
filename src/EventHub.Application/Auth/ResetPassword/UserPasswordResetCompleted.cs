using EventHub.Application.Audit;
using EventHub.Application.Common.Ports;
using EventHub.Contracts.Audit;

namespace EventHub.Application.Auth.ResetPassword;

/// <summary>
/// <c>user.passwordResetCompleted</c> (AD-14, FR32). The caller is anonymous, so the user who reset is the subject
/// actor: a System Administrator's row lands Platform-visible with a null Organization (as <c>user.signedIn</c>).
/// </summary>
public sealed record UserPasswordResetCompleted(SessionAccount Account) : IAuditableEvent
{
    public AuditAction Action => AuditAction.UserPasswordResetCompleted;

    public string EntityType => "User";

    public Guid EntityId => Account.UserId;

    public Guid? OrganizationId => Account.OrganizationId;

    public AuditSubjectActor SubjectActor =>
        new(Account.UserId, Account.ActorKind, Account.OrganizationId, Account.DisplayName);
}
