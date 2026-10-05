using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventHub.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// AD-21: the Data Protection key ring table (<c>PersistKeysToDbContext</c>, app name <c>EventHub</c>). A global
    /// System table: no <c>OrganizationId</c> and never in <c>sec.TenantIsolation</c>.
    /// </summary>
    public partial class Security_DataProtectionKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WARNING: rolling back deletes the whole key ring. Every antiforgery token, session cookie and other
            // Data Protection payload becomes unreadable, so everyone is logged out (and, from Story 1.6, pending encrypted outbox payloads are lost).
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");
        }
    }
}
