namespace EventHub.Infrastructure.Outbox;

/// <summary>
/// One transactional-outbox row (AD-15), owned by Infrastructure (AD-6). Command scopes only insert it; the relay
/// claims and updates it only in the System scope. <see cref="Type"/> names a versioned message record in
/// <c>Application/Common/Outbox/Messages</c>. <see cref="Payload"/> is that record's JSON, encrypted with Data Protection
/// purpose <c>outbox</c>, and cleared (null) once the row is sent or dead-lettered. A pending row has
/// <see cref="ProcessedAtUtc"/> null and <see cref="NextAttemptAtUtc"/> set; a dead-lettered row has
/// <see cref="NextAttemptAtUtc"/> null (never claimed again) after <see cref="OutboxOptions.MaxAttempts"/> failures.
/// <see cref="LastError"/> holds an exception type or a code, never PII or a token.
/// </summary>
public sealed class OutboxMessage
{
    public const int TypeMaxLength = 64;
    public const int LastErrorMaxLength = 256;

    private OutboxMessage()
    {
        // EF Core materialization.
    }

    public OutboxMessage(Guid id, string type, string payload, DateTime createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrEmpty(payload);

        Id = id;
        Type = type;
        Payload = payload;
        CreatedAtUtc = createdAtUtc;
        NextAttemptAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string? Payload { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>A claimed row is skipped by other relays until this passes.</summary>
    public DateTime? LeaseUntilUtc { get; private set; }

    public DateTime? ProcessedAtUtc { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>When the row may be claimed next; null once dead-lettered.</summary>
    public DateTime? NextAttemptAtUtc { get; private set; }

    public string? LastError { get; private set; }
}
