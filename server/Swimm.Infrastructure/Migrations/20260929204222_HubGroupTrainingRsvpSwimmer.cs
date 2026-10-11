using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HubGroupTrainingRsvpSwimmer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sys_HubGroupTrainingRsvps_HubGroupId_SessionDate_SessionSta~",
                table: "Sys_HubGroupTrainingRsvps");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Sys_HubGroupTrainingRsvps",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "SwimmerId",
                table: "Sys_HubGroupTrainingRsvps",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupTrainingRsvps_Session_SwimmerId",
                table: "Sys_HubGroupTrainingRsvps",
                columns: new[] { "HubGroupId", "SessionDate", "SessionStart", "SwimmerId" },
                unique: true,
                filter: "\"SwimmerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupTrainingRsvps_Session_UserId",
                table: "Sys_HubGroupTrainingRsvps",
                columns: new[] { "HubGroupId", "SessionDate", "SessionStart", "UserId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupTrainingRsvps_SwimmerId",
                table: "Sys_HubGroupTrainingRsvps",
                column: "SwimmerId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_HubGroupTrainingRsvps_Subject",
                table: "Sys_HubGroupTrainingRsvps",
                sql: "(\"UserId\" IS NULL) <> (\"SwimmerId\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_HubGroupTrainingRsvps_Swimmers_SwimmerId",
                table: "Sys_HubGroupTrainingRsvps",
                column: "SwimmerId",
                principalTable: "Swimmers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Ответы за пловцов группы (Р73) без аккаунта вернуть в UserId NOT NULL нельзя.
            migrationBuilder.Sql(@"DELETE FROM ""Sys_HubGroupTrainingRsvps"" WHERE ""UserId"" IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_HubGroupTrainingRsvps_Swimmers_SwimmerId",
                table: "Sys_HubGroupTrainingRsvps");

            migrationBuilder.DropIndex(
                name: "IX_Sys_HubGroupTrainingRsvps_Session_SwimmerId",
                table: "Sys_HubGroupTrainingRsvps");

            migrationBuilder.DropIndex(
                name: "IX_Sys_HubGroupTrainingRsvps_Session_UserId",
                table: "Sys_HubGroupTrainingRsvps");

            migrationBuilder.DropIndex(
                name: "IX_Sys_HubGroupTrainingRsvps_SwimmerId",
                table: "Sys_HubGroupTrainingRsvps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_HubGroupTrainingRsvps_Subject",
                table: "Sys_HubGroupTrainingRsvps");

            migrationBuilder.DropColumn(
                name: "SwimmerId",
                table: "Sys_HubGroupTrainingRsvps");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Sys_HubGroupTrainingRsvps",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupTrainingRsvps_HubGroupId_SessionDate_SessionSta~",
                table: "Sys_HubGroupTrainingRsvps",
                columns: new[] { "HubGroupId", "SessionDate", "SessionStart", "UserId" },
                unique: true);
        }
    }
}
