using System.Text.Json.Serialization;

namespace EventHub.Contracts.Audit;

/// <summary>
/// The FR32 audit catalogue owned by Audit (AD-14). Wire value <c>&lt;entity&gt;.&lt;pastTense&gt;</c>; each member
/// declares its visibility rule. Wire-only until Story 5.5 (audit history), so renames stay cheap until then.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AuditAction>))]
public enum AuditAction
{
    // Organization actions: Platform.
    [JsonStringEnumMemberName("organization.created")]
    [AuditVisibility(AuditVisibilityRule.Platform)]
    OrganizationCreated,

    [JsonStringEnumMemberName("organization.updated")]
    [AuditVisibility(AuditVisibilityRule.Platform)]
    OrganizationUpdated,

    [JsonStringEnumMemberName("organization.activated")]
    [AuditVisibility(AuditVisibilityRule.Platform)]
    OrganizationActivated,

    [JsonStringEnumMemberName("organization.deactivated")]
    [AuditVisibility(AuditVisibilityRule.Platform)]
    OrganizationDeactivated,

    // User lifecycle: follows the target's role.
    [JsonStringEnumMemberName("user.invited")]
    [AuditVisibility(AuditVisibilityRule.ByTargetRole)]
    UserInvited,

    [JsonStringEnumMemberName("user.edited")]
    [AuditVisibility(AuditVisibilityRule.ByTargetRole)]
    UserEdited,

    [JsonStringEnumMemberName("user.roleChanged")]
    [AuditVisibility(AuditVisibilityRule.ByTargetRole)]
    UserRoleChanged,

    [JsonStringEnumMemberName("user.deactivated")]
    [AuditVisibility(AuditVisibilityRule.ByTargetRole)]
    UserDeactivated,

    [JsonStringEnumMemberName("user.reactivated")]
    [AuditVisibility(AuditVisibilityRule.ByTargetRole)]
    UserReactivated,

    [JsonStringEnumMemberName("invite.resent")]
    [AuditVisibility(AuditVisibilityRule.ByTargetRole)]
    InviteResent,

    [JsonStringEnumMemberName("invite.accepted")]
    [AuditVisibility(AuditVisibilityRule.ByTargetRole)]
    InviteAccepted,

    // Own-account actions: follow the actor.
    [JsonStringEnumMemberName("user.passwordResetCompleted")]
    [AuditVisibility(AuditVisibilityRule.ByActor)]
    UserPasswordResetCompleted,

    [JsonStringEnumMemberName("user.passwordChanged")]
    [AuditVisibility(AuditVisibilityRule.ByActor)]
    UserPasswordChanged,

    [JsonStringEnumMemberName("user.signedIn")]
    [AuditVisibility(AuditVisibilityRule.ByActor)]
    UserSignedIn,

    [JsonStringEnumMemberName("user.signedOut")]
    [AuditVisibility(AuditVisibilityRule.ByActor)]
    UserSignedOut,

    // Venues: Tenant.
    [JsonStringEnumMemberName("venue.created")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    VenueCreated,

    [JsonStringEnumMemberName("venue.updated")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    VenueUpdated,

    [JsonStringEnumMemberName("venue.activated")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    VenueActivated,

    [JsonStringEnumMemberName("venue.deactivated")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    VenueDeactivated,

    // Events: Tenant.
    [JsonStringEnumMemberName("event.created")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    EventCreated,

    [JsonStringEnumMemberName("event.updated")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    EventUpdated,

    [JsonStringEnumMemberName("event.managerChanged")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    EventManagerChanged,

    [JsonStringEnumMemberName("event.published")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    EventPublished,

    [JsonStringEnumMemberName("event.registrationOpened")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    EventRegistrationOpened,

    [JsonStringEnumMemberName("event.registrationClosed")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    EventRegistrationClosed,

    [JsonStringEnumMemberName("event.registrationReopened")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    EventRegistrationReopened,

    [JsonStringEnumMemberName("event.completed")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    EventCompleted,

    [JsonStringEnumMemberName("event.cancelled")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    EventCancelled,

    [JsonStringEnumMemberName("event.capacityOverrideChanged")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    EventCapacityOverrideChanged,

    // Ticket types: Tenant.
    [JsonStringEnumMemberName("ticketType.created")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    TicketTypeCreated,

    [JsonStringEnumMemberName("ticketType.updated")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    TicketTypeUpdated,

    [JsonStringEnumMemberName("ticketType.activated")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    TicketTypeActivated,

    [JsonStringEnumMemberName("ticketType.deactivated")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    TicketTypeDeactivated,

    // Registrations and exports: Tenant.
    [JsonStringEnumMemberName("registration.created")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    RegistrationCreated,

    [JsonStringEnumMemberName("registration.edited")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    RegistrationEdited,

    [JsonStringEnumMemberName("registration.cancelled")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    RegistrationCancelled,

    [JsonStringEnumMemberName("attendees.exported")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    AttendeesExported,

    [JsonStringEnumMemberName("reports.exported")]
    [AuditVisibility(AuditVisibilityRule.Tenant)]
    ReportsExported,
}
