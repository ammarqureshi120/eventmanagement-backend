using EventHub.Application.Common.Ports;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EventHub.Infrastructure.Email;

/// <summary>
/// AD-15 <see cref="IEmailSender"/> adapter: MailKit over SMTP (<c>EventHub:Smtp:*</c>; Mailpit in dev). One connection
/// per send; the outbox relay is the only caller. <c>Message-Id</c> is <c>{OutboxMessage.Id}@{sender domain}</c>, so a
/// retried send carries the same id. Once the server accepted the message, a failing disconnect is only logged (by
/// type): the email was delivered and must not be retried.
/// </summary>
public sealed partial class MailKitEmailSender(IOptions<SmtpOptions> options, ILogger<MailKitEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var smtp = options.Value;
        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            throw new InvalidOperationException("SMTP host is not configured.");
        }

        var from = MailboxAddress.Parse(smtp.From);
        using var mime = new MimeMessage();
        mime.From.Add(from);
        mime.To.Add(new MailboxAddress(message.ToName ?? string.Empty, message.ToAddress));
        mime.Subject = message.Subject;
        mime.MessageId = MessageIdFor(message.MessageId, from.Domain);
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        try
        {
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception exception)
        {
            LogDisconnectFailed(logger, message.MessageId, exception.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 4020, Level = LogLevel.Warning, Message = "SMTP disconnect failed after message {OutboxMessageId} was sent: {ErrorType}")]
    private static partial void LogDisconnectFailed(ILogger logger, Guid outboxMessageId, string errorType);

    /// <summary>The <c>Message-Id</c> (without angle brackets) for an outbox message.</summary>
    public static string MessageIdFor(Guid outboxMessageId, string? domain) =>
        $"{outboxMessageId:D}@{(string.IsNullOrWhiteSpace(domain) ? "eventhub.local" : domain)}";
}
