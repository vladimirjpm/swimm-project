using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSwimmerPrivateHubGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrivateHubGroupId",
                table: "Swimmers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Swimmers_PrivateHubGroupId",
                table: "Swimmers",
                column: "PrivateHubGroupId",
                filter: "\"PrivateHubGroupId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Swimmers_HubGroups_PrivateHubGroupId",
                table: "Swimmers",
                column: "PrivateHubGroupId",
                principalTable: "HubGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Бэкфилл (Р71): локальный пловец (Origin = local — нет в федерации, заведён импортом
            // тренировок), который состоит РОВНО в одной группе, становится пловцом этой группы.
            // На 29.09.2026 это 18 человек Дельфин-мастерс (группа 17). Локальный пловец в двух
            // группах или ни в одной остаётся как был — владельца у него однозначно нет.
            migrationBuilder.Sql("""
                UPDATE "Swimmers" s
                SET "PrivateHubGroupId" = m."HubGroupId"
                FROM (
                    SELECT "SwimmerId", MIN("HubGroupId") AS "HubGroupId"
                    FROM "HubGroupMembers"
                    GROUP BY "SwimmerId"
                    HAVING COUNT(DISTINCT "HubGroupId") = 1
                ) m
                WHERE m."SwimmerId" = s."Id" AND s."Origin" = 'local';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Swimmers_HubGroups_PrivateHubGroupId",
                table: "Swimmers");

            migrationBuilder.DropIndex(
                name: "IX_Swimmers_PrivateHubGroupId",
                table: "Swimmers");

            migrationBuilder.DropColumn(
                name: "PrivateHubGroupId",
                table: "Swimmers");
        }
    }
}
