using EventHub.Application.Common.Ports;

namespace EventHub.Application.Authorization;

/// <summary>The named permissions of the shipped app (AD-8, AD-27): <c>&lt;Area&gt;.&lt;Action&gt;</c>.</summary>
public static class AppPermissions
{
    /// <summary>Sign in with email and password (FR1).</summary>
    public static readonly Permission AuthLogin = new("Auth.Login");

    /// <summary>Sign out, ending every session of the caller (FR1).</summary>
    public static readonly Permission AuthLogout = new("Auth.Logout");

    /// <summary>Read the caller's own identity, role and nav areas (FR5).</summary>
    public static readonly Permission MeGet = new("Me.Get");
}

/// <summary>
/// The production cells of PRD §4.0 (AD-8), added story by story. Login runs in the Identity scope and is the only
/// Anonymous cell; signed-in callers hold it too, so signing in again replaces the session instead of failing; logout runs in the Identity scope too (its audit insert and the stamp rotation are Identity work);
/// <c>/api/me</c> runs in the System Administrator's Platform scope.
/// </summary>
public sealed class AppPermissionGrants : IPermissionGrantSource
{
    public IEnumerable<PermissionGrant> Grants =>
    [
        new(ActorKind.Anonymous, AppPermissions.AuthLogin, ScopeKind.Identity),
        new(ActorKind.SystemAdministrator, AppPermissions.AuthLogin, ScopeKind.Identity),
        new(ActorKind.OrgAdministrator, AppPermissions.AuthLogin, ScopeKind.Identity),
        new(ActorKind.EventManager, AppPermissions.AuthLogin, ScopeKind.Identity),
        new(ActorKind.SystemAdministrator, AppPermissions.AuthLogout, ScopeKind.Identity),
        new(ActorKind.SystemAdministrator, AppPermissions.MeGet, ScopeKind.Platform),
    ];
}
