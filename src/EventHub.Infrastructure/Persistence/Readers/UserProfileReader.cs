using EventHub.Application.Common.Ports;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Infrastructure.Persistence.Readers;

/// <summary>
/// AD-20 read path for <see cref="IUserProfileReader"/>: a no-tracking projection through the request's
/// <see cref="AppDbContext"/>, so the RLS session context of the request's data scope applies.
/// </summary>
public sealed class UserProfileReader(AppDbContext db) : IUserProfileReader
{
    public Task<UserProfile?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserProfile(
                user.Id, user.Email, user.FirstName, user.LastName, user.Phone, user.Role, user.OrganizationId, user.Version))
            .SingleOrDefaultAsync(cancellationToken);
}
