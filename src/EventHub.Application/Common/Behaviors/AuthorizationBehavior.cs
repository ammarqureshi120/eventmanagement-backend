using EventHub.Application.Authorization;
using EventHub.Application.Common.Ports;
using Mediator;

namespace EventHub.Application.Common.Behaviors;

/// <summary>
/// Pipeline step 2 (AD-5, AD-8): checks the message's static permission against the
/// <see cref="PermissionMatrix"/> for the caller's actor kind and data scope. Runs before validation, so a
/// forbidden caller never learns the shape of the request. Deny by default.
/// </summary>
public sealed class AuthorizationBehavior<TMessage, TResponse>(
    PermissionMatrix matrix,
    ICurrentUser currentUser,
    ITenantContext tenantContext) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var permission = PermissionOf<TMessage>.Value;
        if (permission is null || !matrix.IsGranted(currentUser.Kind, permission.Value, tenantContext.Scope))
        {
            throw new ForbiddenException();
        }

        return next(message, cancellationToken);
    }
}
