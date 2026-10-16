using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileSetupSkips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ProfileSkipClub",
                table: "Sys_AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ProfileSkipGroup",
                table: "Sys_AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ProfileSkipMe",
                table: "Sys_AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileSkipClub",
                table: "Sys_AppUsers");

            migrationBuilder.DropColumn(
                name: "ProfileSkipGroup",
                table: "Sys_AppUsers");

            migrationBuilder.DropColumn(
                name: "ProfileSkipMe",
                table: "Sys_AppUsers");
        }
    }
}
