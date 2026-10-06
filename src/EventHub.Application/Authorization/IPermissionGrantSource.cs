using EventHub.Application.Common.Ports;

namespace EventHub.Application.Authorization;

/// <summary>One allowed combination of actor kind, permission and data scope (AD-8).</summary>
public sealed record PermissionGrant(ActorKind Actor, Permission Permission, ScopeKind Scope);

/// <summary>
/// Contributes grants to the <see cref="PermissionMatrix"/>. Feature stories add sources; the
/// integration test host adds test-only grants.
/// </summary>
public interface IPermissionGrantSource
{
    IEnumerable<PermissionGrant> Grants { get; }
}
