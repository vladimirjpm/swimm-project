using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.API.Migrations
{
    /// <inheritdoc />
    public partial class PrefixSysInternalAuthTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppUserRoles_AppRoles_RoleId",
                table: "AppUserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_AppUserRoles_AppUsers_UserId",
                table: "AppUserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_UserExternalLogins_AppUsers_UserId",
                table: "UserExternalLogins");

            migrationBuilder.DropForeignKey(
                name: "FK_UserLoginHistory_AppUsers_UserId",
                table: "UserLoginHistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserLoginHistory",
                table: "UserLoginHistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserExternalLogins",
                table: "UserExternalLogins");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppUserRoles",
                table: "AppUserRoles");

            migrationBuilder.RenameTable(
                name: "UserLoginHistory",
                newName: "Sys_UserLoginHistory");

            migrationBuilder.RenameTable(
                name: "UserExternalLogins",
                newName: "Sys_UserExternalLogins");

            migrationBuilder.RenameTable(
                name: "AppUserRoles",
                newName: "Sys_AppUserRoles");

            migrationBuilder.RenameIndex(
                name: "IX_UserLoginHistory_UserId",
                table: "Sys_UserLoginHistory",
                newName: "IX_Sys_UserLoginHistory_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_UserLoginHistory_LoginAt",
                table: "Sys_UserLoginHistory",
                newName: "IX_Sys_UserLoginHistory_LoginAt");

            migrationBuilder.RenameIndex(
                name: "IX_UserExternalLogins_UserId",
                table: "Sys_UserExternalLogins",
                newName: "IX_Sys_UserExternalLogins_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_UserExternalLogins_Provider_ProviderKey",
                table: "Sys_UserExternalLogins",
                newName: "IX_Sys_UserExternalLogins_Provider_ProviderKey");

            migrationBuilder.RenameIndex(
                name: "IX_AppUserRoles_RoleId",
                table: "Sys_AppUserRoles",
                newName: "IX_Sys_AppUserRoles_RoleId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Sys_UserLoginHistory",
                table: "Sys_UserLoginHistory",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Sys_UserExternalLogins",
                table: "Sys_UserExternalLogins",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Sys_AppUserRoles",
                table: "Sys_AppUserRoles",
                columns: new[] { "UserId", "RoleId" });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "PK_Sys_UserLoginHistory",
                table: "Sys_UserLoginHistory");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Sys_UserExternalLogins",
                table: "Sys_UserExternalLogins");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Sys_AppUserRoles",
                table: "Sys_AppUserRoles");

            migrationBuilder.RenameTable(
                name: "Sys_UserLoginHistory",
                newName: "UserLoginHistory");

            migrationBuilder.RenameTable(
                name: "Sys_UserExternalLogins",
                newName: "UserExternalLogins");

            migrationBuilder.RenameTable(
                name: "Sys_AppUserRoles",
                newName: "AppUserRoles");

            migrationBuilder.RenameIndex(
                name: "IX_Sys_UserLoginHistory_UserId",
                table: "UserLoginHistory",
                newName: "IX_UserLoginHistory_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_Sys_UserLoginHistory_LoginAt",
                table: "UserLoginHistory",
                newName: "IX_UserLoginHistory_LoginAt");

            migrationBuilder.RenameIndex(
                name: "IX_Sys_UserExternalLogins_UserId",
                table: "UserExternalLogins",
                newName: "IX_UserExternalLogins_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_Sys_UserExternalLogins_Provider_ProviderKey",
                table: "UserExternalLogins",
                newName: "IX_UserExternalLogins_Provider_ProviderKey");

            migrationBuilder.RenameIndex(
                name: "IX_Sys_AppUserRoles_RoleId",
                table: "AppUserRoles",
                newName: "IX_AppUserRoles_RoleId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserLoginHistory",
                table: "UserLoginHistory",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserExternalLogins",
                table: "UserExternalLogins",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppUserRoles",
                table: "AppUserRoles",
                columns: new[] { "UserId", "RoleId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AppUserRoles_AppRoles_RoleId",
                table: "AppUserRoles",
                column: "RoleId",
                principalTable: "AppRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AppUserRoles_AppUsers_UserId",
                table: "AppUserRoles",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserExternalLogins_AppUsers_UserId",
                table: "UserExternalLogins",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserLoginHistory_AppUsers_UserId",
                table: "UserLoginHistory",
                column: "UserId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
