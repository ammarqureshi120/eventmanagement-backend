using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventHub.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// AD-7 / AD-30 (Story 1.4): the Identity scope may INSERT only its own Audit Entries (sign-in, sign-out and, later,
    /// reset completed and invite accepted) and still reads none. <c>sec.fn_audit_write</c> is <c>sec.fn_audit</c> plus
    /// an Identity branch limited to those actions; it replaces only the AuditEntries AFTER INSERT block predicate. The
    /// AFTER UPDATE block predicate and the FILTER predicate stay on <c>sec.fn_audit</c>, so Identity can neither see
    /// nor update Audit Entries. No model change: the snapshot is untouched.
    /// </summary>
    public partial class Auth_AuditIdentityInsert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION sec.fn_audit_write(@OrganizationId uniqueidentifier, @Visibility varchar(16), @Action varchar(64))
                RETURNS TABLE WITH SCHEMABINDING AS RETURN
                  SELECT 1 AS ok
                   WHERE @OrganizationId = CAST(SESSION_CONTEXT(N'OrganizationId') AS uniqueidentifier)
                      OR (CAST(SESSION_CONTEXT(N'Scope') AS nvarchar(16)) = N'platform' AND @Visibility = 'Platform')
                      OR CAST(SESSION_CONTEXT(N'Scope') AS nvarchar(16)) = N'system'
                      OR (CAST(SESSION_CONTEXT(N'Scope') AS nvarchar(16)) = N'identity'
                          AND @Action IN ('user.signedIn', 'user.signedOut', 'user.passwordResetCompleted', 'invite.accepted'));
                """);

            migrationBuilder.Sql("""
                ALTER SECURITY POLICY sec.TenantIsolation
                  ALTER BLOCK PREDICATE sec.fn_audit_write(OrganizationId, Visibility, Action) ON dbo.AuditEntries AFTER INSERT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER SECURITY POLICY sec.TenantIsolation
                  ALTER BLOCK PREDICATE sec.fn_audit(OrganizationId, Visibility) ON dbo.AuditEntries AFTER INSERT;
                """);

            migrationBuilder.Sql("DROP FUNCTION sec.fn_audit_write;");
        }
    }
}
