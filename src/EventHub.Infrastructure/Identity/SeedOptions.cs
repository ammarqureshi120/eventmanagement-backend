using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace EventHub.Infrastructure.Identity;

/// <summary>One <c>EventHub:Seed:SystemAdministrators:N</c> entry (FR37). <see cref="Email"/> is null for a blank entry.</summary>
public sealed record SystemAdministratorSeed(int Index, string? Email, string? FirstName, string? LastName);

/// <summary>
/// Reads the System Administrator seed list. Each entry is either a plain email string
/// (<c>SystemAdministrators:0=sara@example.test</c>) or an object
/// (<c>SystemAdministrators:0:Email</c>, <c>:FirstName</c>, <c>:LastName</c>); both forms bind.
/// </summary>
public static class SeedOptions
{
    public const string SystemAdministratorsSection = "EventHub:Seed:SystemAdministrators";

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
                : new SystemAdministratorSeed(entry.Index, Blank(entry.Child.Value), null, null))
            .ToList();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
