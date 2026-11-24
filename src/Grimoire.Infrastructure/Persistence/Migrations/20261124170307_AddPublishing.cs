using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoPublish",
                table: "Environments",
                type: "INTEGER",
                nullable: false,
                // Existing environments keep today's behaviour plus hot reload: auto-publish on.
                defaultValue: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingChangesSince",
                table: "Environments",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "GlobalPublishing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoPublish = table.Column<bool>(type: "INTEGER", nullable: false),
                    PendingChangesSince = table.Column<DateTimeOffset>(
                        type: "TEXT",
                        nullable: true
                    ),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalPublishing", x => x.Id);
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "GlobalPublishing");

            migrationBuilder.DropColumn(name: "AutoPublish", table: "Environments");

            migrationBuilder.DropColumn(name: "PendingChangesSince", table: "Environments");
        }
    }
}
