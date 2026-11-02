namespace EventHub.Application.Common.Outbox.Messages;

/// <summary>
/// Outbox message type <c>PasswordResetEmailV1</c> (AD-15, FR4). Versioned: a change of shape is a new record
/// (<c>V2</c>), never an edit, because pending rows may still hold the old JSON. It carries the raw reset token, so the
/// whole payload is encrypted with Data Protection purpose <c>outbox</c> and cleared after send or dead-letter.
/// </summary>
public sealed record PasswordResetEmailV1(
    Guid UserId,
    string Email,
    string DisplayName,
    string Token,
    DateTime ExpiresAtUtc)
{
    /// <summary>The <c>OutboxMessages.Type</c> value of this record.</summary>
    public const string TypeName = nameof(PasswordResetEmailV1);

    /// <summary>Never prints the token or the email (NFR4, AD-22).</summary>
    public override string ToString() => $"{TypeName} {{ UserId = {UserId} }}";
}
