using System.Globalization;
using System.Net;
using EventHub.Application.Common.Outbox.Messages;
using EventHub.Application.Common.Ports;
using EventHub.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace EventHub.Infrastructure.Outbox;

/// <summary>
/// Renders <see cref="PasswordResetEmailV1"/> as a plain-text + HTML email (copy: user decision 2026-11-02). The link is
/// <c>{EventHub:App:PublicBaseUrl}/reset-password?token=…</c>, never the request host (AD-15); the plain-text part
/// carries the link in full.
/// </summary>
public sealed class PasswordResetEmailRenderer(IOptions<AppOptions> app, IOptions<UserTokenOptions> tokens)
{
    public const string Subject = "Reset your EventHub password";
    public const string ButtonText = "Set a new password";
    public const string FallbackName = "there";

    /// <summary>The reset link for <paramref name="token"/>.</summary>
    public string Link(string token) =>
        app.Value.Link("reset-password?token=" + Uri.EscapeDataString(token));

    public EmailMessage Render(Guid messageId, PasswordResetEmailV1 message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var name = string.IsNullOrWhiteSpace(message.DisplayName) ? FallbackName : message.DisplayName.Trim();
        var link = Link(message.Token);
        var copy = Copy(name, Lifetime(tokens.Value.ResetTokenHours));

        var text = $"{copy}\r\n\r\n{ButtonText}: {link}\r\n";
        var html = $"""
            <!doctype html>
            <html lang="en">
            <body style="font-family: Arial, sans-serif; color: #1F2933; line-height: 1.55;">
            <p>{WebUtility.HtmlEncode(copy)}</p>
            <p><a href="{WebUtility.HtmlEncode(link)}" style="display: inline-block; padding: 12px 20px; border-radius: 8px; background: #53677A; color: #FCFDFE; text-decoration: none; font-weight: 600;">{ButtonText}</a></p>
            </body>
            </html>
            """;

        return new EmailMessage(
            messageId,
            message.Email,
            string.IsNullOrWhiteSpace(message.DisplayName) ? null : message.DisplayName.Trim(),
            Subject,
            text,
            html);
    }

    /// <summary>The body copy, with the token lifetime spelled as the copy has it ("1 hour").</summary>
    public static string Copy(string name, string lifetime) =>
        $"Hi {name}, we got a request to reset your EventHub password. Use the button below to set a new one. " +
        $"The link works once and expires in {lifetime}. If you didn't ask for this, you can ignore this email — " +
        "your password stays the same.";

    private static string Lifetime(int hours) =>
        hours == 1 ? "1 hour" : string.Create(CultureInfo.InvariantCulture, $"{hours} hours");
}
