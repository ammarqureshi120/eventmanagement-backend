using EventHub.Application.Common.Ports;
using EventHub.Domain.Users;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventHub.Infrastructure.Identity;

/// <summary>
/// FR37 / AD-16: at startup, creates each configured System Administrator that does not exist yet, in the
/// Identity scope, with role SystemAdministrator, status Active, no Organization and no password. Idempotent
/// by normalized email; a lost unique-index race counts as already seeded. It never updates or removes
/// existing rows. Logs carry indexes and ids only, never the email (AD-22).
/// </summary>
public sealed partial class SystemAdministratorSeeder(
    IConfiguration configuration,
    DbScopeFactory scopes,
    IIdGenerator ids,
    ILogger<SystemAdministratorSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in SeedOptions.ReadSystemAdministrators(configuration))
        {
            if (entry.Email is null)
            {
                LogBlankEntry(logger, entry.Index);
                continue;
            }

            if (!User.IsValidEmail(entry.Email))
            {
                LogInvalidEntry(logger, entry.Index);
                continue;
            }

            if (!seen.Add(User.NormalizeEmail(entry.Email)))
            {
                LogDuplicateEntry(logger, entry.Index);
                continue;
            }

            await SeedAsync(entry, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedAsync(SystemAdministratorSeed entry, CancellationToken cancellationToken)
    {
        await using var scope = scopes.OpenIdentity();
        var users = scope.Services.GetRequiredService<UserManager<User>>();

        var existing = await users.FindByEmailAsync(entry.Email!);
        if (existing is not null)
        {
            LogAlreadySeeded(logger, entry.Index, existing.Id);
            return;
        }

        User user;
        try
        {
            user = User.CreateSystemAdministrator(ids.NewId(), entry.Email!, entry.FirstName, entry.LastName);
        }
        catch (ArgumentException)
        {
            // Malformed email or a name over the limit: skip it, never crash startup, never log the values.
            LogInvalidEntry(logger, entry.Index);
            return;
        }

        try
        {
            var result = await users.CreateAsync(user);
            if (result.Succeeded)
            {
                LogSeeded(logger, entry.Index, user.Id);
            }
            else
            {
                LogRejected(logger, entry.Index, string.Join(",", result.Errors.Select(error => error.Code)));
            }
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Another instance seeded the same email between the lookup and the insert.
            LogAlreadySeeded(logger, entry.Index, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning, Message = "Skipping blank System Administrator seed entry {Index}")]
    private static partial void LogBlankEntry(ILogger logger, int index);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "Skipping System Administrator seed entry {Index}: not a valid email address or name")]
    private static partial void LogInvalidEntry(ILogger logger, int index);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Skipping System Administrator seed entry {Index}: duplicate of an earlier entry")]
    private static partial void LogDuplicateEntry(ILogger logger, int index);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information, Message = "System Administrator seed entry {Index} already exists as user {UserId}")]
    private static partial void LogAlreadySeeded(ILogger logger, int index, Guid? userId);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information, Message = "Seeded System Administrator {UserId} from seed entry {Index}")]
    private static partial void LogSeeded(ILogger logger, int index, Guid userId);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Warning, Message = "System Administrator seed entry {Index} was rejected: {ErrorCodes}")]
    private static partial void LogRejected(ILogger logger, int index, string errorCodes);
}
