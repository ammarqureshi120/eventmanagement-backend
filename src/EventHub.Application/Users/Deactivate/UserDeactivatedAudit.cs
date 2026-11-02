using EventHub.Application.Audit;
using EventHub.Application.Common.Events;
using EventHub.Application.Common.Ports;
using EventHub.Contracts.Audit;
using EventHub.Domain.Users;
using Mediator;

namespace EventHub.Application.Users.Deactivate;

/// <summary>
/// <c>user.deactivated</c> (AD-14, SM-7): follows the target's role (<see cref="AuditVisibilityRule.ByTargetRole"/>);
/// the actor is the caller (the System actor in Story 1.5).
/// </summary>
public sealed record UserDeactivatedAudit(Guid UserId, UserRole Role, Guid? UserOrganizationId) : IAuditableEvent
{
    public AuditAction Action => AuditAction.UserDeactivated;

    public string EntityType => "User";

    public Guid EntityId => UserId;

    public Guid? OrganizationId => UserOrganizationId;

    public UserRole? TargetRole => Role;
}

/// <summary>Maps the domain event <see cref="UserDeactivated"/> to its auditable event (AD-5, AD-14).</summary>
public sealed class UserDeactivatedAuditMapper(IApplicationEvents events) : INotificationHandler<DomainEventNotification>
{
    public ValueTask Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.Event is UserDeactivated deactivated)
        {
            events.Raise(new UserDeactivatedAudit(deactivated.UserId, deactivated.Role, deactivated.OrganizationId));
        }

        return ValueTask.CompletedTask;
    }
}
