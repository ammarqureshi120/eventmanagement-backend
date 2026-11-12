using EventHub.Application.Audit;
using EventHub.Application.Common.Events;
using EventHub.Application.Common.Ports;
using EventHub.Contracts.Audit;
using EventHub.Domain.Users;
using Mediator;

namespace EventHub.Application.Me.UpdateMyProfile;

/// <summary>
/// <c>user.edited</c> (AD-14, FR32): follows the target's role (<see cref="AuditVisibilityRule.ByTargetRole"/>; a
/// System Administrator target is Platform-visible with no Organization); the actor is the caller (me).
/// </summary>
public sealed record UserProfileEditedAudit(Guid UserId, UserRole Role, Guid? UserOrganizationId) : IAuditableEvent
{
    public AuditAction Action => AuditAction.UserEdited;

    public string EntityType => "User";

    public Guid EntityId => UserId;

    public Guid? OrganizationId => UserOrganizationId;

    public UserRole? TargetRole => Role;
}

/// <summary>Maps the domain event <see cref="UserProfileEdited"/> to its auditable event (AD-5, AD-14).</summary>
public sealed class UserProfileEditedAuditMapper(IApplicationEvents events) : INotificationHandler<DomainEventNotification>
{
    public ValueTask Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.Event is UserProfileEdited edited)
        {
            events.Raise(new UserProfileEditedAudit(edited.UserId, edited.Role, edited.OrganizationId));
        }

        return ValueTask.CompletedTask;
    }
}
