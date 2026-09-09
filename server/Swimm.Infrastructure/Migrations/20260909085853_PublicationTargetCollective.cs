using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PublicationTargetCollective : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sys_UserMediaPublications_UserMediaId_HubGroupId",
                table: "Sys_UserMediaPublications");

            migrationBuilder.AlterColumn<int>(
                name: "HubGroupId",
                table: "Sys_UserMediaPublications",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "ClubId",
                table: "Sys_UserMediaPublications",
                type: "integer",
                nullable: true);

            // ⚠ Бэкфилл, а не пустая строка (EF предлагает "" по умолчанию): все существующие
            // публикации — групповые, а ниже в этой же миграции встаёт CHECK
            // TargetType IN ('group','club'), и на "" она бы упала прямо при накатывании.
            migrationBuilder.AddColumn<string>(
                name: "TargetType",
                table: "Sys_UserMediaPublications",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "group");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserMediaPublications_ClubId",
                table: "Sys_UserMediaPublications",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserMediaPublications_UserMediaId_ClubId",
                table: "Sys_UserMediaPublications",
                columns: new[] { "UserMediaId", "ClubId" },
                unique: true,
                filter: "\"ClubId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserMediaPublications_UserMediaId_HubGroupId",
                table: "Sys_UserMediaPublications",
                columns: new[] { "UserMediaId", "HubGroupId" },
                unique: true,
                filter: "\"HubGroupId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserMediaPublications_Target",
                table: "Sys_UserMediaPublications",
                sql: "\"TargetType\" IN ('group', 'club')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserMediaPublications_TargetShape",
                table: "Sys_UserMediaPublications",
                sql: "(\"TargetType\" = 'group' AND \"HubGroupId\" IS NOT NULL AND \"ClubId\" IS NULL)\n                  OR (\"TargetType\" = 'club' AND \"ClubId\" IS NOT NULL AND \"HubGroupId\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Sys_UserMediaPublications_Clubs_ClubId",
                table: "Sys_UserMediaPublications",
                column: "ClubId",
                principalTable: "Clubs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sys_UserMediaPublications_Clubs_ClubId",
                table: "Sys_UserMediaPublications");

            migrationBuilder.DropIndex(
                name: "IX_Sys_UserMediaPublications_ClubId",
                table: "Sys_UserMediaPublications");

            migrationBuilder.DropIndex(
                name: "IX_Sys_UserMediaPublications_UserMediaId_ClubId",
                table: "Sys_UserMediaPublications");

            migrationBuilder.DropIndex(
                name: "IX_Sys_UserMediaPublications_UserMediaId_HubGroupId",
                table: "Sys_UserMediaPublications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserMediaPublications_Target",
                table: "Sys_UserMediaPublications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserMediaPublications_TargetShape",
                table: "Sys_UserMediaPublications");

            migrationBuilder.DropColumn(
                name: "ClubId",
                table: "Sys_UserMediaPublications");

            migrationBuilder.DropColumn(
                name: "TargetType",
                table: "Sys_UserMediaPublications");

            migrationBuilder.AlterColumn<int>(
                name: "HubGroupId",
                table: "Sys_UserMediaPublications",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserMediaPublications_UserMediaId_HubGroupId",
                table: "Sys_UserMediaPublications",
                columns: new[] { "UserMediaId", "HubGroupId" },
                unique: true);
        }
    }
}
