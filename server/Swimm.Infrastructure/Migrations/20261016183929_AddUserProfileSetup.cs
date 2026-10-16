using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ProfileRoleCoach",
                table: "Sys_AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ProfileRoleParent",
                table: "Sys_AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ProfileRoleSwimmer",
                table: "Sys_AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ProfileRoleWatcher",
                table: "Sys_AppUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProfileRolesAnsweredAt",
                table: "Sys_AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProfileSetupDismissedAt",
                table: "Sys_AppUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileRoleCoach",
                table: "Sys_AppUsers");

            migrationBuilder.DropColumn(
                name: "ProfileRoleParent",
                table: "Sys_AppUsers");

            migrationBuilder.DropColumn(
                name: "ProfileRoleSwimmer",
                table: "Sys_AppUsers");

            migrationBuilder.DropColumn(
                name: "ProfileRoleWatcher",
                table: "Sys_AppUsers");

            migrationBuilder.DropColumn(
                name: "ProfileRolesAnsweredAt",
                table: "Sys_AppUsers");

            migrationBuilder.DropColumn(
                name: "ProfileSetupDismissedAt",
                table: "Sys_AppUsers");
        }
    }
}
