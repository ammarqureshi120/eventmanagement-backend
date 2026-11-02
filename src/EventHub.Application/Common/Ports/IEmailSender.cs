namespace EventHub.Application.Common.Ports;

/// <summary>
/// Sends one email (AD-15). Only the outbox relay calls it; features never send mail. The adapter is MailKit over
/// SMTP (Mailpit in dev).
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends <paramref name="message"/>; throws when the SMTP server refuses it or cannot be reached.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// One rendered email. <see cref="MessageId"/> is the outbox message id, sent as the <c>Message-Id</c> header so a
/// retried send is recognisable downstream (AD-15). Bodies may carry a token link, so they are never logged.
/// </summary>
public sealed record EmailMessage(
    Guid MessageId,
    string ToAddress,
    string? ToName,
    string Subject,
    string TextBody,
    string HtmlBody)
{
    /// <summary>Keeps the recipient and the bodies (which may hold a token link) out of logs (AD-22).</summary>
    public override string ToString() => $"EmailMessage {{ MessageId = {MessageId} }}";
}
