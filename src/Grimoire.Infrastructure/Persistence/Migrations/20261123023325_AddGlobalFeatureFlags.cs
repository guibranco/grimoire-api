using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeatureFlags_ApplicationId_Key",
                table: "FeatureFlags"
            );

            migrationBuilder.DropIndex(
                name: "IX_FeatureFlagEnvironmentStates_FeatureFlagId_EnvironmentId",
                table: "FeatureFlagEnvironmentStates"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ApplicationId",
                table: "FeatureFlags",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EnvironmentId",
                table: "FeatureFlagEnvironmentStates",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.AddColumn<string>(
                name: "EnvironmentSlug",
                table: "FeatureFlagEnvironmentStates",
                type: "TEXT",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ApplicationId",
                table: "FeatureFlagAuditEntries",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlags_ApplicationId_Key",
                table: "FeatureFlags",
                columns: new[] { "ApplicationId", "Key" },
                unique: true,
                filter: "\"ApplicationId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlags_Key",
                table: "FeatureFlags",
                column: "Key",
                unique: true,
                filter: "\"ApplicationId\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagEnvironmentStates_FeatureFlagId_EnvironmentId",
                table: "FeatureFlagEnvironmentStates",
                columns: new[] { "FeatureFlagId", "EnvironmentId" },
                unique: true,
                filter: "\"EnvironmentId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagEnvironmentStates_FeatureFlagId_EnvironmentSlug",
                table: "FeatureFlagEnvironmentStates",
                columns: new[] { "FeatureFlagId", "EnvironmentSlug" },
                unique: true,
                filter: "\"EnvironmentSlug\" IS NOT NULL"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FeatureFlagEnvironmentStates_OneEnvironment",
                table: "FeatureFlagEnvironmentStates",
                sql: "(\"EnvironmentId\" IS NULL) <> (\"EnvironmentSlug\" IS NULL)"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeatureFlags_ApplicationId_Key",
                table: "FeatureFlags"
            );

            migrationBuilder.DropIndex(name: "IX_FeatureFlags_Key", table: "FeatureFlags");

            migrationBuilder.DropIndex(
                name: "IX_FeatureFlagEnvironmentStates_FeatureFlagId_EnvironmentId",
                table: "FeatureFlagEnvironmentStates"
            );

            migrationBuilder.DropIndex(
                name: "IX_FeatureFlagEnvironmentStates_FeatureFlagId_EnvironmentSlug",
                table: "FeatureFlagEnvironmentStates"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_FeatureFlagEnvironmentStates_OneEnvironment",
                table: "FeatureFlagEnvironmentStates"
            );

            migrationBuilder.DropColumn(
                name: "EnvironmentSlug",
                table: "FeatureFlagEnvironmentStates"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ApplicationId",
                table: "FeatureFlags",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "EnvironmentId",
                table: "FeatureFlagEnvironmentStates",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "ApplicationId",
                table: "FeatureFlagAuditEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlags_ApplicationId_Key",
                table: "FeatureFlags",
                columns: new[] { "ApplicationId", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagEnvironmentStates_FeatureFlagId_EnvironmentId",
                table: "FeatureFlagEnvironmentStates",
                columns: new[] { "FeatureFlagId", "EnvironmentId" },
                unique: true
            );
        }
    }
}
