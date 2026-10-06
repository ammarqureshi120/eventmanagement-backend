using System.Security.Cryptography;
using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Ports;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Infrastructure.Identity;

/// <summary>
/// AD-26 adapter: credential reads and changes run the Identity stores in the Identity scope (AD-7). Each call opens
/// its own Identity-scope context and connection, so it does not join the caller's transaction; callers that must
/// order it after their command (logout) call it after the commit.
/// </summary>
public sealed class IdentityAccount(DbScopeFactory scopes) : IIdentityAccount
{
    /// <summary>
    /// A hash of a random password, built once (lazily) with the resolved <c>UserManager.PasswordHasher</c>, so it
    /// uses the configured hasher options. Unknown and password-less accounts verify against it, so every failure path
    /// costs exactly one hash verification (NFR4).
    /// </summary>
    private static string? _dummyHash;

    public async Task<SessionAccount?> VerifyPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(password);
        cancellationToken.ThrowIfCancellationRequested();

        await using var scope = scopes.OpenIdentity();
        var users = scope.Services.GetRequiredService<UserManager<User>>();
        var store = Store(scope);

        var user = await store.FindByEmailAsync(users.NormalizeEmail(email.Trim()), cancellationToken);
        var hash = user is null ? null : await store.GetPasswordHashAsync(user, cancellationToken);
        if (user is null || hash is null)
        {
            // Same work as a real check, result ignored: never reveal whether the email exists.
            users.PasswordHasher.VerifyHashedPassword(null!, DummyHash(users.PasswordHasher), password);
            return null;
        }

        var result = users.PasswordHasher.VerifyHashedPassword(user, hash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // An older format or iteration count: upgrade to the current hasher. The stamp stays (no session ends).
            await store.SetPasswordHashAsync(user, users.PasswordHasher.HashPassword(user, password), cancellationToken);
            Ensure(await store.UpdateAsync(user, cancellationToken));
        }

        return await ToSessionAccountAsync(store, user, cancellationToken);
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
        await store.SetSecurityStampAsync(user, NewSecurityStamp(), cancellationToken);
        Ensure(await store.UpdateAsync(user, cancellationToken));
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
        new(user.Id, user.Role, user.OrganizationId, DisplayName(user), stamp);

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
