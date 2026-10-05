using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace EventHub.Infrastructure.Persistence.Rls;

/// <summary>
/// AD-30: the only way a tenant table joins RLS. Adds (or drops) the <c>sec.fn_tenant(OrganizationId)</c>
/// filter predicate plus block predicates AFTER INSERT and AFTER UPDATE to <c>sec.TenantIsolation</c>.
/// DDL cannot take parameters, so table and schema names are validated as plain identifiers.
/// </summary>
public static partial class TenantRlsMigrationExtensions
{
    public const string PolicyName = "sec.TenantIsolation";

    public static OperationBuilder<SqlOperation> AddTenantRls(this MigrationBuilder migrationBuilder, string table, string schema = "dbo")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        return migrationBuilder.Sql(AddTenantRlsSql(table, schema));
    }

    public static OperationBuilder<SqlOperation> DropTenantRls(this MigrationBuilder migrationBuilder, string table, string schema = "dbo")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        return migrationBuilder.Sql(DropTenantRlsSql(table, schema));
    }

    public static string AddTenantRlsSql(string table, string schema = "dbo")
    {
        var target = Target(table, schema);
        return $"""
            ALTER SECURITY POLICY {PolicyName}
                ADD FILTER PREDICATE sec.fn_tenant(OrganizationId) ON {target},
                ADD BLOCK PREDICATE sec.fn_tenant(OrganizationId) ON {target} AFTER INSERT,
                ADD BLOCK PREDICATE sec.fn_tenant(OrganizationId) ON {target} AFTER UPDATE;
            """;
    }

    public static string DropTenantRlsSql(string table, string schema = "dbo")
    {
        var target = Target(table, schema);
        return $"""
            ALTER SECURITY POLICY {PolicyName}
                DROP FILTER PREDICATE ON {target},
                DROP BLOCK PREDICATE ON {target} AFTER INSERT,
                DROP BLOCK PREDICATE ON {target} AFTER UPDATE;
            """;
    }

    private static string Target(string table, string schema)
    {
        Identifier(table, nameof(table));
        Identifier(schema, nameof(schema));
        return $"[{schema}].[{table}]";
    }

    private static void Identifier(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || !IdentifierPattern().IsMatch(value))
        {
            throw new ArgumentException($"'{value}' is not a plain SQL identifier.", parameterName);
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
