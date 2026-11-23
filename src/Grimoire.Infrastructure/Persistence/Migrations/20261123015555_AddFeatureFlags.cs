using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureFlagAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlagKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Environment = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: true
                    ),
                    Change = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ChangedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Before = table.Column<string>(type: "TEXT", nullable: true),
                    After = table.Column<string>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlagAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureFlagAuditEntries_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "FeatureFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1000,
                        nullable: true
                    ),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Variants = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultVariant = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Salt = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureFlags_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "FeatureFlagEnvironmentStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FeatureFlagId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    OffVariant = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Rules = table.Column<string>(type: "TEXT", nullable: false),
                    Fallthrough = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlagEnvironmentStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureFlagEnvironmentStates_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_FeatureFlagEnvironmentStates_FeatureFlags_FeatureFlagId",
                        column: x => x.FeatureFlagId,
                        principalTable: "FeatureFlags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagAuditEntries_ApplicationId_FlagKey_ChangedAt",
                table: "FeatureFlagAuditEntries",
                columns: new[] { "ApplicationId", "FlagKey", "ChangedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagEnvironmentStates_EnvironmentId",
                table: "FeatureFlagEnvironmentStates",
                column: "EnvironmentId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagEnvironmentStates_FeatureFlagId_EnvironmentId",
                table: "FeatureFlagEnvironmentStates",
                columns: new[] { "FeatureFlagId", "EnvironmentId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlags_ApplicationId_Key",
                table: "FeatureFlags",
                columns: new[] { "ApplicationId", "Key" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FeatureFlagAuditEntries");

            migrationBuilder.DropTable(name: "FeatureFlagEnvironmentStates");

            migrationBuilder.DropTable(name: "FeatureFlags");
        }
    }
}
