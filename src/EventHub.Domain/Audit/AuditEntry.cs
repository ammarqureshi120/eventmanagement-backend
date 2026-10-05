namespace EventHub.Domain.Audit;

/// <summary>Who performed an audited action (AD-14). Stored as varchar.</summary>
public enum AuditActorType
{
    User,
    SystemAdministrator,
    System,
}

/// <summary>Stored on <c>AuditEntries.Visibility</c>; RLS lets the Platform scope read only <see cref="Platform"/> rows (AD-14, FR33).</summary>
public enum AuditEntryVisibility
{
    Platform,
    Tenant,
}

/// <summary>
/// Append-only audit record (AD-14). Written only by the Application <c>AuditWriter</c>; there is no
/// update or delete path. <see cref="Action"/> holds the wire value of the Contracts <c>AuditAction</c>
/// (for example <c>user.signedIn</c>) because the Domain references no other project.
/// </summary>
public sealed class AuditEntry
{
    public const int ActorNameMaxLength = 201;
    public const int ActionMaxLength = 64;
    public const int EntityTypeMaxLength = 64;

    private AuditEntry()
    {
        // EF Core materialization.
    }

    public Guid Id { get; private set; }

    /// <summary>Null for System Administrator sign-in/out and when no Organization exists.</summary>
    public Guid? OrganizationId { get; private set; }

    public AuditActorType ActorType { get; private set; }

    public Guid? ActorId { get; private set; }

    public string ActorName { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public Guid EntityId { get; private set; }

    public AuditEntryVisibility Visibility { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public static AuditEntry Record(
        Guid id,
        Guid? organizationId,
        AuditActorType actorType,
        Guid? actorId,
        string actorName,
        string action,
        string entityType,
        Guid entityId,
        AuditEntryVisibility visibility,
        DateTime occurredAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An audit entry id is required.", nameof(id));
        }

        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("OccurredAtUtc must be UTC.", nameof(occurredAtUtc));
        }

        return new AuditEntry
        {
            Id = id,
            OrganizationId = organizationId,
            ActorType = actorType,
            ActorId = actorId,
            ActorName = Bounded(actorName, ActorNameMaxLength, nameof(actorName), allowEmpty: true),
            Action = Bounded(action, ActionMaxLength, nameof(action)),
            EntityType = Bounded(entityType, EntityTypeMaxLength, nameof(entityType)),
            EntityId = entityId,
            Visibility = visibility,
            OccurredAtUtc = occurredAtUtc,
        };
    }

    private static string Bounded(string value, int maxLength, string parameterName, bool allowEmpty = false)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if ((!allowEmpty && trimmed.Length == 0) || trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{parameterName} must be 1-{maxLength} characters.", parameterName);
        }

        return trimmed;
    }
}
