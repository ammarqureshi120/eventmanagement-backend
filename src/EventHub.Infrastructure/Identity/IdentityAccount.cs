using System.Security.Cryptography;
using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Ports;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EventHub.Infrastructure.Identity;

/// <summary>
/// AD-26 adapter: credential reads and changes run the Identity stores in the Identity scope (AD-7). Each call opens
/// its own Identity-scope context and connection, so it does not join the caller's transaction; callers that must
/// order it after their command (logout, deactivation) call it after the commit. Lockout (AD-16) uses the
/// configured <see cref="LockoutOptions"/> and is timed by <see cref="IClock"/>. Password reset (Story 1.6) runs the
/// token check, the single-use mark, the new hash, the stamp rotation and the lockout reset in one Identity-scope
/// transaction; the own password change (Story 1.7) does the same after checking the current password instead.
/// </summary>
public sealed class IdentityAccount(DbScopeFactory scopes, IClock clock) : IIdentityAccount
{
    /// <summary>Concurrent sign-ins for one user may race on the failure count; retry the read-modify-write.</summary>
    private const int MaxUpdateAttempts = 3;

    /// <summary>
    /// A hash of a random password, built once (lazily) with the resolved <c>UserManager.PasswordHasher</c>, so it
    /// uses the configured hasher options. Unknown, password-less and locked accounts verify against it, so every
    /// failure path costs exactly one hash verification (NFR4).
    /// </summary>
    private static string? _dummyHash;

    public async Task<PasswordCheck> VerifyPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(password);
        cancellationToken.ThrowIfCancellationRequested();

        await using var scope = scopes.OpenIdentity();
        var users = scope.Services.GetRequiredService<UserManager<User>>();
        var lockout = scope.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Lockout;
        var store = Store(scope);
        var now = clock.UtcNow;

        var user = await store.FindByEmailAsync(users.NormalizeEmail(email.Trim()), cancellationToken);
        var hash = user is null ? null : await store.GetPasswordHashAsync(user, cancellationToken);
        if (user is null || hash is null || await IsLockedOutAsync(store, user, now, cancellationToken))
        {
            // Same work as a real check, result ignored: never reveal whether the email exists or is locked
            // (and a locked account never reveals that it is disabled).
            users.PasswordHasher.VerifyHashedPassword(null!, DummyHash(users.PasswordHasher), password);
            return PasswordCheck.Failed;
        }

        var result = users.PasswordHasher.VerifyHashedPassword(user, hash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            await UpdateAsync(scope, store, user, () => RecordFailureAsync(store, user, lockout, now, cancellationToken), cancellationToken);
            return PasswordCheck.Failed;
        }

        await UpdateAsync(scope, store, user, async () =>
        {
            var changed = await ResetFailuresAsync(store, user, cancellationToken);
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                // An older format or iteration count: upgrade to the current hasher. The stamp stays (no session ends).
                await store.SetPasswordHashAsync(user, users.PasswordHasher.HashPassword(user, password), cancellationToken);
                changed = true;
            }

            return changed;
        }, cancellationToken);

        var account = await ToSessionAccountAsync(store, user, cancellationToken);
        return new PasswordCheck(account.IsActive ? PasswordCheckOutcome.Succeeded : PasswordCheckOutcome.Disabled, account);
    }

    public async Task<SessionAccount?> FindSessionAccountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var scope = scopes.OpenIdentity();
        var store = Store(scope);
        var user = await store.FindByIdAsync(userId.ToString(), cancellationToken);
        if (user is null)
        {
            return null;
        }

        var stamp = await store.GetSecurityStampAsync(user, cancellationToken);
        return stamp is null ? null : Account(user, stamp);
    }

    public async Task RotateSecurityStampAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var scope = scopes.OpenIdentity();
        var store = Store(scope);
        var user = await Find(store, userId, cancellationToken);
        await UpdateAsync(scope, store, user, async () =>
        {
            await store.SetSecurityStampAsync(user, NewSecurityStamp(), cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<PasswordResetResult> CheckResetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hash = UserTokenCodec.HashOf(token);
        if (hash is null)
        {
            return PasswordResetResult.Invalid();
        }

        await using var scope = scopes.OpenIdentity();
        var (result, _, _) = await FindResetTargetAsync(scope, Store(scope), hash, clock.UtcNow, cancellationToken);
        return result;
    }

    public async Task<PasswordResetResult> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newPassword);
        cancellationToken.ThrowIfCancellationRequested();
        var hash = UserTokenCodec.HashOf(token);
        if (hash is null)
        {
            return PasswordResetResult.Invalid();
        }

        await using var scope = scopes.OpenIdentity();
        var users = scope.Services.GetRequiredService<UserManager<User>>();
        var store = Store(scope);
        var db = scope.Db;
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var now = clock.UtcNow;

        var (result, resetToken, user) = await FindResetTargetAsync(scope, store, hash, now, cancellationToken);
        if (!result.Succeeded || resetToken is null || user is null)
        {
            return result;
        }

        // Single use (AD-16): only the request whose conditional update wins may go on; a concurrent submit of the
        // same token waits on the row lock, then matches no row.
        var claimed = await db.UserTokens
            .Where(row => row.Id == resetToken.Id && row.UsedAtUtc == null && row.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.UsedAtUtc, now), cancellationToken);
        if (claimed == 0)
        {
            return PasswordResetResult.Invalid(TokenInvalidReason.Used);
        }

        var stamp = NewSecurityStamp();
        await UpdateAsync(scope, store, user, async () =>
        {
            await store.SetPasswordHashAsync(user, users.PasswordHasher.HashPassword(user, newPassword), cancellationToken);
            await store.SetSecurityStampAsync(user, stamp, cancellationToken);
            await ResetFailuresAsync(store, user, cancellationToken);
            return true;
        }, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return PasswordResetResult.Valid(Account(user, stamp), user.Email);
    }

    public async Task<SessionAccount?> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newPassword);
        cancellationToken.ThrowIfCancellationRequested();

        await using var scope = scopes.OpenIdentity();
        var users = scope.Services.GetRequiredService<UserManager<User>>();
        var store = Store(scope);
        var db = scope.Db;
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);

        var user = await Find(store, userId, cancellationToken);
        var stamp = NewSecurityStamp();
        var disabled = false;
        var wrongCurrent = false;

        // The checks run inside the change, so a concurrency retry (row reloaded after a reset or deactivation committed
        // in between) checks the fresh row again instead of overwriting it.
        await UpdateAsync(scope, store, user, async () =>
        {
            if (user.Status != UserStatus.Active)
            {
                disabled = true;
                return false;
            }

            // By id, with no failure count and no lockout check: the caller is signed in (Story 1.7 design note).
            var hash = await store.GetPasswordHashAsync(user, cancellationToken);
            if (hash is null)
            {
                users.PasswordHasher.VerifyHashedPassword(null!, DummyHash(users.PasswordHasher), currentPassword);
                wrongCurrent = true;
                return false;
            }

            if (users.PasswordHasher.VerifyHashedPassword(user, hash, currentPassword) == PasswordVerificationResult.Failed)
            {
                wrongCurrent = true;
                return false;
            }

            await store.SetPasswordHashAsync(user, users.PasswordHasher.HashPassword(user, newPassword), cancellationToken);
            await store.SetSecurityStampAsync(user, stamp, cancellationToken);
            await ResetFailuresAsync(store, user, cancellationToken);
            return true;
        }, cancellationToken);

        if (disabled)
        {
            throw new AccountDisabledException();
        }

        if (wrongCurrent)
        {
            return null;
        }

        await transaction.CommitAsync(cancellationToken);
        return Account(user, stamp);
    }

#if EVENTHUB_SEED_TOOLS
    /// <summary>Seed-only (AD-31): stores Identity's one-way hash; skips the password policy (Stories 1.4-1.6).</summary>
    public async Task SetPasswordForSeedAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        await using var scope = scopes.OpenIdentity();
        var users = scope.Services.GetRequiredService<UserManager<User>>();
        var store = (IUserPasswordStore<User>)scope.Services.GetRequiredService<IUserStore<User>>();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User not found.");

        await store.SetPasswordHashAsync(user, users.PasswordHasher.HashPassword(user, password), cancellationToken);
        Ensure(await users.UpdateSecurityStampAsync(user));
    }
#endif

    /// <summary>
    /// The reset token with <paramref name="hash"/> and its user, when both are good: the token exists for purpose
    /// reset, is unused and unexpired (a superseded token was expired on supersede), and the user is Active.
    /// </summary>
    private static async Task<(PasswordResetResult Result, UserToken? Token, User? User)> FindResetTargetAsync(
        DbScope scope, UserStore store, byte[] hash, DateTime now, CancellationToken cancellationToken)
    {
        var token = await scope.Db.UserTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.TokenHash == hash && row.Purpose == UserTokenPurpose.Reset, cancellationToken);
        if (token is null)
        {
            return (PasswordResetResult.Invalid(), null, null);
        }

        if (token.UsedAtUtc is not null)
        {
            return (PasswordResetResult.Invalid(TokenInvalidReason.Used), null, null);
        }

        if (token.ExpiresAtUtc <= now)
        {
            return (PasswordResetResult.Invalid(TokenInvalidReason.Expired), null, null);
        }

        var user = await store.FindByIdAsync(token.UserId.ToString(), cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            return (PasswordResetResult.Invalid(), null, null);
        }

        var stamp = await store.GetSecurityStampAsync(user, cancellationToken) ?? string.Empty;
        return (PasswordResetResult.Valid(Account(user, stamp), user.Email), token, user);
    }

    private static async Task<bool> IsLockedOutAsync(UserStore store, User user, DateTime now, CancellationToken cancellationToken) =>
        await store.GetLockoutEnabledAsync(user, cancellationToken)
        && await store.GetLockoutEndDateAsync(user, cancellationToken) is { } end
        && end > new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));

    /// <summary>
    /// Counts one failure; the <see cref="LockoutOptions.MaxFailedAccessAttempts"/>-th consecutive one locks the
    /// account for <see cref="LockoutOptions.DefaultLockoutTimeSpan"/> and resets the count, as Identity does.
    /// </summary>
    private static async Task<bool> RecordFailureAsync(
        UserStore store, User user, LockoutOptions lockout, DateTime now, CancellationToken cancellationToken)
    {
        var count = await store.IncrementAccessFailedCountAsync(user, cancellationToken);
        if (count >= lockout.MaxFailedAccessAttempts)
        {
            var end = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)) + lockout.DefaultLockoutTimeSpan;
            await store.SetLockoutEndDateAsync(user, end, cancellationToken);
            await store.ResetAccessFailedCountAsync(user, cancellationToken);
        }

        return true;
    }

    /// <summary>A right password clears the failure count and any expired lockout end; true when anything changed.</summary>
    private static async Task<bool> ResetFailuresAsync(UserStore store, User user, CancellationToken cancellationToken)
    {
        var changed = false;
        if (await store.GetAccessFailedCountAsync(user, cancellationToken) != 0)
        {
            await store.ResetAccessFailedCountAsync(user, cancellationToken);
            changed = true;
        }

        if (await store.GetLockoutEndDateAsync(user, cancellationToken) is not null)
        {
            await store.SetLockoutEndDateAsync(user, null, cancellationToken);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Applies <paramref name="change"/> and saves it; on a concurrency conflict (another request updated the row
    /// first) reloads the row and applies the change again, a bounded number of times.
    /// </summary>
    private static async Task UpdateAsync(
        DbScope scope, UserStore store, User user, Func<Task<bool>> change, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (!await change())
            {
                return;
            }

            var result = await store.UpdateAsync(user, cancellationToken);
            if (result.Succeeded)
            {
                return;
            }

            if (attempt >= MaxUpdateAttempts)
            {
                Ensure(result);
            }

            await scope.Db.Entry(user).ReloadAsync(cancellationToken);
        }
    }

    private static string DummyHash(IPasswordHasher<User> hasher) =>
        LazyInitializer.EnsureInitialized(
            ref _dummyHash, () => hasher.HashPassword(null!, Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));

    private static UserStore Store(DbScope scope) => (UserStore)scope.Services.GetRequiredService<IUserStore<User>>();

    /// <summary>A user always has a stamp (set on create); one without gets it now, so a session can be validated.</summary>
    private static async Task<SessionAccount> ToSessionAccountAsync(UserStore store, User user, CancellationToken cancellationToken)
    {
        var stamp = await store.GetSecurityStampAsync(user, cancellationToken);
        if (stamp is null)
        {
            stamp = NewSecurityStamp();
            await store.SetSecurityStampAsync(user, stamp, cancellationToken);
            Ensure(await store.UpdateAsync(user, cancellationToken));
        }

        return Account(user, stamp);
    }

    private static SessionAccount Account(User user, string stamp) =>
        new(user.Id, user.Role, user.OrganizationId, DisplayName(user), stamp, user.Status);

    /// <summary>The full name (empty for a seeded System Administrator without names; never the email).</summary>
    private static string DisplayName(User user) => $"{user.FirstName} {user.LastName}".Trim();

    /// <summary>Same strength as Identity's own stamp: 160 random bits.</summary>
    private static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(20));

    private static async Task<User> Find(UserStore store, Guid userId, CancellationToken cancellationToken) =>
        await store.FindByIdAsync(userId.ToString(), cancellationToken) ?? throw new NotFoundException("User not found.");

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Identity update failed: " + string.Join(", ", result.Errors.Select(error => error.Code)));
        }
    }
}
