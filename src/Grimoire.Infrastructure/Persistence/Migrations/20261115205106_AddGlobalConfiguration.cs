using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InheritGlobals",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: true
            );

            migrationBuilder.CreateTable(
                name: "GlobalConfigurationEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentSlug = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    Key = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalConfigurationEntries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "GlobalSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalSecrets", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "GlobalSecretVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GlobalSecretId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentSlug = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    EncryptedValue = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    NotBefore = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalSecretVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GlobalSecretVersions_GlobalSecrets_GlobalSecretId",
                        column: x => x.GlobalSecretId,
                        principalTable: "GlobalSecrets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GlobalConfigurationEntries_EnvironmentSlug_Key",
                table: "GlobalConfigurationEntries",
                columns: new[] { "EnvironmentSlug", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_GlobalSecrets_Name",
                table: "GlobalSecrets",
                column: "Name",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_GlobalSecretVersions_GlobalSecretId_EnvironmentSlug_Version",
                table: "GlobalSecretVersions",
                columns: new[] { "GlobalSecretId", "EnvironmentSlug", "Version" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "GlobalConfigurationEntries");

            migrationBuilder.DropTable(name: "GlobalSecretVersions");

            migrationBuilder.DropTable(name: "GlobalSecrets");

            migrationBuilder.DropColumn(name: "InheritGlobals", table: "Applications");
        }
    }
}
