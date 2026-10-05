using System.Data;
using System.Data.Common;
using EventHub.Infrastructure.Persistence.Scopes;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EventHub.Infrastructure.Persistence.Interceptors;

/// <summary>
/// AD-7 layer 3: on every connection open, sets the read-only SQL session context the RLS predicates read
/// (<c>Scope</c> lowercase and, for a tenant scope, <c>OrganizationId</c>). With no scope it sets nothing, so
/// RLS returns no rows (fail closed). Pooled connections are cleaned by <c>sp_reset_connection</c>.
/// Always parameterized.
/// </summary>
public sealed class RlsSessionContextInterceptor(ScopeAccessor scopeAccessor) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection, scopeAccessor.Current);
        command?.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection, scopeAccessor.Current);
        if (command is not null)
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>Builds the parameterized <c>sp_set_session_context</c> batch, or null when there is no scope.</summary>
    internal static DbCommand? CreateCommand(DbConnection connection, DataScope scope)
    {
        if (scope.SessionValue is null)
        {
            return null;
        }

        var command = connection.CreateCommand();
        command.CommandText =
            "EXEC sys.sp_set_session_context @key = N'Scope', @value = @scope, @read_only = 1;";
        AddParameter(command, "@scope", DbType.String, scope.SessionValue, size: 16);

        if (scope.OrganizationId is { } organizationId)
        {
            command.CommandText +=
                " EXEC sys.sp_set_session_context @key = N'OrganizationId', @value = @organizationId, @read_only = 1;";
            AddParameter(command, "@organizationId", DbType.Guid, organizationId);
        }

        return command;
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value, int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        if (size is not null)
        {
            parameter.Size = size.Value;
        }

        command.Parameters.Add(parameter);
    }
}
