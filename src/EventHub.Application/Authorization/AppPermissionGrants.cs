using EventHub.Application.Common.Ports;

namespace EventHub.Application.Authorization;

/// <summary>The named permissions of the shipped app (AD-8, AD-27): <c>&lt;Area&gt;.&lt;Action&gt;</c>.</summary>
public static class AppPermissions
{
    /// <summary>Sign in with email and password (FR1).</summary>
    public static readonly Permission AuthLogin = new("Auth.Login");

    /// <summary>Sign out, ending every session of the caller (FR1).</summary>
    public static readonly Permission AuthLogout = new("Auth.Logout");

    /// <summary>Request a password-reset link by email (FR4, FR37).</summary>
    public static readonly Permission AuthForgotPassword = new("Auth.ForgotPassword");

    /// <summary>Set a new password with a reset token (FR4, FR37).</summary>
    public static readonly Permission AuthResetPassword = new("Auth.ResetPassword");

    /// <summary>Read the caller's own identity, role and nav areas (FR5).</summary>
    public static readonly Permission MeGet = new("Me.Get");

    /// <summary>Edit the caller's own first name, last name and phone (FR36).</summary>
    public static readonly Permission MeEditProfile = new("Me.EditProfile");

    /// <summary>Change the caller's own password with the current one (FR36).</summary>
    public static readonly Permission MeChangePassword = new("Me.ChangePassword");

    /// <summary>Deactivate a User (FR2). Story 1.5: System actor only, no endpoint (Epic 2 adds the UI and API).</summary>
    public static readonly Permission UsersDeactivate = new("Users.Deactivate");
}

/// <summary>
/// The production cells of PRD §4.0 (AD-8), added story by story. Login runs in the Identity scope and is the only
/// Anonymous cell; signed-in callers hold it too, so signing in again replaces the session instead of failing; logout runs in the Identity scope too (its audit insert and the stamp rotation are Identity work);
/// <c>/api/me</c> runs in the System Administrator's Platform scope. Story 1.5: deactivation is granted only to the
/// System actor in the System scope (the test Arrange hook; the Identity scope may not insert <c>user.deactivated</c>
/// audit rows under RLS). Story 1.6: forgot and reset password are Anonymous cells in the Identity scope too (a
/// signed-in System Administrator may use them as well). Story 1.7: editing the own profile and changing the own
/// password run in the System Administrator's Platform scope, like <c>/api/me</c>; the Organization roles get them
/// with Epic 2's <c>Users</c> RLS for the Tenant scope.
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
        new(ActorKind.Anonymous, AppPermissions.AuthForgotPassword, ScopeKind.Identity),
        new(ActorKind.SystemAdministrator, AppPermissions.AuthForgotPassword, ScopeKind.Identity),
        new(ActorKind.OrgAdministrator, AppPermissions.AuthForgotPassword, ScopeKind.Identity),
        new(ActorKind.EventManager, AppPermissions.AuthForgotPassword, ScopeKind.Identity),
        new(ActorKind.Anonymous, AppPermissions.AuthResetPassword, ScopeKind.Identity),
        new(ActorKind.SystemAdministrator, AppPermissions.AuthResetPassword, ScopeKind.Identity),
        new(ActorKind.OrgAdministrator, AppPermissions.AuthResetPassword, ScopeKind.Identity),
        new(ActorKind.EventManager, AppPermissions.AuthResetPassword, ScopeKind.Identity),
        new(ActorKind.SystemAdministrator, AppPermissions.MeGet, ScopeKind.Platform),
        new(ActorKind.SystemAdministrator, AppPermissions.MeEditProfile, ScopeKind.Platform),
        new(ActorKind.SystemAdministrator, AppPermissions.MeChangePassword, ScopeKind.Platform),
        new(ActorKind.System, AppPermissions.UsersDeactivate, ScopeKind.System),
    ];
}
