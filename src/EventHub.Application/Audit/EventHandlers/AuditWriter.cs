using EventHub.Application.Common.Ports;
using EventHub.Contracts.Audit;
using EventHub.Domain.Audit;
using EventHub.Domain.Users;
using Mediator;

namespace EventHub.Application.Audit.EventHandlers;

/// <summary>
/// The single writer of Audit Entries (AD-14): one entry per <see cref="IAuditableEvent"/>, actor from
/// <see cref="ICurrentUser"/>, id from <see cref="IIdGenerator"/>, time from <see cref="IClock"/> and
/// <c>Visibility</c> resolved from the action's rule. The entry is added to the unit of work during
/// domain-event dispatch, so it is saved with the command, in the same transaction, or not at all.
/// </summary>
public sealed class AuditWriter(IAppDbContext db, ICurrentUser currentUser, IIdGenerator ids, IClock clock)
    : INotificationHandler<IAuditableEvent>
{
    public ValueTask Handle(IAuditableEvent notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var (visibility, organizationId) = Resolve(notification);
        if (visibility == AuditEntryVisibility.Tenant && organizationId is null)
        {
            // A Tenant row without an Organization would be visible to no one (AD-14).
            throw new InvalidOperationException($"Audit action {notification.Action} resolved to Tenant visibility without an Organization.");
        }

        db.Add(AuditEntry.Record(
            ids.NewId(),
            organizationId,
            ActorType(currentUser.Kind),
            currentUser.Kind == ActorKind.System ? null : currentUser.UserId,
            Truncate(currentUser.DisplayName, AuditEntry.ActorNameMaxLength),
            AuditActionCatalogue.WireValue(notification.Action),
            notification.EntityType,
            notification.EntityId,
            visibility,
            clock.UtcNow));

        return ValueTask.CompletedTask;
    }

    private (AuditEntryVisibility Visibility, Guid? OrganizationId) Resolve(IAuditableEvent notification) =>
        AuditActionCatalogue.VisibilityRule(notification.Action) switch
        {
            AuditVisibilityRule.Platform => (AuditEntryVisibility.Platform, notification.OrganizationId),
            AuditVisibilityRule.Tenant => (AuditEntryVisibility.Tenant, notification.OrganizationId),
            AuditVisibilityRule.ByTargetRole => notification.TargetRole switch
            {
                UserRole.OrgAdministrator or UserRole.SystemAdministrator => (AuditEntryVisibility.Platform, notification.OrganizationId),
                null => throw new InvalidOperationException(
                    $"Audit action {notification.Action} needs the target user's role."),
                _ => (AuditEntryVisibility.Tenant, notification.OrganizationId),
            },
            AuditVisibilityRule.ByActor => currentUser.Kind == ActorKind.SystemAdministrator
                ? (AuditEntryVisibility.Platform, null)
                : (AuditEntryVisibility.Tenant, currentUser.OrganizationId ?? notification.OrganizationId),
            var rule => throw new InvalidOperationException($"Unknown audit visibility rule {rule}."),
        };

    private static string Truncate(string? value, int maxLength) =>
        value is null ? string.Empty : value.Length <= maxLength ? value : value[..maxLength];

    private static AuditActorType ActorType(ActorKind kind) => kind switch
    {
        ActorKind.SystemAdministrator => AuditActorType.SystemAdministrator,
        ActorKind.OrgAdministrator or ActorKind.EventManager => AuditActorType.User,
        ActorKind.System => AuditActorType.System,
        _ => throw new InvalidOperationException("An audited action needs an authenticated or System actor."),
    };
}
