#if EVENTHUB_SEED_TOOLS
using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Application.Auth;
using EventHub.Application.Common.Ports;
using EventHub.Infrastructure.Outbox;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;
using static EventHub.Api.IntegrationTests.Fixtures.Arrange.PasswordResetArrange;
using static EventHub.Api.IntegrationTests.Fixtures.Arrange.SignInArrange;

namespace EventHub.Api.IntegrationTests.Features.Auth;

/// <summary>
/// FR4 / FR37 / AD-15 / AD-16 / AD-17 (Story 1.6a): forgot password always answers the same 202 and issues a hashed,
/// single-use, 1-hour token plus an encrypted outbox email only for Active accounts; reset checks the token before the
/// password policy, then sets the password, uses the token, rotates the stamp, clears lockout, audits once and returns
/// the account's email (200, Story 1.6b).
/// </summary>
[Collection(LocalSqlCollection.Name)]
public sealed class PasswordResetTests(LocalSqlFixture sql)
{
    private const string WrongPassword = "Wrong-Passw0rd";

    [Fact]
    public async Task ForgotPassword_ForEveryAccountState_Returns202IdenticallyAndIssuesOnlyForActiveAccounts()
    {
        sql.SkipIfUnavailable();
        var active = UniqueEmail("active");
        var locked = UniqueEmail("locked");
        var inactive = UniqueEmail("inactive");
        var invited = UniqueEmail("invited");
        var settings = Seed(active);
        settings["EventHub:Seed:SystemAdministrators:1:Email"] = locked;
        settings["EventHub:Seed:SystemAdministrators:1:DevPassword"] = Password;
        settings["EventHub:Seed:SystemAdministrators:2:Email"] = inactive;
        await using var factory = Factory(settings);
        using var client = factory.CreateApiClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await LoginAsync(client, locked, WrongPassword);
        }

        await DeactivateAsync(factory, sql, inactive);
        await InsertInvitedAsync(sql, invited);

        var cases = new (string Email, int ExpectedTokens)[]
        {
            (active, 1), (UniqueEmail("unknown"), 0), (invited, 0), (inactive, 0), (locked, 1),
        };
        foreach (var (email, expectedTokens) in cases)
        {
            var tokensBefore = await TotalAsync(sql, "UserTokens");
            var outboxBefore = await TotalAsync(sql, "OutboxMessages");

            var response = await ForgotAsync(client, email);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
            Assert.Null(response.Content.Headers.ContentType);
            Assert.Null(response.Headers.Location);
            Assert.Null(SessionSetCookie(response));
            Assert.Equal(tokensBefore + expectedTokens, await TotalAsync(sql, "UserTokens"));
            Assert.Equal(outboxBefore + expectedTokens, await TotalAsync(sql, "OutboxMessages"));
        }
    }

    [Fact]
    public async Task ForgotPassword_WhenActive_StoresOnlyTheSha256OfThe32ByteTokenWithA1HourExpiryAndAnEncryptedPayload()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("hash");
        await using var factory = Factory(Seed(email));
        using var client = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);

        Assert.Equal(HttpStatusCode.Accepted, (await ForgotAsync(client, email)).StatusCode);

        var pending = Assert.Single(await PendingResetEmailsAsync(factory, sql, userId));
        var token = pending.Message.Token;
        var bytes = Base64Url.DecodeFromChars(token);
        Assert.Equal(32, bytes.Length);
        var stored = Assert.Single(await TokensAsync(sql, userId));
        Assert.Equal(SHA256.HashData(bytes), stored.Hash);
        Assert.Equal("Reset", stored.Purpose);
        Assert.Equal(factory.Clock.UtcNow.AddHours(1), stored.ExpiresAtUtc);
        Assert.Null(stored.UsedAtUtc);
        Assert.Equal(factory.Clock.UtcNow.AddHours(1), pending.Message.ExpiresAtUtc);
        Assert.Equal(email, pending.Message.Email);

        // At rest the payload is ciphertext: neither the token nor the address appears in it.
        Assert.DoesNotContain(token, pending.RawPayload, StringComparison.Ordinal);
        Assert.DoesNotContain(email, pending.RawPayload, StringComparison.OrdinalIgnoreCase);
        var row = await OutboxRowAsync(sql, pending.OutboxId);
        Assert.Equal("PasswordResetEmailV1", row.Type);
        Assert.Equal(0, row.Attempts);
        Assert.Equal(factory.Clock.UtcNow, row.NextAttemptAtUtc);
        Assert.Null(row.ProcessedAtUtc);
    }

    [Fact]
    public async Task ResetPassword_WithAValidToken_SetsThePasswordUsesTheTokenEndsOtherSessionsClearsLockoutAndAuditsOnce()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("reset");
        await using var factory = Factory(Seed(email));
        using var otherDevice = factory.CreateApiClient();
        using var client = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);
        await SignInAsync(otherDevice, email);
        var stampBefore = await SecurityStampAsync(sql, userId);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await LoginAsync(client, email, WrongPassword);
        }

        Assert.NotNull((await CredentialStateAsync(sql, userId)).LockoutEndUtc);
        await ForgotAsync(client, email);
        var token = await LatestTokenAsync(factory, sql, userId);

        var response = await ResetAsync(client, token, NewPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
        {
            // Story 1.6b: the account's email comes back so the SPA can prefill sign-in.
            Assert.Equal(email, body.RootElement.GetProperty("email").GetString(), ignoreCase: true);
        }

        Assert.NotEqual(stampBefore, await SecurityStampAsync(sql, userId));
        Assert.Equal((0, (DateTime?)null), Counts(await CredentialStateAsync(sql, userId)));
        Assert.Equal(factory.Clock.UtcNow, Assert.Single(await TokensAsync(sql, userId)).UsedAtUtc);
        Assert.Equal(1, await AuditCountAsync(sql, userId, "user.passwordResetCompleted"));
        Assert.Equal(1, await sql.ScalarAsync<int>(
            DataScope.System,
            "SELECT COUNT(*) FROM dbo.AuditEntries WHERE EntityId = @id AND Action = 'user.passwordResetCompleted' AND Visibility = 'Platform' AND OrganizationId IS NULL AND ActorId = @id",
            ("@id", userId)));

        // The other device's session ends on its next request; the new password works at once (lockout cleared).
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.GetAsync("/api/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, email, Password)).StatusCode);
        await SignInAsync(client, email, NewPassword);

        // Single use.
        var again = await ResetAsync(client, token, "Another-Pass-77");
        await AssertTokenInvalidAsync(again, "used");
        Assert.Equal(1, await AuditCountAsync(sql, userId, "user.passwordResetCompleted"));
    }

    [Fact]
    public async Task ResetPassword_WhenSysAdminHasNoPasswordYet_SetsTheFirstPassword()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("firstpw");
        await using var factory = Factory(Seed(UniqueEmail("other"), passwordlessEmail: email));
        using var client = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, email, NewPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await ForgotAsync(client, email)).StatusCode);
        var pending = Assert.Single(await PendingResetEmailsAsync(factory, sql, userId));
        Assert.Equal(string.Empty, pending.Message.DisplayName);

        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(client, pending.Message.Token, NewPassword)).StatusCode);
        await SignInAsync(client, email, NewPassword);
    }

    [Fact]
    public async Task ResetPassword_WhenPasswordBreaksThePolicy_ReturnsValidationOnNewPasswordSavesNothingAndKeepsTheTokenUsable()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("weak");
        await using var factory = Factory(Seed(email));
        using var client = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);
        await ForgotAsync(client, email);
        var token = await LatestTokenAsync(factory, sql, userId);
        var stampBefore = await SecurityStampAsync(sql, userId);

        var response = await ResetAsync(client, token, "abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("validation", problem.RootElement.GetProperty("code").GetString());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["newPassword"], errors.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            [PasswordPolicy.TooShort, PasswordPolicy.NeedsNumber],
            errors.GetProperty("newPassword").EnumerateArray().Select(message => message.GetString()));
        Assert.Equal(stampBefore, await SecurityStampAsync(sql, userId));
        Assert.Null(Assert.Single(await TokensAsync(sql, userId)).UsedAtUtc);
        Assert.Equal(0, await AuditCountAsync(sql, userId, "user.passwordResetCompleted"));
        await SignInAsync(client, email, Password);

        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(client, token, NewPassword)).StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ResetPassword_WhenTokenIsBadAndPasswordToo_ReturnsTokenInvalidNotValidation(string? password)
    {
        sql.SkipIfUnavailable();
        await using var factory = Factory(Seed(UniqueEmail("order")));
        using var client = factory.CreateApiClient();
        var unknownToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        await AssertTokenInvalidAsync(await ResetAsync(client, unknownToken, password), reason: null);
        await AssertTokenInvalidAsync(await ResetAsync(client, "not-a-token", password), reason: null);
        await AssertTokenInvalidAsync(await ResetAsync(client, string.Empty, password), reason: null);
        await AssertTokenInvalidAsync(await ResetAsync(client, null, password), reason: null);
        await AssertTokenInvalidAsync(await ResetAsync(client, new string('a', 500), password), reason: null);
    }

    [Fact]
    public async Task ResetPassword_WhenTokenIsOlderThanOneHour_ReturnsTokenInvalidExpiredAndChangesNothing()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("expired");
        await using var factory = Factory(Seed(email));
        using var client = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);
        await ForgotAsync(client, email);
        var token = await LatestTokenAsync(factory, sql, userId);
        var stampBefore = await SecurityStampAsync(sql, userId);

        factory.Clock.UtcNow = factory.Clock.UtcNow.AddMinutes(59).AddSeconds(59);
        (await AssertProblemCodeAsync(await ResetAsync(client, token, "abc"), "validation")).Dispose(); // still valid: the policy speaks
        factory.Clock.UtcNow = factory.Clock.UtcNow.AddSeconds(1);

        await AssertTokenInvalidAsync(await ResetAsync(client, token, NewPassword), "expired");
        Assert.Equal(stampBefore, await SecurityStampAsync(sql, userId));
        Assert.Null(Assert.Single(await TokensAsync(sql, userId)).UsedAtUtc);
        await SignInAsync(client, email, Password);
    }

    [Fact]
    public async Task ResetPassword_WhenANewerTokenWasIssued_RefusesTheOlderAsExpiredAndAcceptsTheNewer()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("supersede");
        await using var factory = Factory(Seed(email));
        using var client = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);
        await ForgotAsync(client, email);
        var older = await LatestTokenAsync(factory, sql, userId);
        factory.Clock.UtcNow = factory.Clock.UtcNow.AddMinutes(1);
        await ForgotAsync(client, email);
        var newer = await LatestTokenAsync(factory, sql, userId);
        Assert.NotEqual(older, newer);

        var tokens = await TokensAsync(sql, userId);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(factory.Clock.UtcNow, tokens[0].ExpiresAtUtc); // superseded = expired now
        Assert.Equal(factory.Clock.UtcNow.AddHours(1), tokens[1].ExpiresAtUtc);

        await AssertTokenInvalidAsync(await ResetAsync(client, older, NewPassword), "expired");
        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(client, newer, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WhenTheAccountIsNoLongerActive_ReturnsTokenInvalidWithoutReason()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("gone");
        await using var factory = Factory(Seed(email));
        using var client = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);
        await ForgotAsync(client, email);
        var token = await LatestTokenAsync(factory, sql, userId);
        await DeactivateAsync(factory, sql, email);
        var stampBefore = await SecurityStampAsync(sql, userId);

        await AssertTokenInvalidAsync(await ResetAsync(client, token, NewPassword), reason: null);
        Assert.Equal(stampBefore, await SecurityStampAsync(sql, userId));
        Assert.Equal(0, await AuditCountAsync(sql, userId, "user.passwordResetCompleted"));
    }

    [Fact]
    public async Task ForgotAndResetAndRelay_WhenLogsAreCaptured_NeverLogTheTokenPasswordOrEmailBody()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("nolog");
        var sink = new InMemoryLogSink();
        var sender = new CapturingEmailSender();
        await using var factory = Factory(Seed(email), services =>
        {
            services.AddSingleton<ILogEventSink>(sink);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
        });
        using var client = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);
        await ForgotAsync(client, email);
        var pending = Assert.Single(await PendingResetEmailsAsync(factory, sql, userId));
        var token = pending.Message.Token;

        var processor = factory.Services.GetRequiredService<OutboxProcessor>();
        for (var pass = 0; pass < 20 && (await OutboxRowAsync(sql, pending.OutboxId)).ProcessedAtUtc is null; pass++)
        {
            await processor.ProcessBatchAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(client, token, NewPassword)).StatusCode);
        var email1 = Assert.Single(sender.Sent, sent => sent.MessageId == pending.OutboxId);

        var lines = sink.RenderAll();
        Assert.Contains(lines, line => line.Contains("ResetPasswordCommand", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(pending.OutboxId.ToString(), StringComparison.Ordinal));
        foreach (var secret in new[] { token, Uri.EscapeDataString(token), NewPassword, pending.RawPayload, email1.HtmlBody, email1.TextBody })
        {
            Assert.DoesNotContain(lines, line => line.Contains(secret, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task ForgotPassword_WhenEmailIsMissingOrMalformed_ReturnsValidationOnEmailAndWritesNothing(string? email)
    {
        sql.SkipIfUnavailable();
        await using var factory = Factory(Seed(UniqueEmail("shape")));
        using var client = factory.CreateApiClient();
        var tokensBefore = await TotalAsync(sql, "UserTokens");
        var outboxBefore = await TotalAsync(sql, "OutboxMessages");

        var response = await ForgotAsync(client, email);

        using var problem = await AssertProblemCodeAsync(response, "validation");
        Assert.Equal(["email"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
        Assert.Equal(tokensBefore, await TotalAsync(sql, "UserTokens"));
        Assert.Equal(outboxBefore, await TotalAsync(sql, "OutboxMessages"));
    }

    [Fact]
    public async Task ForgotPassword_WhenEmailHasSpacesAndOtherCase_IssuesExactlyOneTokenForTheAccount()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("normalize");
        await using var factory = Factory(Seed(email));
        using var client = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);

        Assert.Equal(HttpStatusCode.Accepted, (await ForgotAsync(client, "  " + email.ToUpperInvariant() + " ")).StatusCode);

        Assert.Single(await TokensAsync(sql, userId));
        Assert.Single(await PendingResetEmailsAsync(factory, sql, userId));
    }

    [Fact]
    public async Task ResetPassword_WhenSubmittedTwiceAtOnce_SucceedsExactlyOnce()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("race");
        await using var factory = Factory(Seed(email));
        using var first = factory.CreateApiClient();
        using var second = factory.CreateApiClient();
        var userId = await UserIdAsync(sql, email);
        await ForgotAsync(first, email);
        var token = await LatestTokenAsync(factory, sql, userId);

        var responses = await Task.WhenAll(ResetAsync(first, token, NewPassword), ResetAsync(second, token, "Other-Pass-123"));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(responses, response => response.StatusCode != HttpStatusCode.OK);
        using var _ = await AssertProblemCodeAsync(loser, "token_invalid");
        Assert.Equal(1, await AuditCountAsync(sql, userId, "user.passwordResetCompleted"));
    }

    private EventHubApiFactory Factory(Dictionary<string, string> settings, Action<IServiceCollection>? configureServices = null)
    {
        var factory = new EventHubApiFactory(sql.ConnectionString, settings: settings, fakeClock: true, configureServices: configureServices);
        var now = DateTime.UtcNow;
        factory.Clock.UtcNow = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        return factory;
    }

    private static (int, DateTime?) Counts((int AccessFailedCount, DateTime? LockoutEndUtc, string Status) state) =>
        (state.AccessFailedCount, state.LockoutEndUtc);

    /// <summary>A 400 problem with <paramref name="code"/>; returns the parsed body.</summary>
    private static async Task<JsonDocument> AssertProblemCodeAsync(HttpResponseMessage response, string code)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {raw}");
        var problem = JsonDocument.Parse(raw);
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        return problem;
    }

    /// <summary>400 <c>token_invalid</c> with the given optional <c>reason</c> and no field errors.</summary>
    private static async Task AssertTokenInvalidAsync(HttpResponseMessage response, string? reason)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {raw}");
        using var problem = JsonDocument.Parse(raw);
        var root = problem.RootElement;
        Assert.Equal("token_invalid", root.GetProperty("code").GetString());
        Assert.Equal("This reset link has expired.", root.GetProperty("title").GetString());
        Assert.Equal("https://eventhub.dev/errors/token_invalid", root.GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("errors", out _));
        if (reason is null)
        {
            Assert.False(root.TryGetProperty("reason", out _));
        }
        else
        {
            Assert.Equal(reason, root.GetProperty("reason").GetString());
        }
    }
}
#endif
