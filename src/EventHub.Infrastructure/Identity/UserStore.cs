using EventHub.Domain.Users;
using EventHub.Infrastructure.Persistence;
using EventHub.Infrastructure.Persistence.Configurations.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Identity;

/// <summary>
/// AD-26: ASP.NET Identity stores over the domain <c>Users</c> row (no <c>IdentityUser</c> subclass, no role or
/// claim stores). Credentials live in EF shadow properties reached only through <c>Entry(user).Property(...)</c>.
/// Resolve it from an Identity-scope DI scope (<c>DbScopeFactory.OpenIdentity()</c>) so RLS allows the reads.
/// The email is the user name and cannot be changed here (AD-16); users are never deleted (AD-19).
/// </summary>
public sealed class UserStore(AppDbContext db) :
    IUserEmailStore<User>,
    IUserPasswordStore<User>,
    IUserSecurityStampStore<User>,
    IUserLockoutStore<User>
{
    // IUserStore

    public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(NotNull(user).Id.ToString());

    public Task<string?> GetUserNameAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(NotNull(user).Email);

    public Task SetUserNameAsync(User user, string? userName, CancellationToken cancellationToken) =>
        RequireUnchanged(NotNull(user).Email, userName, StringComparison.Ordinal);

    public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(NotNull(user).NormalizedEmail);

    public Task SetNormalizedUserNameAsync(User user, string? normalizedName, CancellationToken cancellationToken) =>
        RequireUnchanged(NotNull(user).NormalizedEmail, normalizedName, StringComparison.Ordinal);

    public async Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken)
    {
        Tracked(user).State = EntityState.Added;
        await db.SaveChangesAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken)
    {
        // AD-24: app-managed concurrency token; the stale-version UPDATE matches no row and throws below.
        Tracked(user).Property(u => u.Version).CurrentValue++;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        }
    }

    public Task<IdentityResult> DeleteAsync(User user, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Users are never deleted (AD-19); deactivate them instead.");

    public async Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Guid.TryParse(userId, out var id)
            ? await db.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken)
            : null;

    public Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedUserName, cancellationToken);

    // IUserEmailStore (email = user name)

    public Task SetEmailAsync(User user, string? email, CancellationToken cancellationToken) =>
        RequireUnchanged(NotNull(user).Email, email, StringComparison.Ordinal);

    public Task<string?> GetEmailAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(NotNull(user).Email);

    /// <summary>Accounts are reached only through emailed invite/reset links, so the address is confirmed.</summary>
    public Task<bool> GetEmailConfirmedAsync(User user, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task SetEmailConfirmedAsync(User user, bool confirmed, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        FindByNameAsync(normalizedEmail, cancellationToken);

    public Task<string?> GetNormalizedEmailAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(NotNull(user).NormalizedEmail);

    public Task SetNormalizedEmailAsync(User user, string? normalizedEmail, CancellationToken cancellationToken) =>
        RequireUnchanged(NotNull(user).NormalizedEmail, normalizedEmail, StringComparison.Ordinal);

    // IUserPasswordStore

    public Task SetPasswordHashAsync(User user, string? passwordHash, CancellationToken cancellationToken)
    {
        Credential<string?>(user, UserCredentialProperties.PasswordHash).CurrentValue = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(Credential<string?>(user, UserCredentialProperties.PasswordHash).CurrentValue);

    public Task<bool> HasPasswordAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(Credential<string?>(user, UserCredentialProperties.PasswordHash).CurrentValue is not null);

    // IUserSecurityStampStore

    public Task SetSecurityStampAsync(User user, string stamp, CancellationToken cancellationToken)
    {
        Credential<string?>(user, UserCredentialProperties.SecurityStamp).CurrentValue = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(Credential<string?>(user, UserCredentialProperties.SecurityStamp).CurrentValue);

    // IUserLockoutStore

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(User user, CancellationToken cancellationToken)
    {
        var end = Credential<DateTime?>(user, UserCredentialProperties.LockoutEndUtc).CurrentValue;
        return Task.FromResult<DateTimeOffset?>(end is null ? null : new DateTimeOffset(end.Value, TimeSpan.Zero));
    }

    public Task SetLockoutEndDateAsync(User user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        Credential<DateTime?>(user, UserCredentialProperties.LockoutEndUtc).CurrentValue = lockoutEnd?.UtcDateTime;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(User user, CancellationToken cancellationToken)
    {
        var count = Credential<int>(user, UserCredentialProperties.AccessFailedCount);
        count.CurrentValue++;
        return Task.FromResult(count.CurrentValue);
    }

    public Task ResetAccessFailedCountAsync(User user, CancellationToken cancellationToken)
    {
        Credential<int>(user, UserCredentialProperties.AccessFailedCount).CurrentValue = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(Credential<int>(user, UserCredentialProperties.AccessFailedCount).CurrentValue);

    public Task<bool> GetLockoutEnabledAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(Credential<bool>(user, UserCredentialProperties.LockoutEnabled).CurrentValue);

    public Task SetLockoutEnabledAsync(User user, bool enabled, CancellationToken cancellationToken)
    {
        Credential<bool>(user, UserCredentialProperties.LockoutEnabled).CurrentValue = enabled;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // The DbContext belongs to the DI scope.
    }

    private Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<User, TValue> Credential<TValue>(User user, string name) =>
        Tracked(user).Property<TValue>(name);

    /// <summary>Shadow values need a tracked entry; a new user is attached first and marked Added on create.</summary>
    private Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<User> Tracked(User user)
    {
        var entry = db.Entry(NotNull(user));
        if (entry.State == EntityState.Detached)
        {
            entry.State = EntityState.Unchanged;
        }

        return entry;
    }

    private static User NotNull(User user) => user ?? throw new ArgumentNullException(nameof(user));

    private static Task RequireUnchanged(string current, string? requested, StringComparison comparison) =>
        string.Equals(current, requested, comparison)
            ? Task.CompletedTask
            : throw new NotSupportedException("The email is the sign-in identity and cannot be changed (AD-16).");
}
