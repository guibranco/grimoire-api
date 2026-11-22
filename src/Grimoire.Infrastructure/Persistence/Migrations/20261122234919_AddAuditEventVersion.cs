using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEventVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "AuditEvents",
                type: "INTEGER",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Version", table: "AuditEvents");
        }
    }
}
