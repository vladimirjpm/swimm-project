using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.API.Migrations
{
    /// <inheritdoc />
    public partial class PrefixSysUsersAndRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppUsers_Swimmers_SwimmerId",
                table: "AppUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_AppUserRoles_AppRoles_RoleId",
                table: "Sys_AppUserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_AppUserRoles_AppUsers_UserId",
                table: "Sys_AppUserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_UserExternalLogins_AppUsers_UserId",
                table: "Sys_UserExternalLogins");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_UserLoginHistory_AppUsers_UserId",
                table: "Sys_UserLoginHistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppUsers",
                table: "AppUsers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppRoles",
                table: "AppRoles");

            migrationBuilder.RenameTable(
                name: "AppUsers",
                newName: "Sys_AppUsers");

            migrationBuilder.RenameTable(
                name: "AppRoles",
                newName: "Sys_AppRoles");

            migrationBuilder.RenameIndex(
                name: "IX_AppUsers_SwimmerId",
                table: "Sys_AppUsers",
                newName: "IX_Sys_AppUsers_SwimmerId");

            migrationBuilder.RenameIndex(
                name: "IX_AppUsers_Email",
                table: "Sys_AppUsers",
                newName: "IX_Sys_AppUsers_Email");

            migrationBuilder.RenameIndex(
                name: "IX_AppRoles_Name",
                table: "Sys_AppRoles",
                newName: "IX_Sys_AppRoles_Name");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Sys_AppUsers",
                table: "Sys_AppUsers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Sys_AppRoles",
                table: "Sys_AppRoles",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_AppUserRoles_Sys_AppRoles_RoleId",
                table: "Sys_AppUserRoles",
                column: "RoleId",
                principalTable: "Sys_AppRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_AppUserRoles_Sys_AppUsers_UserId",
                table: "Sys_AppUserRoles",
                column: "UserId",
                principalTable: "Sys_AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_AppUsers_Swimmers_SwimmerId",
                table: "Sys_AppUsers",
                column: "SwimmerId",
                principalTable: "Swimmers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_UserExternalLogins_Sys_AppUsers_UserId",
                table: "Sys_UserExternalLogins",
                column: "UserId",
                principalTable: "Sys_AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_UserLoginHistory_Sys_AppUsers_UserId",
                table: "Sys_UserLoginHistory",
                column: "UserId",
                principalTable: "Sys_AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sys_AppUserRoles_Sys_AppRoles_RoleId",
                table: "Sys_AppUserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_AppUserRoles_Sys_AppUsers_UserId",
                table: "Sys_AppUserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_AppUsers_Swimmers_SwimmerId",
                table: "Sys_AppUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_UserExternalLogins_Sys_AppUsers_UserId",
                table: "Sys_UserExternalLogins");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_UserLoginHistory_Sys_AppUsers_UserId",
                table: "Sys_UserLoginHistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Sys_AppUsers",
                table: "Sys_AppUsers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Sys_AppRoles",
                table: "Sys_AppRoles");

            migrationBuilder.RenameTable(
                name: "Sys_AppUsers",
                newName: "AppUsers");

            migrationBuilder.RenameTable(
                name: "Sys_AppRoles",
                newName: "AppRoles");

            migrationBuilder.RenameIndex(
                name: "IX_Sys_AppUsers_SwimmerId",
                table: "AppUsers",
                newName: "IX_AppUsers_SwimmerId");

            migrationBuilder.RenameIndex(
                name: "IX_Sys_AppUsers_Email",
                table: "AppUsers",
                newName: "IX_AppUsers_Email");

            migrationBuilder.RenameIndex(
                name: "IX_Sys_AppRoles_Name",
                table: "AppRoles",
                newName: "IX_AppRoles_Name");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppUsers",
                table: "AppUsers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppRoles",
                table: "AppRoles",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AppUsers_Swimmers_SwimmerId",
                table: "AppUsers",
                column: "SwimmerId",
                principalTable: "Swimmers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_AppUserRoles_AppRoles_RoleId",
                table: "Sys_AppUserRoles",
                column: "RoleId",
                principalTable: "AppRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_AppUserRoles_AppUsers_UserId",
                table: "Sys_AppUserRoles",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_UserExternalLogins_AppUsers_UserId",
                table: "Sys_UserExternalLogins",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_UserLoginHistory_AppUsers_UserId",
                table: "Sys_UserLoginHistory",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
