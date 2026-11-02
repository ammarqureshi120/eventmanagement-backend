using EventHub.Domain.Users;

namespace EventHub.Application.Common.Ports;

/// <summary>
/// The only way Application reads or changes credential state on a User (AD-26). The Infrastructure adapter runs
/// the ASP.NET Identity stores in the Identity scope, each call on its own connection, outside the caller's
/// transaction. Reset and the password policy arrive in Story 1.6.
/// </summary>
public interface IIdentityAccount
{
    /// <summary>
    /// Checks <paramref name="password"/> for the account with <paramref name="email"/> (AD-16, FR1, FR2).
    /// <list type="bullet">
    /// <item>Unknown email, no password set, or locked out: <see cref="PasswordCheckOutcome.Failed"/>.</item>
    /// <item>Wrong password: <see cref="PasswordCheckOutcome.Failed"/>; the failure is counted and the 5th
    /// consecutive one locks the account for 15 minutes (timed by <see cref="IClock"/>).</item>
    /// <item>Right password: the failure count is reset; <see cref="PasswordCheckOutcome.Succeeded"/> for an
    /// Active user, else <see cref="PasswordCheckOutcome.Disabled"/>.</item>
    /// </list>
    /// Every failure path runs exactly one password-hash verification, so they take comparable time (NFR4). A
    /// locked account never reveals whether it is disabled.
    /// </summary>
    Task<PasswordCheck> VerifyPasswordAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// The current session state of a user (role, name, status, security stamp), read in the Identity scope; null
    /// when the user does not exist. The security-stamp validator calls it on every authenticated request (AD-16).
    /// </summary>
    Task<SessionAccount?> FindSessionAccountAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates the security stamp, ending every session of the user on their next request (AD-16). Runs on its own
    /// connection, so callers invoke it only after their command has committed; it honours the token.
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

/// <summary>The result of a password check (AD-16).</summary>
public enum PasswordCheckOutcome
{
    /// <summary>Unknown email, no password, locked out or wrong password: always <c>invalid_credentials</c>.</summary>
    Failed,

    /// <summary>Right password, Active user.</summary>
    Succeeded,

    /// <summary>Right password, but the user is not Active: <c>account_disabled</c> (FR2).</summary>
    Disabled,
}

/// <summary>
/// A password check's outcome and, only after a right password (<see cref="PasswordCheckOutcome.Succeeded"/> or
/// <see cref="PasswordCheckOutcome.Disabled"/>), the account.
/// </summary>
public sealed record PasswordCheck(PasswordCheckOutcome Outcome, SessionAccount? Account)
{
    public static PasswordCheck Failed { get; } = new(PasswordCheckOutcome.Failed, null);
}

/// <summary>
/// What a session is built from: the user's id, role, Organization, display name and status, plus the opaque
/// security stamp the session cookie carries so a rotated stamp rejects it (AD-16). Never logged or returned to clients.
/// </summary>
public sealed record SessionAccount(
    Guid UserId,
    UserRole Role,
    Guid? OrganizationId,
    string DisplayName,
    string SecurityStamp,
    UserStatus Status)
{
    /// <summary>The <see cref="ActorKind"/> this account acts as (AD-8).</summary>
    public ActorKind ActorKind => Role switch
    {
        UserRole.SystemAdministrator => ActorKind.SystemAdministrator,
        UserRole.OrgAdministrator => ActorKind.OrgAdministrator,
        UserRole.EventManager => ActorKind.EventManager,
        _ => throw new InvalidOperationException($"Unknown user role {Role}."),
    };

    /// <summary>Only an Active user may hold a session (AD-16, FR2).</summary>
    public bool IsActive => Status == UserStatus.Active;

    /// <summary>Keeps the stamp out of logs and debugger displays.</summary>
    public override string ToString() => $"SessionAccount {{ UserId = {UserId}, Role = {Role}, Status = {Status} }}";
}
