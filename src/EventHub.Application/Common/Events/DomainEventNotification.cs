using EventHub.Domain.Common;
using Mediator;

namespace EventHub.Application.Common.Events;

/// <summary>
/// Carries a BCL-only domain event through Mediator notifications (AD-5). Handlers subscribe with
/// <c>INotificationHandler&lt;DomainEventNotification&gt;</c> and switch on <see cref="Event"/>. The first
/// audited aggregate event (Story 2.1) adds the domain-to-audit mapping.
/// </summary>
public sealed record DomainEventNotification(IDomainEvent Event) : INotification;
