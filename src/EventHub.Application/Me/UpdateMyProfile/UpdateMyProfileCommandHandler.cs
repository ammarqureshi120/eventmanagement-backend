using EventHub.Application.Common.Errors;
using EventHub.Application.Common.Ports;
using Mediator;

namespace EventHub.Application.Me.UpdateMyProfile;

/// <summary>
/// Loads the caller's User aggregate, checks the version (AD-24), and calls <c>User.EditProfile</c>. The explicit
/// version check rejects a stale version, and runs first, so a stale save is a conflict even when it would change
/// nothing. A save racing another one that commits after this read loses at SaveChanges, because EF uses the loaded
/// <c>Version</c> as the concurrency token's original value. A real change
/// raises <c>UserProfileEdited</c> (→ one <c>user.edited</c> entry, <see cref="UserProfileEditedAuditMapper"/>) and the
/// version interceptor bumps <c>Version</c>; no change leaves the row untouched.
/// </summary>
public sealed class UpdateMyProfileCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateMyProfileCommand>
{
    public async ValueTask<Unit> Handle(UpdateMyProfileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = currentUser.UserId ?? throw new InvalidOperationException("Editing a profile needs a signed-in caller.");
        var user = await db.FindUserAsync(userId, cancellationToken)
                   ?? throw new NotFoundException("The signed-in user was not found.");
        if (user.Version != command.Version)
        {
            throw new ConcurrencyConflictException();
        }

        user.EditProfile(command.FirstName, command.LastName, command.Phone);
        return Unit.Value;
    }
}
