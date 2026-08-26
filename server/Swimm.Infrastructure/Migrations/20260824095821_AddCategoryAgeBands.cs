using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryAgeBands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxAge",
                table: "Categories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinAge",
                table: "Categories",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "MaxAge", "MinAge" },
                values: new object[] { 17, 14 });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "MaxAge", "MinAge" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "MaxAge", "MinAge" },
                values: new object[] { 11, 8 });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "MaxAge", "MinAge" },
                values: new object[] { 14, 11 });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "MaxAge", "MinAge" },
                values: new object[] { null, 17 });

            // Страховка: UpdateData выше адресует строки по Id из сида, а в чужой инсталляции
            // категории могли завестись с другими Id (Maccabiah и Age 8-99 — как раз такие).
            // Полосы — свойство КЛЮЧА, поэтому дозаполняем по нему; уже проставленное не трогаем.
            migrationBuilder.Sql("""
                UPDATE "Categories" SET "MinAge" = v."MinAge", "MaxAge" = v."MaxAge"
                FROM (VALUES
                    ('results-kids-team',      8,  11),
                    ('results-youth-team',     11, 14),
                    ('results-junior-results', 14, 17),
                    ('results-main',           17, NULL)
                ) AS v("Key", "MinAge", "MaxAge")
                WHERE "Categories"."Key" = v."Key"
                  AND "Categories"."MinAge" IS NULL AND "Categories"."MaxAge" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxAge",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "MinAge",
                table: "Categories");
        }
    }
}
