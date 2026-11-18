using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserContentPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanAddPhotos",
                table: "Sys_AppUsers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanAddVideos",
                table: "Sys_AppUsers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanPublish",
                table: "Sys_AppUsers",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanAddPhotos",
                table: "Sys_AppUsers");

            migrationBuilder.DropColumn(
                name: "CanAddVideos",
                table: "Sys_AppUsers");

            migrationBuilder.DropColumn(
                name: "CanPublish",
                table: "Sys_AppUsers");
        }
    }
}
