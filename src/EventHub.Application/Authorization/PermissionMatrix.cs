using EventHub.Application.Common.Ports;

namespace EventHub.Application.Authorization;

/// <summary>
/// The single implementation of PRD §4.0 (AD-8): actor kind × <see cref="Permission"/> × data scope,
/// composed from every registered <see cref="IPermissionGrantSource"/>. Deny by default: anything not
/// granted is forbidden, including every anonymous call in Story 1.3.
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
        && actor != ActorKind.Anonymous
        && scope != ScopeKind.None
        && _grants.Contains(new PermissionGrant(actor, permission, scope));
}
