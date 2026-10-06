using EventHub.Application.Authorization;
using EventHub.Application.Common.Ports;
using EventHub.Application.Common.Tenancy;
using Mediator;

namespace EventHub.Application.Common.Behaviors;

/// <summary>
/// Pipeline step 2 (AD-5, AD-8): checks the message's static permission against the
/// <see cref="PermissionMatrix"/> for the caller's actor kind and data scope. Runs before validation, so a
/// forbidden caller never learns the shape of the request. Deny by default. An <see cref="IIdentityScoped"/>
/// message first switches the request to the Identity scope (AD-7), so it is checked and run there.
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
        if (message is IIdentityScoped)
        {
            tenantContext.UseIdentityScope();
        }

        var permission = PermissionOf<TMessage>.Value;
        if (permission is null || !matrix.IsGranted(currentUser.Kind, permission.Value, tenantContext.Scope))
        {
            throw new ForbiddenException();
        }

        return next(message, cancellationToken);
    }
}
