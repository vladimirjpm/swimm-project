using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupIsTest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTest",
                table: "HubGroups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroups_TestNotOfficial",
                table: "HubGroups",
                sql: "NOT (\"IsTest\" AND \"IsOfficial\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroups_TestNotOfficial",
                table: "HubGroups");

            migrationBuilder.DropColumn(
                name: "IsTest",
                table: "HubGroups");
        }
    }
}
