#if EVENTHUB_SEED_TOOLS
using EventHub.Api.IntegrationTests.Fixtures;
using EventHub.Api.IntegrationTests.Fixtures.Arrange;
using EventHub.Application.Common.Ports;
using EventHub.Infrastructure.Email;
using EventHub.Infrastructure.Outbox;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;
using static EventHub.Api.IntegrationTests.Fixtures.Arrange.PasswordResetArrange;
using static EventHub.Api.IntegrationTests.Fixtures.Arrange.SignInArrange;

namespace EventHub.Api.IntegrationTests.Features.Outbox;

/// <summary>
/// AD-15 (Story 1.6a): the System-scope outbox relay claims leased rows, sends each exactly once with
/// <c>Message-Id</c> = the row id and a link built from <c>PublicBaseUrl</c>, clears the payload after send, backs off
/// on failure and dead-letters after 10 attempts with a type-only <c>LastError</c>.
/// </summary>
[Collection(LocalSqlCollection.Name)]
public sealed class OutboxRelayTests(LocalSqlFixture sql)
{
    [Fact]
    public async Task Relay_WhenAResetEmailIsPending_SendsItOnceWithThePublicLinkAndClearsThePayload()
    {
        sql.SkipIfUnavailable();
        var (factory, sender) = Factory();
        await using var _ = factory;
        var email = await ArrangeResetAsync(factory);
        var pending = Assert.Single(await PendingResetEmailsAsync(factory, sql, await UserIdAsync(sql, email)));

        await DrainUntilAsync(factory.Services.GetRequiredService<OutboxProcessor>(), pending.OutboxId, row => row.ProcessedAtUtc is not null);

        var sent = Assert.Single(sender.Sent, message => message.MessageId == pending.OutboxId);
        var link = $"{EventHubApiFactory.PublicBaseUrl}/reset-password?token={pending.Message.Token}";
        Assert.Equal(email, sent.ToAddress);
        Assert.Equal($"{FirstName} {LastName}", sent.ToName);
        Assert.Equal("Reset your EventHub password", sent.Subject);
        Assert.StartsWith($"Hi {FirstName} {LastName}, we got a request to reset your EventHub password.", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains("The link works once and expires in 1 hour.", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains($"Set a new password: {link}", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains($"href=\"{link}\"", sent.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(">Set a new password</a>", sent.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("ignore this email — your password stays the same.", sent.HtmlBody, StringComparison.Ordinal);

        var row = await OutboxRowAsync(sql, pending.OutboxId);
        Assert.Equal(factory.Clock.UtcNow, row.ProcessedAtUtc);
        Assert.Null(row.Payload);
        Assert.Null(row.LeaseUntilUtc);
        Assert.Null(row.LastError);

        // Never again: a processed row is not claimed by later passes.
        factory.Clock.UtcNow = factory.Clock.UtcNow.AddHours(2);
        await factory.Services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(TestContext.Current.CancellationToken);
        Assert.Single(sender.Sent, message => message.MessageId == pending.OutboxId);
    }

    [Fact]
    public async Task Relay_WhenTheDisplayNameIsEmpty_GreetsWithThere()
    {
        sql.SkipIfUnavailable();
        var email = UniqueEmail("noname");
        var (factory, sender) = Factory(Seed(UniqueEmail("seed"), passwordlessEmail: email));
        await using var _ = factory;
        using var client = factory.CreateApiClient();
        await ForgotAsync(client, email);
        var pending = Assert.Single(await PendingResetEmailsAsync(factory, sql, await UserIdAsync(sql, email)));

        await DrainUntilAsync(factory.Services.GetRequiredService<OutboxProcessor>(), pending.OutboxId, row => row.ProcessedAtUtc is not null);

        var sent = Assert.Single(sender.Sent, message => message.MessageId == pending.OutboxId);
        Assert.StartsWith("Hi there, we got a request", sent.TextBody, StringComparison.Ordinal);
        Assert.Null(sent.ToName);
    }

    [Fact]
    public async Task Relay_WhenTwoProcessorsRunAtOnce_SendsTheRowExactlyOnce()
    {
        sql.SkipIfUnavailable();
        var (factory, sender) = Factory();
        await using var _ = factory;
        sender.Delay = TimeSpan.FromMilliseconds(150);
        var email = await ArrangeResetAsync(factory);
        var pending = Assert.Single(await PendingResetEmailsAsync(factory, sql, await UserIdAsync(sql, email)));
        var first = ActivatorUtilities.CreateInstance<OutboxProcessor>(factory.Services);
        var second = ActivatorUtilities.CreateInstance<OutboxProcessor>(factory.Services);

        for (var pass = 0; pass < 50 && (await OutboxRowAsync(sql, pending.OutboxId)).ProcessedAtUtc is null; pass++)
        {
            await Task.WhenAll(
                first.ProcessBatchAsync(TestContext.Current.CancellationToken),
                second.ProcessBatchAsync(TestContext.Current.CancellationToken));
        }

        Assert.NotNull((await OutboxRowAsync(sql, pending.OutboxId)).ProcessedAtUtc);
        Assert.Single(sender.Sent, message => message.MessageId == pending.OutboxId);
        Assert.Equal(sender.Sent.Count, sender.Sent.Select(message => message.MessageId).Distinct().Count());
    }

    [Fact]
    public async Task Relay_WhenSmtpKeepsFailing_BacksOffExponentiallyThenDeadLettersAtTheTenthAttemptWithoutPii()
    {
        sql.SkipIfUnavailable();
        var sink = new InMemoryLogSink();
        // The longest token life, so the ~4.5 h of backoff never hits the expired-token dead-letter.
        var settings = Seed(UniqueEmail("backoff"));
        settings["EventHub:Auth:ResetTokenHours"] = "168";
        var (factory, sender) = Factory(settings, services => services.AddSingleton<ILogEventSink>(sink));
        await using var _ = factory;
        sender.Fail = true;
        var email = await ArrangeResetAsync(factory);
        var pending = Assert.Single(await PendingResetEmailsAsync(factory, sql, await UserIdAsync(sql, email)));
        var processor = factory.Services.GetRequiredService<OutboxProcessor>();
        var expectedDelays = new[] { 30, 60, 120, 240, 480, 960, 1920, 3600, 3600 };

        for (var attempt = 1; attempt <= 9; attempt++)
        {
            var row = await DrainUntilAsync(processor, pending.OutboxId, current => current.Attempts == attempt);
            Assert.Equal(factory.Clock.UtcNow.AddSeconds(expectedDelays[attempt - 1]), row.NextAttemptAtUtc);
            Assert.Null(row.LeaseUntilUtc);
            Assert.Null(row.ProcessedAtUtc);
            Assert.NotNull(row.Payload);
            Assert.Equal(nameof(SimulatedSmtpException), row.LastError);

            // Not due yet: one second early nothing happens to the row.
            factory.Clock.UtcNow = row.NextAttemptAtUtc!.Value.AddSeconds(-1);
            await processor.ProcessBatchAsync(TestContext.Current.CancellationToken);
            Assert.Equal(attempt, (await OutboxRowAsync(sql, pending.OutboxId)).Attempts);
            factory.Clock.UtcNow = row.NextAttemptAtUtc!.Value;
        }

        var dead = await DrainUntilAsync(processor, pending.OutboxId, current => current.Attempts == 10);
        Assert.Null(dead.NextAttemptAtUtc);
        Assert.Null(dead.Payload);
        Assert.Null(dead.LeaseUntilUtc);
        Assert.Null(dead.ProcessedAtUtc);
        Assert.Equal(nameof(SimulatedSmtpException), dead.LastError);

        // Dead-lettered rows are never claimed again, even once SMTP recovers.
        sender.Fail = false;
        factory.Clock.UtcNow = factory.Clock.UtcNow.AddDays(1);
        for (var pass = 0; pass < 5; pass++)
        {
            await processor.ProcessBatchAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(10, (await OutboxRowAsync(sql, pending.OutboxId)).Attempts);
        Assert.DoesNotContain(sender.Sent, message => message.MessageId == pending.OutboxId);
        Assert.DoesNotContain(sink.RenderAll(), line =>
            line.Contains(CapturingEmailSender.FailureSecret, StringComparison.Ordinal)
            || line.Contains(pending.Message.Token, StringComparison.Ordinal)
            || line.Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Relay_WhenARowsLeaseWasTakenBeforeItsTurn_SkipsItInsteadOfSendingIt()
    {
        sql.SkipIfUnavailable();
        var (factory, sender) = Factory();
        await using var _ = factory;
        var processor = factory.Services.GetRequiredService<OutboxProcessor>();
        await DrainAsync(processor);
        var email = await ArrangeResetAsync(factory);
        await ArrangeResetAsync(factory);
        var ids = (await PendingResetEmailsAsync(factory, sql, await UserIdAsync(sql, email))).Select(pending => pending.OutboxId).ToList();
        Assert.Equal(2, ids.Count);
        var claimLease = factory.Clock.UtcNow.AddSeconds(60);
        var foreignLease = claimLease.AddHours(1);

        // While the first row is being sent (as if it took longer than the lease), another relay re-claims every row
        // this pass has not started yet.
        var stolen = 0;
        sender.OnSend = async message =>
        {
            if (Interlocked.Exchange(ref stolen, 1) == 0)
            {
                await sql.ExecuteAsync(
                    DataScope.System,
                    "UPDATE dbo.OutboxMessages SET LeaseUntilUtc = @foreign WHERE LeaseUntilUtc = @claim AND Id <> @current AND ProcessedAtUtc IS NULL",
                    ("@foreign", foreignLease), ("@claim", claimLease), ("@current", message.MessageId));
            }
        };

        Assert.Equal(2, await processor.ProcessBatchAsync(TestContext.Current.CancellationToken));

        var sent = Assert.Single(sender.Sent, message => ids.Contains(message.MessageId));
        var skipped = await OutboxRowAsync(sql, ids.Single(id => id != sent.MessageId));
        Assert.Null(skipped.ProcessedAtUtc);
        Assert.Equal(foreignLease, skipped.LeaseUntilUtc);
        Assert.Equal(0, skipped.Attempts);
        Assert.NotNull(skipped.Payload);
    }

    [Fact]
    public async Task Relay_WhenTheResetTokenExpiredBeforeSending_DeadLettersWithoutSending()
    {
        sql.SkipIfUnavailable();
        var (factory, sender) = Factory();
        await using var _ = factory;
        var email = await ArrangeResetAsync(factory);
        var pending = Assert.Single(await PendingResetEmailsAsync(factory, sql, await UserIdAsync(sql, email)));
        factory.Clock.UtcNow = factory.Clock.UtcNow.AddHours(1);

        var row = await DrainUntilAsync(factory.Services.GetRequiredService<OutboxProcessor>(), pending.OutboxId, current => current.Attempts == 1);

        Assert.DoesNotContain(sender.Sent, message => message.MessageId == pending.OutboxId);
        Assert.Equal(OutboxProcessor.TokenExpiredError, row.LastError);
        Assert.Null(row.NextAttemptAtUtc);
        Assert.Null(row.Payload);
        Assert.Null(row.ProcessedAtUtc);
    }

    [Theory]
    [InlineData("SomethingElseV9", "irrelevant", OutboxProcessor.UnknownTypeError)]
    [InlineData("PasswordResetEmailV1", "not-a-protected-payload", "CryptographicException")]
    public async Task Relay_WhenARowCanNeverBeSent_DeadLettersItOnTheFirstAttempt(string type, string payload, string expectedError)
    {
        sql.SkipIfUnavailable();
        var (factory, sender) = Factory();
        await using var _ = factory;
        var id = Guid.NewGuid();
        await sql.ExecuteAsync(
            DataScope.System,
            "INSERT INTO dbo.OutboxMessages (Id, Type, Payload, CreatedAtUtc, Attempts, NextAttemptAtUtc) VALUES (@id, @type, @payload, @now, 0, @now)",
            ("@id", id), ("@type", type), ("@payload", payload), ("@now", factory.Clock.UtcNow));

        var row = await DrainUntilAsync(factory.Services.GetRequiredService<OutboxProcessor>(), id, current => current.Attempts == 1);

        Assert.Equal(expectedError, row.LastError);
        Assert.Null(row.NextAttemptAtUtc);
        Assert.Null(row.Payload);
        Assert.Null(row.ProcessedAtUtc);
        Assert.DoesNotContain(sender.Sent, message => message.MessageId == id);
    }

    [Fact]
    public async Task OutboxRelay_WhenEnabled_SendsPendingRowsInTheBackground()
    {
        sql.SkipIfUnavailable();
        var settings = Seed(UniqueEmail("hosted"));
        settings["EventHub:Outbox:Enabled"] = "true";
        settings["EventHub:Outbox:PollIntervalSeconds"] = "1";
        var (factory, sender) = Factory(settings);
        await using var _ = factory;
        var email = await ArrangeResetAsync(factory);
        var pending = Assert.Single(await PendingResetEmailsAsync(factory, sql, await UserIdAsync(sql, email)));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((await OutboxRowAsync(sql, pending.OutboxId)).ProcessedAtUtc is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        Assert.NotNull((await OutboxRowAsync(sql, pending.OutboxId)).ProcessedAtUtc);
        Assert.Single(sender.Sent, message => message.MessageId == pending.OutboxId);
    }

    [Fact]
    public void MessageId_ForAnOutboxRow_IsTheRowIdAtTheSenderDomain()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"{id:D}@eventhub.test", MailKitEmailSender.MessageIdFor(id, "eventhub.test"));
        Assert.Equal($"{id:D}@eventhub.local", MailKitEmailSender.MessageIdFor(id, null));
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(7, 1920)]
    [InlineData(8, 3600)]
    [InlineData(10, 3600)]
    public void Backoff_ForTheNthFailure_Is30SecondsDoublingCappedAtOneHour(int attempts, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), OutboxOptions.Backoff(attempts));
    }

    private (EventHubApiFactory Factory, CapturingEmailSender Sender) Factory(
        Dictionary<string, string>? settings = null, Action<IServiceCollection>? configure = null)
    {
        var sender = new CapturingEmailSender();
        var factory = new EventHubApiFactory(
            sql.ConnectionString,
            settings: settings ?? Seed(UniqueEmail("relay")),
            fakeClock: true,
            configureServices: services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
                configure?.Invoke(services);
            });
        var now = DateTime.UtcNow;
        factory.Clock.UtcNow = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        return (factory, sender);
    }

    /// <summary>Requests a reset for the seeded entry-0 System Administrator; returns its email.</summary>
    private static async Task<string> ArrangeResetAsync(EventHubApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        var config = factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var email = config["EventHub:Seed:SystemAdministrators:0:Email"]!;
        Assert.Equal(System.Net.HttpStatusCode.Accepted, (await ForgotAsync(client, email)).StatusCode);
        return email;
    }

    /// <summary>Processes every row that is due now (other tests' leftovers), so the next batch holds only this test's rows.</summary>
    private static async Task DrainAsync(OutboxProcessor processor)
    {
        for (var pass = 0; pass < 50 && await processor.ProcessBatchAsync(TestContext.Current.CancellationToken) > 0; pass++)
        {
            // Keep draining while full or partial batches are still being claimed.
        }
    }

    /// <summary>
    /// Runs passes until <paramref name="done"/> holds for the row (other tests' leftover rows in the shared database
    /// may fill earlier batches); returns the row.
    /// </summary>
    private async Task<OutboxRow> DrainUntilAsync(OutboxProcessor processor, Guid id, Func<OutboxRow, bool> done)
    {
        for (var pass = 0; pass < 50; pass++)
        {
            await processor.ProcessBatchAsync(TestContext.Current.CancellationToken);
            var row = await OutboxRowAsync(sql, id);
            if (done(row))
            {
                return row;
            }
        }

        Assert.Fail("The outbox row did not reach the expected state.");
        return null!;
    }
}
#endif
