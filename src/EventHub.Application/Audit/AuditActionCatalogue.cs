using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json.Serialization;
using EventHub.Contracts.Audit;

namespace EventHub.Application.Audit;

/// <summary>Reads the wire value and visibility rule declared on each <see cref="AuditAction"/> member (AD-14).</summary>
public static class AuditActionCatalogue
{
    private static readonly FrozenDictionary<AuditAction, (string WireValue, AuditVisibilityRule Rule)> Entries =
        Enum.GetValues<AuditAction>().ToFrozenDictionary(action => action, Describe);

    public static IReadOnlyCollection<AuditAction> All => Entries.Keys;

    public static string WireValue(AuditAction action) => Lookup(action).WireValue;

    public static AuditVisibilityRule VisibilityRule(AuditAction action) => Lookup(action).Rule;

    private static (string WireValue, AuditVisibilityRule Rule) Lookup(AuditAction action) =>
        Entries.TryGetValue(action, out var entry)
            ? entry
            : throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown audit action.");

    private static (string WireValue, AuditVisibilityRule Rule) Describe(AuditAction action)
    {
        var field = typeof(AuditAction).GetField(action.ToString(), BindingFlags.Public | BindingFlags.Static)!;
        var wireValue = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name
                        ?? throw new InvalidOperationException($"AuditAction.{action} has no wire value.");
        var rule = field.GetCustomAttribute<AuditVisibilityAttribute>()?.Rule
                   ?? throw new InvalidOperationException($"AuditAction.{action} has no visibility rule.");
        return (wireValue, rule);
    }
}
