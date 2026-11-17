using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLanePlanSessionStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sys_LanePlans_HubGroupId_Date",
                table: "Sys_LanePlans");

            migrationBuilder.AddColumn<string>(
                name: "Start",
                table: "Sys_LanePlans",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            // CF5 плана trainings-coach-flow (§5.1): план на дату переходит к занятию этой даты —
            // слоту недельного расписания (HubGroups.TrainingSchedule, день ISO) или разовому занятию
            // (Sys_TrainingSessions с «Ask who's coming» и началом; дата там — полночь UTC). Занятий
            // несколько — к первому по времени («HH:mm» сравнивается строкой). Занятия нет — Start
            // остаётся null: план дня, архив только для чтения. Содержимое планов не трогаем.
            // На 17.11.2026 спорных нет: прод — 4 плана, локально — 1, у каждого ровно одно занятие.
            migrationBuilder.Sql("""
                UPDATE "Sys_LanePlans" AS p
                SET "Start" = k.start
                FROM (
                    SELECT x."Id", min(x.start) AS start
                    FROM (
                        SELECT lp."Id", s->>'start' AS start
                        FROM "Sys_LanePlans" lp
                        JOIN "HubGroups" h ON h."Id" = lp."HubGroupId"
                        CROSS JOIN LATERAL jsonb_array_elements(
                            CASE WHEN jsonb_typeof(h."TrainingSchedule"::jsonb -> 'slots') = 'array'
                                 THEN h."TrainingSchedule"::jsonb -> 'slots' ELSE '[]'::jsonb END) s
                        WHERE s->>'day' ~ '^[1-7]$'
                          AND (s->>'day')::int = EXTRACT(ISODOW FROM lp."Date")
                          AND s->>'start' ~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'
                        UNION
                        SELECT lp."Id", t."StartTime"
                        FROM "Sys_LanePlans" lp
                        JOIN "Sys_TrainingSessions" t ON t."HubGroupId" = lp."HubGroupId"
                         AND (t."Date" AT TIME ZONE 'UTC')::date = lp."Date"
                        WHERE t."AsksRsvp" AND t."StartTime" ~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'
                    ) x
                    GROUP BY x."Id"
                ) k
                WHERE p."Id" = k."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_LanePlans_HubGroupId_Date_Start",
                table: "Sys_LanePlans",
                columns: new[] { "HubGroupId", "Date", "Start" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LanePlans_Start",
                table: "Sys_LanePlans",
                sql: "\"Start\" IS NULL OR \"Start\" ~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sys_LanePlans_HubGroupId_Date_Start",
                table: "Sys_LanePlans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LanePlans_Start",
                table: "Sys_LanePlans");

            // Откат к «один план на дату»: у двух занятий одного дня планов два — остаётся план
            // первого по времени, остальные удаляются (иначе UNIQUE (группа, дата) не создать).
            migrationBuilder.Sql("""
                DELETE FROM "Sys_LanePlans" p
                USING "Sys_LanePlans" first
                WHERE first."HubGroupId" = p."HubGroupId" AND first."Date" = p."Date"
                  AND (first."Start" IS NULL AND p."Start" IS NOT NULL OR first."Start" < p."Start");
                """);

            migrationBuilder.DropColumn(
                name: "Start",
                table: "Sys_LanePlans");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_LanePlans_HubGroupId_Date",
                table: "Sys_LanePlans",
                columns: new[] { "HubGroupId", "Date" },
                unique: true);
        }
    }
}
