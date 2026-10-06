using EventHub.Domain.Users;

namespace EventHub.Application.Common.Ports;

/// <summary>
/// The only way Application reads or changes credential state on a User (AD-26). The Infrastructure adapter runs
/// the ASP.NET Identity stores in the Identity scope, each call on its own connection, outside the caller's
/// transaction. Lockout, reset and the password policy arrive in Stories 1.5-1.6.
/// </summary>
public interface IIdentityAccount
{
    /// <summary>
    /// Checks <paramref name="password"/> for the account with <paramref name="email"/>. Returns null for a wrong
    /// password, an unknown email or an account with no password; every one of those paths runs exactly one
    /// password-hash verification, so they take comparable time (AD-16, NFR4).
    /// </summary>
    Task<SessionAccount?> VerifyPasswordAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// The current session state of a user (role, name, security stamp), read in the Identity scope; null when the
    /// user does not exist. The security-stamp validator calls it on every authenticated request (AD-16).
    /// </summary>
    Task<SessionAccount?> FindSessionAccountAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates the security stamp, ending every session of the user on their next request (AD-16). Runs on its own
    /// connection, so callers invoke it only after their command has committed.
    /// </summary>
    Task RotateSecurityStampAsync(Guid userId, CancellationToken cancellationToken = default);

#if EVENTHUB_SEED_TOOLS
    /// <summary>
    /// Seed-only (AD-31): sets a password for tests and dev seeds, stored only as Identity's one-way hash
    /// (NFR3). Compiled out of Release builds; a test checks the Release assembly.
    /// </summary>
    Task SetPasswordForSeedAsync(Guid userId, string password, CancellationToken cancellationToken = default);
#endif
}

/// <summary>
/// What a session is built from: the user's id, role, Organization and display name, plus the opaque security
/// stamp the session cookie carries so a rotated stamp rejects it (AD-16). Never logged or returned to clients.
/// </summary>
public sealed record SessionAccount(
    Guid UserId,
    UserRole Role,
    Guid? OrganizationId,
    string DisplayName,
    string SecurityStamp)
{
    /// <summary>The <see cref="ActorKind"/> this account acts as (AD-8).</summary>
    public ActorKind ActorKind => Role switch
    {
        UserRole.SystemAdministrator => ActorKind.SystemAdministrator,
        UserRole.OrgAdministrator => ActorKind.OrgAdministrator,
        UserRole.EventManager => ActorKind.EventManager,
        _ => throw new InvalidOperationException($"Unknown user role {Role}."),
    };

    /// <summary>Keeps the stamp out of logs and debugger displays.</summary>
    public override string ToString() => $"SessionAccount {{ UserId = {UserId}, Role = {Role} }}";
}
