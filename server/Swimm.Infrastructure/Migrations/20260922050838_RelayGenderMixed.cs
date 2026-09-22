using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Э4 плана docs/plans/records-relays-plan.md (22.09.2026): пол эстафеты в Results.
    /// «none» раньше значил и «смешанная», и «неизвестен». Смешанной считаем эстафету, у которой
    /// среди участников (RelayMembers → Swimmers) есть и мужчина, и женщина (старые M/F — тоже);
    /// остальные «none» не трогаем — их разбирают по loglig. Плюс карточки пловцов, которым
    /// EnrichSwimmersFromResultsAsync протащил пол эстафеты («none»), — обратно в NULL.
    /// Правило то же, что у импорта: RelayGender.Resolve.
    /// </summary>
    public partial class RelayGenderMixed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Results" r SET "Gender" = 'mixed'
                WHERE r."RelayId" IS NOT NULL
                  AND coalesce(r."Gender", '') IN ('', 'none')
                  AND EXISTS (SELECT 1 FROM "RelayMembers" m JOIN "Swimmers" s ON s."Id" = m."SwimmerId"
                              WHERE m."RelayId" = r."RelayId" AND lower(s."Gender") IN ('male', 'm'))
                  AND EXISTS (SELECT 1 FROM "RelayMembers" m JOIN "Swimmers" s ON s."Id" = m."SwimmerId"
                              WHERE m."RelayId" = r."RelayId" AND lower(s."Gender") IN ('female', 'f'));

                UPDATE "Swimmers" SET "Gender" = NULL WHERE "Gender" IN ('none', 'mixed');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Обратно в прежнее слитое значение; карточки пловцов не восстанавливаем — «none» там был багом.
            migrationBuilder.Sql("""UPDATE "Results" SET "Gender" = 'none' WHERE "RelayId" IS NOT NULL AND "Gender" = 'mixed';""");
        }
    }
}
