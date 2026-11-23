using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecretReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecretReads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecretId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GlobalSecretId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastReadAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReadCount = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretReads", x => x.Id);
                    table.CheckConstraint(
                        "CK_SecretReads_OneSecret",
                        "(\"SecretId\" IS NULL) <> (\"GlobalSecretId\" IS NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_SecretReads_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_SecretReads_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_SecretReads_GlobalSecrets_GlobalSecretId",
                        column: x => x.GlobalSecretId,
                        principalTable: "GlobalSecrets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_SecretReads_Secrets_SecretId",
                        column: x => x.SecretId,
                        principalTable: "Secrets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SecretReads_ApplicationId",
                table: "SecretReads",
                column: "ApplicationId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SecretReads_EnvironmentId_GlobalSecretId",
                table: "SecretReads",
                columns: new[] { "EnvironmentId", "GlobalSecretId" },
                unique: true,
                filter: "\"GlobalSecretId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SecretReads_EnvironmentId_SecretId",
                table: "SecretReads",
                columns: new[] { "EnvironmentId", "SecretId" },
                unique: true,
                filter: "\"SecretId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SecretReads_GlobalSecretId",
                table: "SecretReads",
                column: "GlobalSecretId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SecretReads_SecretId",
                table: "SecretReads",
                column: "SecretId"
            );

            // Start from the audit log: successful secret.read events per reading environment.
            // An application secret wins over a global of the same name, as in the Consumer API.
            const string newId =
                "upper(substr(h, 1, 8) || '-' || substr(h, 9, 4) || '-4' || substr(h, 14, 3) || '-' || "
                + "substr('89AB', 1 + (abs(random()) % 4), 1) || substr(h, 18, 3) || '-' || substr(h, 21, 12))";
            const string reads = """
                SELECT a."Id" AS "ApplicationId", e."Id" AS "EnvironmentId", ev."Target" AS "Name",
                       a."InheritGlobals" AS "InheritGlobals",
                       MAX(ev."OccurredAt") AS "LastReadAt", COUNT(*) AS "ReadCount"
                FROM "AuditEvents" ev
                JOIN "Applications" a ON a."Slug" = ev."ApplicationSlug" AND a."IsDeleted" = 0
                JOIN "Environments" e ON e."ApplicationId" = a."Id" AND e."Slug" = ev."Environment"
                WHERE ev."Action" = 'secret.read' AND ev."Outcome" = 0 AND ev."Target" IS NOT NULL
                GROUP BY a."Id", e."Id", ev."Target"
                """;
            migrationBuilder.Sql(
                $"""
                INSERT INTO "SecretReads"
                    ("Id", "ApplicationId", "EnvironmentId", "SecretId", "GlobalSecretId", "LastReadAt", "ReadCount")
                SELECT {newId}, r."ApplicationId", r."EnvironmentId", s."Id", NULL, r."LastReadAt", r."ReadCount"
                FROM (SELECT *, lower(hex(randomblob(16))) AS h FROM ({reads})) r
                JOIN "Secrets" s ON s."ApplicationId" = r."ApplicationId" AND s."Name" = r."Name";
                """
            );
            migrationBuilder.Sql(
                $"""
                INSERT INTO "SecretReads"
                    ("Id", "ApplicationId", "EnvironmentId", "SecretId", "GlobalSecretId", "LastReadAt", "ReadCount")
                SELECT {newId}, r."ApplicationId", r."EnvironmentId", NULL, g."Id", r."LastReadAt", r."ReadCount"
                FROM (SELECT *, lower(hex(randomblob(16))) AS h FROM ({reads})) r
                JOIN "GlobalSecrets" g ON g."Name" = r."Name"
                WHERE r."InheritGlobals" = 1
                  AND NOT EXISTS (SELECT 1 FROM "Secrets" s
                                  WHERE s."ApplicationId" = r."ApplicationId" AND s."Name" = r."Name");
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SecretReads");
        }
    }
}
