using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Ступени категорий приведены к подписям + названия на иврите.
    ///
    /// Ключи ПЕРЕЕЗЖАЮТ по кругу (данные остаются в своей возрастной полосе):
    ///   #3 results-youth-team     (Kids)    → results-kids-team      Kids    ילדים
    ///   #4 results-junior-results (Youth)   → results-youth-team     Young   צעירים
    ///   #1 results-main           (Juniors) → results-junior-results Juniors נוער
    ///   + новая #7                            results-main           Adults  בוגרים
    ///
    /// ⚠ Порядок UpdateData важен: на "Categories"."Key" уникальный индекс, он проверяется
    /// пооператорно, поэтому ключ надо освободить ДО того, как его займёт следующая строка.
    /// Автогенерация EF выдаёт порядок по Id (1,2,3,4) и падает на коллизии — не пересоздавай
    /// эту миграцию скаффолдингом вслепую.
    /// </summary>
    public partial class CategoryLadderRenameAndHebrew : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameHe",
                table: "Categories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // 1) #3 уходит на свободный results-kids-team.
            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DisplayOrder", "Key", "NameHe" },
                values: new object[] { 1, "results-kids-team", "ילדים" });

            // 2) results-youth-team освободился — его занимает #4 (11–14, теперь «Young»).
            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "DisplayOrder", "Key", "Name", "NameHe" },
                values: new object[] { 2, "results-youth-team", "Young", "צעירים" });

            // 3) results-junior-results освободился — его занимает #1 («Juniors», נוער).
            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DisplayOrder", "Key", "NameHe" },
                values: new object[] { 3, "results-junior-results", "נוער" });

            // 4) results-main освободился — заводим под ним новую ступень Adults (בוגרים).
            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Badge", "DisplayOrder", "Key", "Name", "NameHe" },
                values: new object[] { 7, "A", 4, "results-main", "Adults", "בוגרים" });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "DisplayOrder", "NameHe" },
                values: new object[] { 5, "מסטרס" });

            // Кастомные категории в сид не входят (их заводит админ) — иврит проставляем по Key,
            // если такая строка в этой БД есть.
            migrationBuilder.Sql(
                """UPDATE "Categories" SET "NameHe" = 'מכביה' WHERE "Key" = 'result-maccabiah';""");

            // Id новой ступени задан явно (7) — двигаем последовательность за максимум,
            // иначе следующая созданная в админке категория попадёт на занятый Id.
            migrationBuilder.Sql(
                """SELECT setval(pg_get_serial_sequence('"Categories"', 'Id'), (SELECT MAX("Id") FROM "Categories"));""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Обратная ротация — тоже строго по порядку освобождения ключей.
            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DisplayOrder", "Key" },
                values: new object[] { 1, "results-main" });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "DisplayOrder", "Key", "Name" },
                values: new object[] { 4, "results-junior-results", "Youth" });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DisplayOrder", "Key" },
                values: new object[] { 3, "results-youth-team" });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 2,
                column: "DisplayOrder",
                value: 2);

            migrationBuilder.DropColumn(
                name: "NameHe",
                table: "Categories");
        }
    }
}
