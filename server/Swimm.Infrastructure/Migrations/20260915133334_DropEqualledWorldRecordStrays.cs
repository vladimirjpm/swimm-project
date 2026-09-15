using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Инцидент И-19 (docs/data-integrity.md): повторённый мировой рекорд («=WR» в отчёте World
    /// Aquatics) парсер считал национальным, и в Records осели две строки-одиночки —
    /// country/USA/open (100 на спине ж 25 м, Regan Smith) и country/JAM/open (100 брасс ж 25 м,
    /// Alia Atkinson). Правило живёт в `WorldRecordsParser.NormalizeRecordType`, здесь — разовая
    /// уборка того, что успело записаться до него.
    ///
    /// Схема не меняется: миграция чинит ДАННЫЕ.
    ///
    /// ⚠ Удаляем, только пока у страны в Records ровно одна строка. Значения-то верные — это и
    /// есть национальные рекорды США и Ямайки, просто приехавшие не тем путём. После Фазы 11
    /// (рекорды всех стран) те же дисциплины лягут законно в полный набор страны, и миграция,
    /// применённая к такой базе позже (прод отстаёт), обязана их не тронуть.
    /// </summary>
    public partial class DropEqualledWorldRecordStrays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "Records" r
                 WHERE r."RegionType" = 'country'
                   AND r."Category" = 'open'
                   AND r."Gender" = 'female'
                   AND r."PoolType" = '25m'
                   AND r."Distance" = '100m'
                   AND ((r."RegionCode" = 'USA' AND r."Style" = 'backstroke')
                     OR (r."RegionCode" = 'JAM' AND r."Style" = 'breaststroke'))
                   AND (SELECT count(*) FROM "Records" x
                         WHERE x."RegionType" = 'country'
                           AND x."RegionCode" = r."RegionCode") = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Возвращать нечего: строки лежали не в своём наборе. Национальные рекорды США и
            // Ямайки приедут полным набором импортом Фазы 11, соавторство в world — через
            // --records-refresh.
        }
    }
}
