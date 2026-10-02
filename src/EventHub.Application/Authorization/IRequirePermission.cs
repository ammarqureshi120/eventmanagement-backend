namespace EventHub.Application.Authorization;

/// <summary>
/// Every Mediator command and query implements this (AD-8). The authorization pipeline
/// behavior and the static <c>Permission</c> member arrive with the PermissionMatrix (Story 1.3);
/// the architecture test already fails for any message that does not implement it.
/// </summary>
public interface IRequirePermission;
