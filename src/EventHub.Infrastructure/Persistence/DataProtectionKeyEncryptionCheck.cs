using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventHub.Infrastructure.Persistence;

/// <summary>
/// AD-21: outside Development/Testing, logs an Error at startup when the Data Protection key ring has no XML
/// encryptor, i.e. key XML sits in plain text in <c>DataProtectionKeys</c>. It never fails startup; the fix is the
/// hosting follow-up (<c>ProtectKeysWith*</c> via Key Vault/KMS or a certificate).
/// </summary>
public sealed partial class DataProtectionKeyEncryptionCheck(
    IOptions<KeyManagementOptions> options,
    IHostEnvironment environment,
    ILogger<DataProtectionKeyEncryptionCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && options.Value.XmlEncryptor is null)
        {
            LogUnencryptedKeys(logger, environment.EnvironmentName);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 2200, Level = LogLevel.Error,
        Message = "Data Protection keys are stored unencrypted in {Environment}: no key encryption (ProtectKeysWith*) is configured")]
    private static partial void LogUnencryptedKeys(ILogger logger, string environment);
}
