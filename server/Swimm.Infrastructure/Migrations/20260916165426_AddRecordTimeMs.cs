using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Время рекорда в миллисекундах — вычисляемой колонкой (этап 11.2.1 плана Фазы 11).
    ///
    /// Зачем: строкой время не сортируется и не сравнивается — «01:02.36» лексикографически
    /// меньше «21.08», хотя вдвое дольше. Рейтингу рекордов нужно упорядочивать в БД.
    ///
    /// Почему GENERATED, а не обычная колонка: рекорды пишут ЧЕТЫРЕ разных места — дифф
    /// импорта, две формы админки и сидер, — плюс psql и восстановление дампа. Любая ручная
    /// синхронизация двух колонок рано или поздно разъезжается и делает это молча; вычисляемая
    /// не может по построению. Приложение её не пишет: попытка присвоить кончится ошибкой БД.
    ///
    /// Функция обязана быть IMMUTABLE — иначе Postgres не возьмёт её в GENERATED. Она чистая:
    /// разбор строки, никаких обращений к таблицам и настройкам.
    ///
    /// ⚠ Формат должен совпадать с <c>SwimTime.ParseToMs</c> (Swimm.Application/Mapping):
    /// <c>[[ч:]м:]сс.дроб</c>, дробная часть дополняется нулями до миллисекунд и обрезается до
    /// трёх знаков. Расхождение двух разборов ловит тест <c>SwimTimeSqlContractTests</c>.
    /// Неразбираемая строка даёт NULL, а не ноль: «время не распознано» и «ноль миллисекунд» —
    /// разные вещи, и ноль встал бы первым в любом рейтинге.
    /// </summary>
    public partial class AddRecordTimeMs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION swim_time_ms(t text) RETURNS int
                LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE AS $$
                  SELECT CASE
                    -- ч:мм:сс.дроб — длинные дистанции у столетних («01:14:08.7»)
                    WHEN t ~ '^\d+:\d+:\d+\.\d+$' THEN
                      (split_part(t, ':', 1)::int * 3600
                       + split_part(t, ':', 2)::int * 60
                       + split_part(split_part(t, ':', 3), '.', 1)::int) * 1000
                      + rpad(split_part(t, '.', 2), 3, '0')::int
                    -- мм:сс.дроб
                    WHEN t ~ '^\d+:\d+\.\d+$' THEN
                      (split_part(t, ':', 1)::int * 60
                       + split_part(split_part(t, ':', 2), '.', 1)::int) * 1000
                      + rpad(split_part(t, '.', 2), 3, '0')::int
                    -- сс.дроб
                    WHEN t ~ '^\d+\.\d+$' THEN
                      split_part(t, '.', 1)::int * 1000
                      + rpad(split_part(t, '.', 2), 3, '0')::int
                    ELSE NULL
                  END
                $$;
            ");

            migrationBuilder.AddColumn<int>(
                name: "TimeMs",
                table: "Records",
                type: "integer",
                nullable: true,
                computedColumnSql: "swim_time_ms(\"Time\")",
                stored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Сначала колонка, потом функция: пока колонка на неё опирается, DROP FUNCTION
            // не пройдёт.
            migrationBuilder.DropColumn(
                name: "TimeMs",
                table: "Records");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS swim_time_ms(text);");
        }
    }
}
