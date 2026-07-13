using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MergeCountryIlIntoIsr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Данные-миграция (без изменения схемы). Израиль в легаси-данных существует под двумя
            // кодами: alpha-2 "IL" (из старых JSON-импортов) и alpha-3 "ISR" (текущий стандарт,
            // см. docs/ARCHITECTURE.md). Сливаем IL в ISR: перецеливаем все FK, нормализуем
            // строковый Competitions.Country ("IL"/"il" → "ISR") и удаляем строку-дубль.
            // Идемпотентно и не зависит от конкретных Id (резолвим по коду).
            migrationBuilder.Sql(@"
DO $$
DECLARE il_id int; isr_id int;
BEGIN
    SELECT ""Id"" INTO il_id  FROM ""Countries"" WHERE ""CountryCode"" = 'IL';
    SELECT ""Id"" INTO isr_id FROM ""Countries"" WHERE ""CountryCode"" = 'ISR';
    IF il_id IS NOT NULL AND isr_id IS NOT NULL THEN
        UPDATE ""Swimmers""  SET ""CountryId"" = isr_id WHERE ""CountryId"" = il_id;
        UPDATE ""Clubs""     SET ""CountryId"" = isr_id WHERE ""CountryId"" = il_id;
        UPDATE ""Results""   SET ""CountryId"" = isr_id WHERE ""CountryId"" = il_id;
        UPDATE ""HubGroups"" SET ""CountryId"" = isr_id WHERE ""CountryId"" = il_id;
        DELETE FROM ""Countries"" WHERE ""Id"" = il_id;
    END IF;
END $$;
UPDATE ""Competitions"" SET ""Country"" = 'ISR' WHERE ""Country"" IN ('IL', 'il');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Слияние необратимо: после merge нельзя восстановить, какие именно записи были
            // под "IL". Down — no-op (данные-миграция), откат только восстановлением из бэкапа.
        }
    }
}
