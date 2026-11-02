using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using EventHub.Application.Common.Outbox.Messages;
using EventHub.Application.Common.Ports;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventHub.Infrastructure.Outbox;

/// <summary>
/// AD-15: one relay pass, in the System scope (AD-7). It claims up to <see cref="OutboxOptions.BatchSize"/> due rows
/// atomically (<c>UPDATE TOP(n) … WITH (UPDLOCK, READPAST) … OUTPUT</c>, setting a lease), so concurrent relays on any
/// replica never take the same row. Right before each row's turn the lease is renewed conditionally; a row whose lease
/// another relay has taken meanwhile is skipped. Each row is decrypted, rendered and sent with <c>Message-Id</c> = its
/// id, then marked: sent → <c>ProcessedAtUtc</c> set and payload cleared; a transient failure → <c>Attempts</c>+1 and
/// <c>NextAttemptAtUtc</c> backed off, dead-lettered (<c>NextAttemptAtUtc</c> null, payload cleared) at
/// <see cref="OutboxOptions.MaxAttempts"/>; a permanent failure (unknown type, no or unreadable payload, an expired
/// reset token) → dead-lettered at once. <c>LastError</c> and the logs hold only an exception type or a code: never a
/// payload, an address, a body or a token (AD-22). Marks are guarded by the row's current lease.
/// </summary>
public sealed partial class OutboxProcessor(
    DbScopeFactory scopes,
    IClock clock,
    IEmailSender sender,
    IDataProtectionProvider dataProtection,
    PasswordResetEmailRenderer passwordResetRenderer,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger)
{
    /// <summary><c>LastError</c> code for a row whose <c>Type</c> no renderer knows.</summary>
    public const string UnknownTypeError = "unknown_type";

    /// <summary><c>LastError</c> code for a pending row without a payload.</summary>
    public const string MissingPayloadError = "missing_payload";

    /// <summary><c>LastError</c> code for a reset email whose token expired before it could be sent.</summary>
    public const string TokenExpiredError = "token_expired";

    private const string ClaimSql = """
        UPDATE TOP (@batch) dbo.OutboxMessages WITH (UPDLOCK, READPAST)
           SET LeaseUntilUtc = @leaseUntil
        OUTPUT inserted.Id, inserted.Type, inserted.Payload, inserted.Attempts
         WHERE ProcessedAtUtc IS NULL
           AND NextAttemptAtUtc IS NOT NULL
           AND NextAttemptAtUtc <= @now
           AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc <= @now)
        """;

    /// <summary>Claims and processes one batch; returns how many rows were claimed.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var now = clock.UtcNow;
        var claimLease = now.AddSeconds(settings.LeaseSeconds);

        List<ClaimedOutboxMessage> claimed;
        await using (var scope = scopes.OpenSystem())
        {
            claimed = await scope.Db.Database
                .SqlQueryRaw<ClaimedOutboxMessage>(
                    ClaimSql,
                    new SqlParameter("@batch", SqlDbType.Int) { Value = Math.Max(1, settings.BatchSize) },
                    new SqlParameter("@leaseUntil", SqlDbType.DateTime2) { Value = claimLease },
                    new SqlParameter("@now", SqlDbType.DateTime2) { Value = now })
                .ToListAsync(cancellationToken);
        }

        foreach (var message in claimed)
        {
            var lease = await RenewLeaseAsync(message, claimLease, settings, cancellationToken);
            if (lease is null)
            {
                LogLeaseLost(logger, message.Id, message.Type);
                continue;
            }

            await ProcessAsync(message, lease.Value, settings, cancellationToken);
        }

        return claimed.Count;
    }

    /// <summary>
    /// Extends this pass's lease right before the row's turn, only while the pass still owns it; null when another
    /// relay re-claimed the row after the claim lease ran out (earlier sends took too long).
    /// </summary>
    private async Task<DateTime?> RenewLeaseAsync(
        ClaimedOutboxMessage message, DateTime currentLease, OutboxOptions settings, CancellationToken cancellationToken)
    {
        var renewed = clock.UtcNow.AddSeconds(settings.LeaseSeconds);
        await using var scope = scopes.OpenSystem();
        var updated = await Owned(scope, message.Id, currentLease)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LeaseUntilUtc, renewed), cancellationToken);
        return updated == 0 ? null : renewed;
    }

    private async Task ProcessAsync(
        ClaimedOutboxMessage message, DateTime leaseUntil, OutboxOptions settings, CancellationToken cancellationToken)
    {
        SendOutcome outcome;
        try
        {
            outcome = await SendAsync(message, settings, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Shutting down: the lease runs out and another pass picks the row up.
        }
        catch (Exception exception)
        {
            outcome = SendOutcome.Retry(exception.GetType().Name);
        }

        if (outcome.Error is null)
        {
            await MarkSentAsync(message, leaseUntil);
        }
        else
        {
            await MarkFailedAsync(message, leaseUntil, outcome, settings, cancellationToken);
        }
    }

    /// <summary>Sends the row, or says why not: a transient failure is thrown, a permanent one is returned.</summary>
    private async Task<SendOutcome> SendAsync(ClaimedOutboxMessage message, OutboxOptions settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(message.Payload))
        {
            return SendOutcome.DeadLetter(MissingPayloadError);
        }

        var protector = OutboxPayload.Protector(dataProtection);
        EmailMessage email;
        switch (message.Type)
        {
            case PasswordResetEmailV1.TypeName:
                PasswordResetEmailV1 reset;
                try
                {
                    reset = OutboxPayload.Unprotect<PasswordResetEmailV1>(protector, message.Payload);
                }
                catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidDataException)
                {
                    // Undecryptable or unreadable: retrying cannot help.
                    return SendOutcome.DeadLetter(exception.GetType().Name);
                }

                if (reset.ExpiresAtUtc <= clock.UtcNow)
                {
                    // The link would no longer work; never mail a dead link.
                    return SendOutcome.DeadLetter(TokenExpiredError);
                }

                email = passwordResetRenderer.Render(message.Id, reset);
                break;
            default:
                return SendOutcome.DeadLetter(UnknownTypeError);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.SendTimeoutSeconds)));
        await sender.SendAsync(email, timeout.Token);
        return SendOutcome.Sent;
    }

    private async Task MarkSentAsync(ClaimedOutboxMessage message, DateTime leaseUntil)
    {
        var now = clock.UtcNow;
        await using var scope = scopes.OpenSystem();
        var updated = await Owned(scope, message.Id, leaseUntil)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.ProcessedAtUtc, now)
                    .SetProperty(row => row.Payload, (string?)null)
                    .SetProperty(row => row.LeaseUntilUtc, (DateTime?)null)
                    .SetProperty(row => row.LastError, (string?)null),
                CancellationToken.None); // Sent is sent: record it even while shutting down.

        if (updated == 0)
        {
            LogLeaseLost(logger, message.Id, message.Type);
            return;
        }

        LogSent(logger, message.Id, message.Type, message.Attempts + 1);
    }

    private async Task MarkFailedAsync(
        ClaimedOutboxMessage message, DateTime leaseUntil, SendOutcome outcome, OutboxOptions settings, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var attempts = message.Attempts + 1;
        var deadLetter = outcome.Permanent || attempts >= settings.MaxAttempts;
        var error = outcome.Error!;
        var lastError = error.Length <= OutboxMessage.LastErrorMaxLength ? error : error[..OutboxMessage.LastErrorMaxLength];

        await using var scope = scopes.OpenSystem();
        var owned = Owned(scope, message.Id, leaseUntil);
        var updated = deadLetter
            ? await owned.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.Attempts, attempts)
                    .SetProperty(row => row.NextAttemptAtUtc, (DateTime?)null)
                    .SetProperty(row => row.LeaseUntilUtc, (DateTime?)null)
                    .SetProperty(row => row.Payload, (string?)null)
                    .SetProperty(row => row.LastError, lastError),
                cancellationToken)
            : await owned.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.Attempts, attempts)
                    .SetProperty(row => row.NextAttemptAtUtc, now + OutboxOptions.Backoff(attempts))
                    .SetProperty(row => row.LeaseUntilUtc, (DateTime?)null)
                    .SetProperty(row => row.LastError, lastError),
                cancellationToken);

        if (updated == 0)
        {
            LogLeaseLost(logger, message.Id, message.Type);
        }
        else if (deadLetter)
        {
            LogDeadLettered(logger, message.Id, message.Type, attempts, lastError);
        }
        else
        {
            LogFailed(logger, message.Id, message.Type, attempts, lastError);
        }
    }

    /// <summary>The row, only while this pass still owns its lease.</summary>
    private static IQueryable<OutboxMessage> Owned(DbScope scope, Guid id, DateTime leaseUntil) =>
        scope.Db.OutboxMessages.Where(row => row.Id == id && row.ProcessedAtUtc == null && row.LeaseUntilUtc == leaseUntil);

    [LoggerMessage(EventId = 4000, Level = LogLevel.Information, Message = "Outbox message {OutboxMessageId} ({OutboxType}) sent on attempt {Attempt}")]
    private static partial void LogSent(ILogger logger, Guid outboxMessageId, string outboxType, int attempt);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Outbox message {OutboxMessageId} ({OutboxType}) failed on attempt {Attempt}: {ErrorType}; retrying later")]
    private static partial void LogFailed(ILogger logger, Guid outboxMessageId, string outboxType, int attempt, string errorType);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Error, Message = "Outbox message {OutboxMessageId} ({OutboxType}) dead-lettered after {Attempt} attempts: {ErrorType}")]
    private static partial void LogDeadLettered(ILogger logger, Guid outboxMessageId, string outboxType, int attempt, string errorType);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "Outbox message {OutboxMessageId} ({OutboxType}) lease lost to another relay; left to it")]
    private static partial void LogLeaseLost(ILogger logger, Guid outboxMessageId, string outboxType);

    /// <summary>Sent (<see cref="Error"/> null), a transient failure to retry, or a permanent one to dead-letter now.</summary>
    private readonly record struct SendOutcome(string? Error, bool Permanent)
    {
        public static SendOutcome Sent => default;

        public static SendOutcome Retry(string error) => new(error, false);

        public static SendOutcome DeadLetter(string error) => new(error, true);
    }
}

/// <summary>The columns the claim returns (unmapped query type).</summary>
internal sealed class ClaimedOutboxMessage
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? Payload { get; set; }

    public int Attempts { get; set; }

    /// <summary>Never prints the payload (AD-22).</summary>
    public override string ToString() => $"ClaimedOutboxMessage {{ Id = {Id}, Type = {Type} }}";
}
