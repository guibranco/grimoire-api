using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationValueType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ValueType",
                table: "GlobalConfigurationEntries",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "String"
            );

            migrationBuilder.AddColumn<string>(
                name: "ValueType",
                table: "ConfigurationEntries",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "String"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ValueType", table: "GlobalConfigurationEntries");

            migrationBuilder.DropColumn(name: "ValueType", table: "ConfigurationEntries");
        }
    }
}
