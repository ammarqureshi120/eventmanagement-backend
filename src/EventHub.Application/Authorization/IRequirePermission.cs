namespace EventHub.Application.Authorization;

/// <summary>
/// Every Mediator command and query implements this (AD-8). The authorization pipeline behavior reads the
/// static <see cref="Permission"/> and checks it against the <see cref="PermissionMatrix"/> before validation
/// runs; an architecture test fails for any message without it.
/// </summary>
public interface IRequirePermission
{
    static abstract Permission Permission { get; }
}
