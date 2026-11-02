namespace EventHub.Application.Common.Ports;

/// <summary>
/// The only creator of <c>UserToken</c>s and of the emails that carry them (AD-15, AD-16). Each call runs in one
/// Identity-scope transaction: the token row (only its SHA-256 hash) and the matching outbox message (the raw token
/// only inside the Data Protection-encrypted payload) are written together or not at all. Epic 2 adds invites.
/// </summary>
public interface IUserTokenIssuer
{
    /// <summary>
    /// Issues a password-reset token for the Active account with <paramref name="email"/> and enqueues one
    /// <c>PasswordResetEmailV1</c> (FR4, FR37). Earlier unused reset tokens of that user expire now (superseded).
    /// Unknown, Invited and Inactive accounts get nothing, and the caller cannot tell the difference.
    /// </summary>
    Task IssuePasswordResetAsync(string email, CancellationToken cancellationToken = default);
}
