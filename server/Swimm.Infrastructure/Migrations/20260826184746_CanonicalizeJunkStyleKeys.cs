using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Данные, а не схема: два мусорных ключа стиля из справочника <c>Styles</c> уводятся
    /// в канонический <c>freestyle</c>.
    ///
    /// Откуда взялись: парсер искал стиль точным совпадением по ивритскому словарю, и
    /// заголовок с лишним словом уезжал в ключ целиком —
    ///   • `מטר_חופשי` («3000 МЕТРОВ вольным») — 32 заплыва чемпионата Израиля 3 км
    ///     В БАССЕЙНЕ (#1540). Из-за неканонического ключа соревнование не показывала ни одна
    ///     витрина: селектор дисциплины берёт только канонические стили;
    ///   • `חופשי_נוקאוט` («вольный, нокаут-раунды») — 56 заплывов чемпионата в открытой
    ///     воде (#1547).
    ///
    /// Оба — вольный стиль: «метры» и «нокаут» это единица измерения и формат раунда, а не
    /// вид плавания. Причина устранена в <c>HebrewTextHelper.ResolveStyle</c> (поиск по
    /// токенам), эта миграция чинит уже загруженное. Решения — docs/data-integrity.md §9
    /// (2026-08-26).
    /// </summary>
    public partial class CanonicalizeJunkStyleKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Идём от ИМЕНИ, а не от Id: в другой базе (прод) идентификаторы свои.
            // Мусорных ключей нет — оба запроса просто ничего не тронут.
            migrationBuilder.Sql(
                @"UPDATE ""Results"" r
                     SET ""StyleId"" = canon.""Id""
                    FROM ""Styles"" junk, ""Styles"" canon
                   WHERE r.""StyleId"" = junk.""Id""
                     AND canon.""Name"" = 'freestyle'
                     AND junk.""Name"" IN ('מטר_חופשי', 'חופשי_נוקאוט');");

            // Опустевшие справочные строки удаляем: иначе они так и будут предлагаться
            // в админке как шестой и седьмой «вид плавания».
            migrationBuilder.Sql(
                @"DELETE FROM ""Styles"" s
                   WHERE s.""Name"" IN ('מטר_חופשי', 'חופשי_נוקאוט')
                     AND NOT EXISTS (SELECT 1 FROM ""Results"" r WHERE r.""StyleId"" = s.""Id"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Обратной операции нет и быть не может: после слияния уже не отличить, какие
            // заплывы вольным пришли с этих двух соревнований, а какие были там всегда.
            // Схему миграция не трогает, поэтому откатывать нечего.
        }
    }
}
