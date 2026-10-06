using EventHub.Application.Common.Ports;
using EventHub.Contracts.Audit;
using EventHub.Domain.Users;
using Mediator;

namespace EventHub.Application.Audit;

/// <summary>
/// An event that maps to an FR32 action (AD-14). Features raise it (through <c>IApplicationEvents</c> or,
/// from Story 2.1, mapped from a domain event); only the <c>AuditWriter</c> turns it into an Audit Entry,
/// exactly one per event, in the command transaction.
/// </summary>
public interface IAuditableEvent : INotification
{
    AuditAction Action { get; }

    /// <summary>Entity type name, for example <c>Organization</c>, <c>Event</c>, <c>Report</c>.</summary>
    string EntityType { get; }

    Guid EntityId { get; }

    /// <summary>The affected entity's Organization; null only when none exists.</summary>
    Guid? OrganizationId { get; }

    /// <summary>Required for user-lifecycle actions (<see cref="AuditVisibilityRule.ByTargetRole"/>).</summary>
    UserRole? TargetRole => null;

    /// <summary>
    /// The actor of an action taken while the caller is still anonymous (sign-in; later reset completed and invite
    /// accepted). When set it wins over <c>ICurrentUser</c> for the actor and for the
    /// <see cref="AuditVisibilityRule.ByActor"/> rule (AD-14).
    /// </summary>
    AuditSubjectActor? SubjectActor => null;
}

/// <summary>Who performed an audited action when it is not the current caller (AD-14).</summary>
public sealed record AuditSubjectActor(Guid Id, ActorKind Kind, Guid? OrganizationId, string Name);
