using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TrustPolicyId",
                table: "AccessTokens",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "TrustPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Repository = table.Column<string>(
                        type: "TEXT",
                        maxLength: 200,
                        nullable: false
                    ),
                    RefPattern = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    GitHubEnvironment = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    EnvironmentSlugs = table.Column<string>(type: "TEXT", nullable: true),
                    Scopes = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenLifetimeMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrustPolicies_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_TrustPolicyId",
                table: "AccessTokens",
                column: "TrustPolicyId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TrustPolicies_ApplicationId_Name",
                table: "TrustPolicies",
                columns: new[] { "ApplicationId", "Name" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TrustPolicies");

            migrationBuilder.DropIndex(
                name: "IX_AccessTokens_TrustPolicyId",
                table: "AccessTokens"
            );

            migrationBuilder.DropColumn(name: "TrustPolicyId", table: "AccessTokens");
        }
    }
}
