using EventHub.Application.Common.Ports;

namespace EventHub.Application.Authorization;

/// <summary>
/// The single implementation of PRD §4.0 (AD-8): actor kind × <see cref="Permission"/> × data scope,
/// composed from every registered <see cref="IPermissionGrantSource"/>. Deny by default: anything not
/// granted is forbidden. An anonymous caller passes only through an explicit Anonymous cell (Story 1.4: login in
/// the Identity scope, <see cref="AppPermissionGrants"/>); no scope (<see cref="ScopeKind.None"/>) is never granted.
/// </summary>
public sealed class PermissionMatrix
{
    private readonly HashSet<PermissionGrant> _grants;

    public PermissionMatrix(IEnumerable<IPermissionGrantSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _grants = sources.SelectMany(source => source.Grants).ToHashSet();
        if (_grants.Any(grant => grant is null || string.IsNullOrWhiteSpace(grant.Permission.Name)))
        {
            throw new ArgumentException("A permission grant names no permission (default(Permission)).", nameof(sources));
        }
    }

    public bool IsGranted(ActorKind actor, Permission permission, ScopeKind scope) =>
        !string.IsNullOrWhiteSpace(permission.Name)
        && scope != ScopeKind.None
        && _grants.Contains(new PermissionGrant(actor, permission, scope));
}
