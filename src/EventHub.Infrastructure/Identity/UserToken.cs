namespace EventHub.Infrastructure.Identity;

/// <summary>What a <see cref="UserToken"/> is for (AD-16). Stored as its name (varchar).</summary>
public enum UserTokenPurpose
{
    /// <summary>Password reset (Story 1.6), 1 h by default.</summary>
    Reset,

    /// <summary>Invitation acceptance (Epic 2), 72 h.</summary>
    Invite,
}

/// <summary>
/// A single-use emailed token (AD-15, AD-16): only the SHA-256 of its 32 random bytes is stored
/// (<c>UX_UserTokens_TokenHash</c>); the raw value lives only in the email and, until sent, in the encrypted outbox
/// payload. Created only by <see cref="UserTokenIssuer"/>; read and used only in the Identity scope. A newer token of
/// the same purpose supersedes older unused ones by setting their expiry to now.
/// </summary>
public sealed class UserToken
{
    /// <summary>Bytes of randomness in a raw token.</summary>
    public const int TokenBytes = 32;

    /// <summary>Bytes of a SHA-256 hash.</summary>
    public const int HashBytes = 32;

    private UserToken()
    {
        // EF Core materialization.
    }

    public UserToken(Guid id, Guid userId, UserTokenPurpose purpose, byte[] tokenHash, DateTime expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        if (tokenHash.Length != HashBytes)
        {
            throw new ArgumentException("A token hash is a SHA-256 (32 bytes).", nameof(tokenHash));
        }

        Id = id;
        UserId = userId;
        Purpose = purpose;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public UserTokenPurpose Purpose { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>Set once, by a conditional update, when the token is used.</summary>
    public DateTime? UsedAtUtc { get; private set; }
}
