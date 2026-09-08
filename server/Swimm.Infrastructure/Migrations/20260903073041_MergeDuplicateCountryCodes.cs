using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Данные-миграция (схему не трогает): alpha-2 коды стран сливаются в alpha-3.
    ///
    /// Это ВТОРАЯ такая склейка. Первая (<c>MergeCountryIlIntoIsr</c>, 2026-07-13) вычистила
    /// «IL» из справочника, но вход остался открыт — импорт заводил страну по сырому коду из
    /// файла, и к 2026-09-02 «IL» вернулся: 791 пловец и 3466 результатов смотрели на вторую
    /// запись Израиля, и рекорды им не находились вовсе (docs/data-integrity.md §14).
    /// Тогда склейку сделали руками в живой БД; эта миграция повторяет её ВОСПРОИЗВОДИМО —
    /// для любой копии базы (прод, восстановленный дамп, свежая машина).
    ///
    /// Вход закрыт отдельно — <c>CountryCodes.Normalize</c> в трёх find-or-create
    /// (импорт, соревнование, группа) плюс проверка реестра <c>countries.duplicate</c>.
    ///
    /// Идемпотентна и не зависит от Id: и коды, и канон резолвятся по справочнику.
    /// </summary>
    public partial class MergeDuplicateCountryCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Таблица синонимов — зеркало CountryCodes.Aliases (Swimm.Application/Constants).
            // Держим её короткой по той же причине: коды World Aquatics расходятся с ISO
            // alpha-3 (GER≠DEU, SUI≠CHE), и «на всякий случай» здесь молча подменяло бы страну.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    alias    record;
    dup_id   int;
    canon_id int;
BEGIN
    FOR alias IN
        SELECT * FROM (VALUES ('IL', 'ISR', 'Israel')) AS t(alpha2, alpha3, name)
    LOOP
        SELECT ""Id"" INTO dup_id FROM ""Countries"" WHERE upper(""CountryCode"") = alias.alpha2;
        CONTINUE WHEN dup_id IS NULL;

        SELECT ""Id"" INTO canon_id FROM ""Countries"" WHERE upper(""CountryCode"") = alias.alpha3;

        IF canon_id IS NULL THEN
            -- Канона нет: переименовываем саму запись, иначе склеивать не во что.
            UPDATE ""Countries""
               SET ""CountryCode"" = alias.alpha3,
                   ""CountryName"" = CASE
                       WHEN ""CountryName"" IS NULL
                            OR btrim(""CountryName"") = ''
                            OR upper(""CountryName"") = alias.alpha2
                       THEN alias.name ELSE ""CountryName"" END
             WHERE ""Id"" = dup_id;
            CONTINUE;
        END IF;

        -- Все пять FK на Countries (Competitions в первой миграции не было: тогда страна
        -- соревнования жила строкой, а не ссылкой).
        UPDATE ""Swimmers""     SET ""CountryId"" = canon_id WHERE ""CountryId"" = dup_id;
        UPDATE ""Clubs""        SET ""CountryId"" = canon_id WHERE ""CountryId"" = dup_id;
        UPDATE ""Results""      SET ""CountryId"" = canon_id WHERE ""CountryId"" = dup_id;
        UPDATE ""HubGroups""    SET ""CountryId"" = canon_id WHERE ""CountryId"" = dup_id;
        UPDATE ""Competitions"" SET ""CountryId"" = canon_id WHERE ""CountryId"" = dup_id;

        DELETE FROM ""Countries"" WHERE ""Id"" = dup_id;
    END LOOP;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Слияние необратимо: какие именно строки смотрели на alpha-2, после склейки
            // не восстановить. Down — no-op, откат только из бэкапа (как у MergeCountryIlIntoIsr).
        }
    }
}
