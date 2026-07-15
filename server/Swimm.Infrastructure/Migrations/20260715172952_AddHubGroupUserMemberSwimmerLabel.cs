using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupUserMemberSwimmerLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Sys_HubGroupUserMembers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SwimmerId",
                table: "Sys_HubGroupUserMembers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupUserMembers_SwimmerId",
                table: "Sys_HubGroupUserMembers",
                column: "SwimmerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_HubGroupUserMembers_Swimmers_SwimmerId",
                table: "Sys_HubGroupUserMembers",
                column: "SwimmerId",
                principalTable: "Swimmers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sys_HubGroupUserMembers_Swimmers_SwimmerId",
                table: "Sys_HubGroupUserMembers");

            migrationBuilder.DropIndex(
                name: "IX_Sys_HubGroupUserMembers_SwimmerId",
                table: "Sys_HubGroupUserMembers");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "Sys_HubGroupUserMembers");

            migrationBuilder.DropColumn(
                name: "SwimmerId",
                table: "Sys_HubGroupUserMembers");
        }
    }
}
