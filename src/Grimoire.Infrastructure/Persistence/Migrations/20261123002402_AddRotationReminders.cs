using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRotationReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RotationIntervalDays",
                table: "Secrets",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "RotationIntervalDays",
                table: "GlobalSecrets",
                type: "INTEGER",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "RotationReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SentAt = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotationReminders", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RotationReminders_Key",
                table: "RotationReminders",
                column: "Key",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_RotationReminders_SentAt",
                table: "RotationReminders",
                column: "SentAt"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RotationReminders");

            migrationBuilder.DropColumn(name: "RotationIntervalDays", table: "Secrets");

            migrationBuilder.DropColumn(name: "RotationIntervalDays", table: "GlobalSecrets");
        }
    }
}
