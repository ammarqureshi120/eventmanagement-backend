using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Ports;
using Mediator;

namespace EventHub.Application.Users.Deactivate;

/// <summary>
/// Loads the User aggregate and calls <c>User.Deactivate()</c> (AD-6). The raised <c>UserDeactivated</c> becomes
/// one <c>user.deactivated</c> Audit Entry in the same transaction (<see cref="UserDeactivatedAudit"/>); an already
/// Inactive user is a no-op with no second entry.
/// </summary>
public sealed class DeactivateUserHandler(IAppDbContext db) : ICommandHandler<DeactivateUserCommand>
{
    public async ValueTask<Unit> Handle(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await db.FindUserAsync(command.UserId, cancellationToken)
                   ?? throw new NotFoundException("User not found.");
        user.Deactivate();
        return Unit.Value;
    }
}
