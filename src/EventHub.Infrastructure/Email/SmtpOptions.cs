namespace EventHub.Infrastructure.Email;

/// <summary>
/// SMTP settings (<c>EventHub__Smtp__*</c>). In local dev the AppHost points Host/Port at the
/// native Mailpit process (SMTP 1025).
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "EventHub:Smtp";

    public string? Host { get; set; }

    public int Port { get; set; } = 25;

    public string From { get; set; } = "no-reply@eventhub.local";

    /// <summary>Probe timeout for the <c>/health</c> SMTP check.</summary>
    public int ProbeTimeoutSeconds { get; set; } = 3;
}
