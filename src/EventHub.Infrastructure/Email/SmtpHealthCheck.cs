using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EventHub.Infrastructure.Email;

/// <summary>
/// AD-21: SMTP reachability, reported on <c>/health</c> detail only (never readiness).
/// Connects and disconnects without authenticating or sending.
/// </summary>
public sealed class SmtpHealthCheck(IOptions<SmtpOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var smtp = options.Value;
        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "SMTP host is not configured.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, smtp.ProbeTimeoutSeconds)));
        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptions.None, timeout.Token);
            await client.DisconnectAsync(quit: true, timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "SMTP probe timed out.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // No exception text in the response (AD-17).
            return new HealthCheckResult(context.Registration.FailureStatus, "SMTP unreachable.");
        }
    }
}
