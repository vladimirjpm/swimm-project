using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Версии клубных правил переведены на формат «&lt;очки за 1 место&gt;pt.&lt;мест в шкале&gt;pl.&lt;период&gt;»
    /// (напр. 40pt.24pl.2026.01): в DDL привязки на /Admin/Competitions видно суть шкалы, а не
    /// только дату. Правила #1/#2 живут в сиде (HasData) — их правит UpdateData; #3/#4 заведены
    /// в админке, поэтому обновляются SQL по старому значению (no-op там, где их нет).
    /// </summary>
    public partial class PointRuleVersionsPtPl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "PointRulesClubs",
                keyColumn: "Id",
                keyValue: 1,
                column: "Version",
                value: "30pt.24pl.2025.01");

            migrationBuilder.UpdateData(
                table: "PointRulesClubs",
                keyColumn: "Id",
                keyValue: 2,
                column: "Version",
                value: "12pt.12pl.2025.01");

            migrationBuilder.Sql(
                """
                UPDATE "PointRulesClubs" SET "Version" = '40pt.24pl.2026.01' WHERE "Version" = '2026.01-youth-11-14';
                UPDATE "PointRulesClubs" SET "Version" = '25pt.20pl.2026.01' WHERE "Version" = '2026.01-adults';
                UPDATE "PointRulesSwimmers" SET "Version" = '13.8.5.3.2.1-age-2026.01' WHERE "Version" = '2026.01-age';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "PointRulesClubs" SET "Version" = '2026.01-youth-11-14' WHERE "Version" = '40pt.24pl.2026.01';
                UPDATE "PointRulesClubs" SET "Version" = '2026.01-adults' WHERE "Version" = '25pt.20pl.2026.01';
                UPDATE "PointRulesSwimmers" SET "Version" = '2026.01-age' WHERE "Version" = '13.8.5.3.2.1-age-2026.01';
                """);

            migrationBuilder.UpdateData(
                table: "PointRulesClubs",
                keyColumn: "Id",
                keyValue: 1,
                column: "Version",
                value: "2025.01");

            migrationBuilder.UpdateData(
                table: "PointRulesClubs",
                keyColumn: "Id",
                keyValue: 2,
                column: "Version",
                value: "2025.01-masters");
        }
    }
}
