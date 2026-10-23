using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountDeletionSetNullUserRefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sys_HubGroupAdmins_Sys_AppUsers_GrantedByUserId",
                table: "Sys_HubGroupAdmins");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_HubGroupClubRequests_Sys_AppUsers_DecidedByUserId",
                table: "Sys_HubGroupClubRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_HubGroupUserMembers_Sys_AppUsers_AddedByUserId",
                table: "Sys_HubGroupUserMembers");

            migrationBuilder.AlterColumn<int>(
                name: "GrantedByUserId",
                table: "Sys_HubGroupAdmins",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_HubGroupAdmins_Sys_AppUsers_GrantedByUserId",
                table: "Sys_HubGroupAdmins",
                column: "GrantedByUserId",
                principalTable: "Sys_AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_HubGroupClubRequests_Sys_AppUsers_DecidedByUserId",
                table: "Sys_HubGroupClubRequests",
                column: "DecidedByUserId",
                principalTable: "Sys_AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_HubGroupUserMembers_Sys_AppUsers_AddedByUserId",
                table: "Sys_HubGroupUserMembers",
                column: "AddedByUserId",
                principalTable: "Sys_AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sys_HubGroupAdmins_Sys_AppUsers_GrantedByUserId",
                table: "Sys_HubGroupAdmins");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_HubGroupClubRequests_Sys_AppUsers_DecidedByUserId",
                table: "Sys_HubGroupClubRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_HubGroupUserMembers_Sys_AppUsers_AddedByUserId",
                table: "Sys_HubGroupUserMembers");

            migrationBuilder.AlterColumn<int>(
                name: "GrantedByUserId",
                table: "Sys_HubGroupAdmins",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_HubGroupAdmins_Sys_AppUsers_GrantedByUserId",
                table: "Sys_HubGroupAdmins",
                column: "GrantedByUserId",
                principalTable: "Sys_AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_HubGroupClubRequests_Sys_AppUsers_DecidedByUserId",
                table: "Sys_HubGroupClubRequests",
                column: "DecidedByUserId",
                principalTable: "Sys_AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_HubGroupUserMembers_Sys_AppUsers_AddedByUserId",
                table: "Sys_HubGroupUserMembers",
                column: "AddedByUserId",
                principalTable: "Sys_AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
