using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventHub.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// AD-7 / AD-19 / AD-30 foundation: READ_COMMITTED_SNAPSHOT, schema <c>sec</c>, the RLS predicate functions
    /// (exactly SOLUTION-DESIGN §4.4) and the <c>sec.TenantIsolation</c> policy (filter + AFTER INSERT/UPDATE block
    /// predicates) over <c>Users</c> and <c>AuditEntries</c>. Later tenant tables join only through <c>migrationBuilder.AddTenantRls(...)</c>.
    /// Runs after <c>Auth_Users</c> because the policy needs the tables.
    /// </summary>
    public partial class Foundation_Rls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AD-19: list and dashboard reads never block on writes. ALTER DATABASE cannot run in a transaction.
            migrationBuilder.Sql(
                "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;",
                suppressTransaction: true);

            migrationBuilder.Sql("EXEC(N'CREATE SCHEMA sec');");

            // Tenant business data: only own org, or system workers.
            migrationBuilder.Sql("""
                CREATE FUNCTION sec.fn_tenant(@OrganizationId uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING AS RETURN
                  SELECT 1 AS ok
                   WHERE @OrganizationId = CAST(SESSION_CONTEXT(N'OrganizationId') AS uniqueidentifier)
                      OR CAST(SESSION_CONTEXT(N'Scope') AS nvarchar(16)) = N'system';
                """);

            // Users: own org, platform, identity (login/reset/invite), system.
            migrationBuilder.Sql("""
                CREATE FUNCTION sec.fn_users(@OrganizationId uniqueidentifier)
                RETURNS TABLE WITH SCHEMABINDING AS RETURN
                  SELECT 1 AS ok
                   WHERE @OrganizationId = CAST(SESSION_CONTEXT(N'OrganizationId') AS uniqueidentifier)
                      OR CAST(SESSION_CONTEXT(N'Scope') AS nvarchar(16)) IN (N'platform', N'identity', N'system');
                """);

            // Audit: own org; platform only for Visibility = 'Platform'.
            migrationBuilder.Sql("""
                CREATE FUNCTION sec.fn_audit(@OrganizationId uniqueidentifier, @Visibility varchar(16))
                RETURNS TABLE WITH SCHEMABINDING AS RETURN
                  SELECT 1 AS ok
                   WHERE @OrganizationId = CAST(SESSION_CONTEXT(N'OrganizationId') AS uniqueidentifier)
                      OR (CAST(SESSION_CONTEXT(N'Scope') AS nvarchar(16)) = N'platform' AND @Visibility = 'Platform')
                      OR CAST(SESSION_CONTEXT(N'Scope') AS nvarchar(16)) = N'system';
                """);

            migrationBuilder.Sql("""
                CREATE SECURITY POLICY sec.TenantIsolation
                  ADD FILTER PREDICATE sec.fn_users(OrganizationId) ON dbo.Users,
                  ADD BLOCK PREDICATE sec.fn_users(OrganizationId) ON dbo.Users AFTER INSERT,
                  ADD BLOCK PREDICATE sec.fn_users(OrganizationId) ON dbo.Users AFTER UPDATE,
                  ADD FILTER PREDICATE sec.fn_audit(OrganizationId, Visibility) ON dbo.AuditEntries,
                  ADD BLOCK PREDICATE sec.fn_audit(OrganizationId, Visibility) ON dbo.AuditEntries AFTER INSERT,
                  ADD BLOCK PREDICATE sec.fn_audit(OrganizationId, Visibility) ON dbo.AuditEntries AFTER UPDATE
                  WITH (STATE = ON);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY sec.TenantIsolation;");
            migrationBuilder.Sql("DROP FUNCTION sec.fn_audit;");
            migrationBuilder.Sql("DROP FUNCTION sec.fn_users;");
            migrationBuilder.Sql("DROP FUNCTION sec.fn_tenant;");
            migrationBuilder.Sql("EXEC(N'DROP SCHEMA sec');");
            migrationBuilder.Sql(
                "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE;",
                suppressTransaction: true);
        }
    }
}
