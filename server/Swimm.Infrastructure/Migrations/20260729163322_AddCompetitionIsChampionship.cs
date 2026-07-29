using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionIsChampionship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsChampionship",
                table: "Competitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Бэкфилл по названию — той же эвристикой, что раньше считалась на лету
            // («אליפות» И «ישראל», между ними бывает спонсор; либо championship + israel).
            // Дальше флаг живёт руками: правки на Edit переимпорт/новая миграция не затирают.
            migrationBuilder.Sql("""
                UPDATE "Competitions"
                SET "IsChampionship" = true
                WHERE ("Name" LIKE '%אליפות%' AND "Name" LIKE '%ישראל%')
                   OR ("Name" ILIKE '%championship%' AND "Name" ILIKE '%israel%');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsChampionship",
                table: "Competitions");
        }
    }
}
