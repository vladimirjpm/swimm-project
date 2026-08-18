using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionEventOrgCompId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrgCompId",
                table: "CompetitionEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionEvents_OrgCompId",
                table: "CompetitionEvents",
                column: "OrgCompId");

            // Бэкфилл: штамп сайта до сих пор ставился только на ПЕРВЫЙ день многодневки
            // (Competition.OrgCompId — альтернативный ключ, уникальный). Поднимаем его на
            // событие, чтобы переимпорт находил по compID все дни, а не один.
            // MIN() — на случай, если внутри события штампов несколько: на сайте одному
            // протоколу могут соответствовать две записи (6621 и 6622 → тот же файл).
            migrationBuilder.Sql("""
                UPDATE "CompetitionEvents" e
                SET "OrgCompId" = sub.org
                FROM (
                    SELECT c."EventId" AS event_id, MIN(c."OrgCompId") AS org
                    FROM "Competitions" c
                    WHERE c."EventId" IS NOT NULL AND c."OrgCompId" IS NOT NULL
                    GROUP BY c."EventId"
                ) sub
                WHERE e."Id" = sub.event_id AND e."OrgCompId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompetitionEvents_OrgCompId",
                table: "CompetitionEvents");

            migrationBuilder.DropColumn(
                name: "OrgCompId",
                table: "CompetitionEvents");
        }
    }
}
