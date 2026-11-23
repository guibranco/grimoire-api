using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfigurationRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EnvironmentKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    Key = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Change = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    ValueType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: true
                    ),
                    RolledBackFrom = table.Column<int>(type: "INTEGER", nullable: true),
                    ChangedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationRevisions", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationRevisions_ApplicationId",
                table: "ConfigurationRevisions",
                column: "ApplicationId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationRevisions_Scope_EnvironmentKey_Key_Revision",
                table: "ConfigurationRevisions",
                columns: new[] { "Scope", "EnvironmentKey", "Key", "Revision" },
                unique: true
            );

            // History starts from today's values: one "baseline" revision per existing entry.
            const string newId =
                "upper(substr(h, 1, 8) || '-' || substr(h, 9, 4) || '-4' || substr(h, 14, 3) || '-' || "
                + "substr('89AB', 1 + (abs(random()) % 4), 1) || substr(h, 18, 3) || '-' || substr(h, 21, 12))";
            migrationBuilder.Sql(
                $"""
                INSERT INTO "ConfigurationRevisions"
                    ("Id", "Scope", "ApplicationId", "EnvironmentKey", "Key", "Revision", "Change",
                     "Value", "ValueType", "Description", "RolledBackFrom", "ChangedBy", "ChangedAt")
                SELECT {newId}, 0, c."ApplicationId", lower(replace(c."EnvironmentId", '-', '')), c."Key", 1,
                       'baseline', c."Value", c."ValueType", c."Description", NULL, 'system', c."UpdatedAt"
                FROM (SELECT *, lower(hex(randomblob(16))) AS h FROM "ConfigurationEntries") c;
                """
            );
            migrationBuilder.Sql(
                $"""
                INSERT INTO "ConfigurationRevisions"
                    ("Id", "Scope", "ApplicationId", "EnvironmentKey", "Key", "Revision", "Change",
                     "Value", "ValueType", "Description", "RolledBackFrom", "ChangedBy", "ChangedAt")
                SELECT {newId}, 1, NULL, g."EnvironmentSlug", g."Key", 1,
                       'baseline', g."Value", g."ValueType", g."Description", NULL, 'system', g."UpdatedAt"
                FROM (SELECT *, lower(hex(randomblob(16))) AS h FROM "GlobalConfigurationEntries") g;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ConfigurationRevisions");
        }
    }
}
