using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserMediaLinkHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LinkCheckedAt",
                table: "Sys_UserMedia",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkError",
                table: "Sys_UserMedia",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LinkOk",
                table: "Sys_UserMedia",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LinkStatusCode",
                table: "Sys_UserMedia",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LinkCheckedAt",
                table: "Sys_UserMedia");

            migrationBuilder.DropColumn(
                name: "LinkError",
                table: "Sys_UserMedia");

            migrationBuilder.DropColumn(
                name: "LinkOk",
                table: "Sys_UserMedia");

            migrationBuilder.DropColumn(
                name: "LinkStatusCode",
                table: "Sys_UserMedia");
        }
    }
}
