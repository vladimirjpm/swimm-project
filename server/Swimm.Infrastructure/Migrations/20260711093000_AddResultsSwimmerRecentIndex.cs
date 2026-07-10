using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Индекс под «последние заплывы ростера» на странице группы:
    /// WHERE SwimmerId IN (…) ORDER BY CompetitionDate DESC, Id DESC LIMIT N.
    /// Без него планировщик пятится по IX_Results_CompetitionDate через ВСЮ таблицу,
    /// отфильтровывая чужих пловцов (на 3 млн строк — секунды на каждый запрос группы).
    /// IX_Results_SwimmerId намеренно не удаляем: на него опираются другие запросы,
    /// а составной индекс его лишь дублирует по ведущей колонке.
    /// </summary>
    public partial class AddResultsSwimmerRecentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Results_SwimmerId_CompetitionDate_Id",
                table: "Results",
                columns: new[] { "SwimmerId", "CompetitionDate", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Results_SwimmerId_CompetitionDate_Id",
                table: "Results");
        }
    }
}
