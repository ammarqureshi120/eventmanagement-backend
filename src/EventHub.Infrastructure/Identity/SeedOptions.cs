using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace EventHub.Infrastructure.Identity;

/// <summary>One <c>EventHub:Seed:SystemAdministrators:N</c> entry (FR37). <see cref="Email"/> is null for a blank entry.</summary>
public sealed record SystemAdministratorSeed(int Index, string? Email, string? FirstName, string? LastName)
{
#if EVENTHUB_SEED_TOOLS
    /// <summary>
    /// Development only (Story 1.4 decision, an explicit Development-only override of AD-31 "SysAdmins stay
    /// password-less"): an optional <c>:DevPassword</c> the seeder sets when the user has no password yet and the
    /// host runs in Development. Compiled out of Release, never logged, never in repo files (user-secrets only).
    /// </summary>
    public string? DevPassword { get; init; }

    /// <summary>Keeps <see cref="DevPassword"/> out of any accidental log or debugger output.</summary>
    public override string ToString() => $"SystemAdministratorSeed {{ Index = {Index} }}";
#endif
}

/// <summary>
/// Reads the System Administrator seed list. Each entry is either a plain email string
/// (<c>SystemAdministrators:0=sara@example.test</c>) or an object
/// (<c>SystemAdministrators:0:Email</c>, <c>:FirstName</c>, <c>:LastName</c>, and outside Release an optional
/// Development-only <c>:DevPassword</c>); both forms bind.
/// </summary>
public static class SeedOptions
{
    public const string SystemAdministratorsSection = "EventHub:Seed:SystemAdministrators";

#if EVENTHUB_SEED_TOOLS
    /// <summary>Development-only seed password key (compiled out of Release; a Cecil test checks).</summary>
    public const string DevPasswordKey = "DevPassword";
#endif

    public static IReadOnlyList<SystemAdministratorSeed> ReadSystemAdministrators(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(SystemAdministratorsSection)
            .GetChildren()
            .Select(child => (Index: int.TryParse(child.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : int.MaxValue, Child: child))
            .OrderBy(entry => entry.Index)
            // An entry with children is the object form; some providers also report an empty value for the parent key.
            .Select(entry => entry.Child.GetChildren().Any()
                ? new SystemAdministratorSeed(
                    entry.Index,
                    Blank(entry.Child["Email"]),
                    Blank(entry.Child["FirstName"]),
                    Blank(entry.Child["LastName"]))
                {
#if EVENTHUB_SEED_TOOLS
                    DevPassword = string.IsNullOrEmpty(entry.Child[DevPasswordKey]) ? null : entry.Child[DevPasswordKey],
#endif
                }
                : new SystemAdministratorSeed(entry.Index, Blank(entry.Child.Value), null, null))
            .ToList();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
