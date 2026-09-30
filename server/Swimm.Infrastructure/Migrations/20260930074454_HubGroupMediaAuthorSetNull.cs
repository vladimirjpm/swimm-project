using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HubGroupMediaAuthorSetNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sys_HubGroupMedia_Sys_AppUsers_CreatedByUserId",
                table: "Sys_HubGroupMedia");

            migrationBuilder.AlterColumn<int>(
                name: "CreatedByUserId",
                table: "Sys_HubGroupMedia",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_HubGroupMedia_Sys_AppUsers_CreatedByUserId",
                table: "Sys_HubGroupMedia",
                column: "CreatedByUserId",
                principalTable: "Sys_AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Медиа без автора (аккаунт удалён) в NOT NULL + RESTRICT вернуть нельзя.
            migrationBuilder.Sql(@"DELETE FROM ""Sys_HubGroupMedia"" WHERE ""CreatedByUserId"" IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Sys_HubGroupMedia_Sys_AppUsers_CreatedByUserId",
                table: "Sys_HubGroupMedia");

            migrationBuilder.AlterColumn<int>(
                name: "CreatedByUserId",
                table: "Sys_HubGroupMedia",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_HubGroupMedia_Sys_AppUsers_CreatedByUserId",
                table: "Sys_HubGroupMedia",
                column: "CreatedByUserId",
                principalTable: "Sys_AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
