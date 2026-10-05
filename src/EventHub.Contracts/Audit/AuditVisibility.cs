namespace EventHub.Contracts.Audit;

/// <summary>
/// How the <c>AuditWriter</c> resolves the stored <c>Visibility</c> (Platform | Tenant) of an
/// <see cref="AuditAction"/> member (AD-14).
/// </summary>
public enum AuditVisibilityRule
{
    /// <summary>Always Platform (Organization actions).</summary>
    Platform,

    /// <summary>Always Tenant (venues, events, ticket types, registrations, exports).</summary>
    Tenant,

    /// <summary>User lifecycle: Platform when the target is an Org Administrator, else Tenant.</summary>
    ByTargetRole,

    /// <summary>Platform with a null Organization when the actor is a System Administrator, else Tenant (actor's org).</summary>
    ByActor,
}

/// <summary>Declares the visibility rule of one <see cref="AuditAction"/> member (AD-14).</summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class AuditVisibilityAttribute(AuditVisibilityRule rule) : Attribute
{
    public AuditVisibilityRule Rule { get; } = rule;
}
