using System.Data;
using EventHub.Application.Common.Outbox.Messages;
using EventHub.Application.Common.Ports;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Outbox;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventHub.Infrastructure.Identity;

/// <summary>
/// AD-15 / AD-16: the only writer of <see cref="UserToken"/>s and of the outbox message that carries each one. A reset
/// runs in one Identity-scope transaction on its own connection: lock the user row (so concurrent requests for one
/// account serialise and "newest wins" holds), expire every older unused reset token, insert the new token's hash and
/// the encrypted <see cref="PasswordResetEmailV1"/>. Nothing is written for unknown, Invited or Inactive accounts.
/// </summary>
public sealed class UserTokenIssuer(
    DbScopeFactory scopes,
    IClock clock,
    IIdGenerator ids,
    IDataProtectionProvider dataProtection,
    IOptions<UserTokenOptions> options) : IUserTokenIssuer
{
    public async Task IssuePasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        var normalizedEmail = User.NormalizeEmail(email.Trim());

        await using var scope = scopes.OpenIdentity();
        var db = scope.Db;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var user = await db.Users
            .FromSql($"SELECT * FROM dbo.Users WITH (UPDLOCK, ROWLOCK) WHERE NormalizedEmail = {normalizedEmail}")
            .AsNoTracking()
            .Select(row => new { row.Id, row.Status, row.Email, row.FirstName, row.LastName })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            return; // Rolled back on dispose; nothing was written.
        }

        var now = clock.UtcNow;
        await db.UserTokens
            .Where(token => token.UserId == user.Id
                            && token.Purpose == UserTokenPurpose.Reset
                            && token.UsedAtUtc == null
                            && token.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ExpiresAtUtc, now), cancellationToken);

        var (rawToken, hash) = UserTokenCodec.NewToken();
        var expiresAt = now.AddHours(options.Value.ResetTokenHours);
        db.UserTokens.Add(new UserToken(ids.NewId(), user.Id, UserTokenPurpose.Reset, hash, expiresAt));

        var message = new PasswordResetEmailV1(
            user.Id, user.Email, $"{user.FirstName} {user.LastName}".Trim(), rawToken, expiresAt);
        db.OutboxMessages.Add(new OutboxMessage(
            ids.NewId(),
            PasswordResetEmailV1.TypeName,
            OutboxPayload.Protect(OutboxPayload.Protector(dataProtection), message),
            now));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
