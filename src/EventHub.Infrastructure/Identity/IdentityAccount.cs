using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Ports;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Infrastructure.Identity;

/// <summary>
/// AD-26 adapter: credential changes run the Identity stores in the Identity scope (AD-7). It opens its own
/// Identity-scope context and connection, so it does not join the caller's transaction.
/// </summary>
public sealed class IdentityAccount(DbScopeFactory scopes) : IIdentityAccount
{
    public async Task RotateSecurityStampAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.OpenIdentity();
        var users = scope.Services.GetRequiredService<UserManager<User>>();
        var user = await Find(users, userId);
        Ensure(await users.UpdateSecurityStampAsync(user));
    }

#if EVENTHUB_SEED_TOOLS
    /// <summary>Seed-only (AD-31): stores Identity's one-way hash; skips the password policy (Stories 1.4-1.6).</summary>
    public async Task SetPasswordForSeedAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        await using var scope = scopes.OpenIdentity();
        var users = scope.Services.GetRequiredService<UserManager<User>>();
        var store = (IUserPasswordStore<User>)scope.Services.GetRequiredService<IUserStore<User>>();
        var user = await Find(users, userId);

        await store.SetPasswordHashAsync(user, users.PasswordHasher.HashPassword(user, password), cancellationToken);
        Ensure(await users.UpdateSecurityStampAsync(user));
    }
#endif

    private static async Task<User> Find(UserManager<User> users, Guid userId) =>
        await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User not found.");

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Identity update failed: " + string.Join(", ", result.Errors.Select(error => error.Code)));
        }
    }
}
