using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHubGroupMediaMembersLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ResultId",
                table: "Sys_HubGroupMedia",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SwimmerId",
                table: "Sys_HubGroupMedia",
                type: "integer",
                nullable: true);

            // Бэкфилл существующих записей (историческая публичная галерея) в 'public' —
            // иначе check-constraint CK_HubGroupMedia_Visibility упал бы на пустой строке.
            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "Sys_HubGroupMedia",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "public");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupMedia_ResultId",
                table: "Sys_HubGroupMedia",
                column: "ResultId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupMedia_SwimmerId",
                table: "Sys_HubGroupMedia",
                column: "SwimmerId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupMedia_AnchorMembersOnly",
                table: "Sys_HubGroupMedia",
                sql: "(\"SwimmerId\" IS NULL AND \"ResultId\" IS NULL) OR \"Visibility\" = 'members'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupMedia_Visibility",
                table: "Sys_HubGroupMedia",
                sql: "\"Visibility\" IN ('public', 'members')");

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_HubGroupMedia_Results_ResultId",
                table: "Sys_HubGroupMedia",
                column: "ResultId",
                principalTable: "Results",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_HubGroupMedia_Swimmers_SwimmerId",
                table: "Sys_HubGroupMedia",
                column: "SwimmerId",
                principalTable: "Swimmers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sys_HubGroupMedia_Results_ResultId",
                table: "Sys_HubGroupMedia");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_HubGroupMedia_Swimmers_SwimmerId",
                table: "Sys_HubGroupMedia");

            migrationBuilder.DropIndex(
                name: "IX_Sys_HubGroupMedia_ResultId",
                table: "Sys_HubGroupMedia");

            migrationBuilder.DropIndex(
                name: "IX_Sys_HubGroupMedia_SwimmerId",
                table: "Sys_HubGroupMedia");

            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupMedia_AnchorMembersOnly",
                table: "Sys_HubGroupMedia");

            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupMedia_Visibility",
                table: "Sys_HubGroupMedia");

            migrationBuilder.DropColumn(
                name: "ResultId",
                table: "Sys_HubGroupMedia");

            migrationBuilder.DropColumn(
                name: "SwimmerId",
                table: "Sys_HubGroupMedia");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Sys_HubGroupMedia");
        }
    }
}
