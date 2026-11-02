#if EVENTHUB_SEED_TOOLS
using System.Net.Http.Json;
using EventHub.Application.Common.Outbox.Messages;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Outbox;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Api.IntegrationTests.Fixtures.Arrange;

/// <summary>One <c>OutboxMessages</c> row as stored.</summary>
public sealed record OutboxRow(
    Guid Id,
    string Type,
    string? Payload,
    int Attempts,
    DateTime? NextAttemptAtUtc,
    DateTime? LeaseUntilUtc,
    DateTime? ProcessedAtUtc,
    string? LastError);

/// <summary>A pending reset email, decrypted.</summary>
public sealed record PendingResetEmail(Guid OutboxId, string RawPayload, PasswordResetEmailV1 Message);

/// <summary>AD-31 arrange helpers for the password-reset tests (Story 1.6).</summary>
public static class PasswordResetArrange
{
    public const string NewPassword = "Brand-New-Pass-42";

    public static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string? email) =>
        client.PostAsJsonAsync("/api/auth/forgot-password", new { email }, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> ResetAsync(HttpClient client, string? token, string? newPassword) =>
        client.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword }, TestContext.Current.CancellationToken);

    /// <summary>
    /// The user's reset emails still waiting in the outbox (payload not cleared), oldest first, decrypted with the host's
    /// key ring as the relay would.
    /// </summary>
    public static async Task<IReadOnlyList<PendingResetEmail>> PendingResetEmailsAsync(
        EventHubApiFactory factory, LocalSqlFixture sql, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var protector = OutboxPayload.Protector(factory.Services.GetRequiredService<IDataProtectionProvider>());
        var result = new List<PendingResetEmail>();
        await using var connection = await sql.OpenAsync(DataScope.System, TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            "SELECT Id, Payload FROM dbo.OutboxMessages WHERE Type = @type AND Payload IS NOT NULL ORDER BY CreatedAtUtc, Id",
            connection);
        command.Parameters.AddWithValue("@type", PasswordResetEmailV1.TypeName);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            var payload = reader.GetString(1);
            PasswordResetEmailV1 message;
            try
            {
                message = OutboxPayload.Unprotect<PasswordResetEmailV1>(protector, payload);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                continue; // Another host's key ring (a database-free test); not ours.
            }

            if (message.UserId == userId)
            {
                result.Add(new PendingResetEmail(reader.GetGuid(0), payload, message));
            }
        }

        return result;
    }

    /// <summary>The raw token of the user's newest pending reset email.</summary>
    public static async Task<string> LatestTokenAsync(EventHubApiFactory factory, LocalSqlFixture sql, Guid userId) =>
        (await PendingResetEmailsAsync(factory, sql, userId))[^1].Message.Token;

    public static async Task<OutboxRow> OutboxRowAsync(LocalSqlFixture sql, Guid id)
    {
        await using var connection = await sql.OpenAsync(DataScope.System, TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            "SELECT Id, Type, Payload, Attempts, NextAttemptAtUtc, LeaseUntilUtc, ProcessedAtUtc, LastError FROM dbo.OutboxMessages WHERE Id = @id",
            connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), "Outbox row not found.");
        return new OutboxRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3),
            Utc(reader, 4),
            Utc(reader, 5),
            Utc(reader, 6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    /// <summary>The user's reset tokens as stored: hash, expiry and use time.</summary>
    public static async Task<IReadOnlyList<(byte[] Hash, string Purpose, DateTime ExpiresAtUtc, DateTime? UsedAtUtc)>> TokensAsync(
        LocalSqlFixture sql, Guid userId)
    {
        await using var connection = await sql.OpenAsync(DataScope.Identity, TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            "SELECT TokenHash, Purpose, ExpiresAtUtc, UsedAtUtc FROM dbo.UserTokens WHERE UserId = @id ORDER BY ExpiresAtUtc, Id",
            connection);
        command.Parameters.AddWithValue("@id", userId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var tokens = new List<(byte[], string, DateTime, DateTime?)>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tokens.Add(((byte[])reader[0], reader.GetString(1), Utc(reader, 2)!.Value, Utc(reader, 3)));
        }

        return tokens;
    }

    public static Task<int> TotalAsync(LocalSqlFixture sql, string table) =>
        sql.ScalarAsync<int>(DataScope.System, $"SELECT COUNT(*) FROM dbo.[{table}]");

    /// <summary>
    /// An Invited user (no command creates one before Epic 2): inserted directly in the Identity scope, which RLS lets
    /// write <c>Users</c>. No password, as an invitee has none.
    /// </summary>
    public static async Task<Guid> InsertInvitedAsync(LocalSqlFixture sql, string email)
    {
        var id = Guid.NewGuid();
        await sql.ExecuteAsync(
            DataScope.Identity,
            """
            INSERT INTO dbo.Users (Id, OrganizationId, Role, Status, FirstName, LastName, Email, NormalizedEmail, Version,
                                   CreatedAtUtc, UpdatedAtUtc, AccessFailedCount, LockoutEnabled, SecurityStamp)
            VALUES (@id, NULL, 'SystemAdministrator', 'Invited', N'In', N'Vitee', @email, @normalized, 1,
                    SYSUTCDATETIME(), SYSUTCDATETIME(), 0, 0, 'INVITED0STAMP')
            """,
            ("@id", id), ("@email", email), ("@normalized", User.NormalizeEmail(email)));
        return id;
    }

    private static DateTime? Utc(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
}
#endif
