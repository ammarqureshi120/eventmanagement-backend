namespace EventHub.Application.Common.Tenancy;

/// <summary>
/// Marks a message that runs in the Identity scope (AD-7): login, logout and, later, reset and accept-invite.
/// The authorization behavior switches the request to <c>ScopeKind.Identity</c> before checking the matrix, which
/// happens before any connection of the request's unit of work opens, so the RLS session context is Identity.
/// </summary>
public interface IIdentityScoped;
