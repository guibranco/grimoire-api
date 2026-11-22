using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsumerActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsumerActivities",
                columns: table => new
                {
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastUserAgent = table.Column<string>(
                        type: "TEXT",
                        maxLength: 256,
                        nullable: true
                    ),
                    LastIpAddress = table.Column<string>(
                        type: "TEXT",
                        maxLength: 45,
                        nullable: true
                    ),
                    RequestCount = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumerActivities", x => new
                    {
                        x.ApplicationId,
                        x.EnvironmentId,
                    });
                    table.ForeignKey(
                        name: "FK_ConsumerActivities_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_ConsumerActivities_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ConsumerActivities_EnvironmentId",
                table: "ConsumerActivities",
                column: "EnvironmentId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ConsumerActivities");
        }
    }
}
