namespace EventHub.Application.Common.Ports;

/// <summary>
/// The only way Application changes credential state on a User (AD-26). The Infrastructure adapter runs the
/// ASP.NET Identity stores in the Identity scope. Sign-in, reset and password policy arrive in Stories 1.4-1.6.
/// </summary>
public interface IIdentityAccount
{
    /// <summary>Rotates the security stamp, ending every session of the user on their next request (AD-16).</summary>
    Task RotateSecurityStampAsync(Guid userId, CancellationToken cancellationToken = default);

#if EVENTHUB_SEED_TOOLS
    /// <summary>
    /// Seed-only (AD-31): sets a password for tests and dev seeds, stored only as Identity's one-way hash
    /// (NFR3). Compiled out of Release builds; a test checks the Release assembly.
    /// </summary>
    Task SetPasswordForSeedAsync(Guid userId, string password, CancellationToken cancellationToken = default);
#endif
}
