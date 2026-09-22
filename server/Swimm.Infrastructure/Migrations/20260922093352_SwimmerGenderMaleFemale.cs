using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Пол в карточке пловца — только male/female или NULL (хвост §5 docs/plans/records-relays-plan.md,
    /// 22.09.2026). «M»/«F» писал сидер тренировок Дельфина (18 local-пловцов); читатели понимали
    /// оба формата, но каждый новый читатель обязан был это помнить. Сначала данные, потом CHECK.
    /// Пустых строк в базе нет (замер 22.09), но на всякий случай — в NULL.
    /// </summary>
    public partial class SwimmerGenderMaleFemale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Swimmers" SET "Gender" = CASE
                    WHEN lower(trim("Gender")) IN ('m', 'male') THEN 'male'
                    WHEN lower(trim("Gender")) IN ('f', 'female') THEN 'female'
                    ELSE NULL END
                WHERE "Gender" IS NOT NULL AND "Gender" NOT IN ('male', 'female');
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Swimmers_Gender",
                table: "Swimmers",
                sql: "\"Gender\" IS NULL OR \"Gender\" IN ('male', 'female')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Swimmers_Gender",
                table: "Swimmers");
        }
    }
}
