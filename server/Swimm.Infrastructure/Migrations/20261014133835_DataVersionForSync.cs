using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <summary>
    /// Версия данных для синхронизации «прод → локально» (решение В17,
    /// docs/plans/azure-deploy-plan.md §0-тер-бис). После запуска главная база — прод; локальная —
    /// её копия, и отставание должно быть видно, а не угадываться.
    ///
    /// <list type="bullet">
    /// <item><c>Sys_DataVersionSeq</c> — сама версия. Последовательность, а не счётчик в строке:
    /// <c>nextval</c> не берёт блокировок, поэтому длинный импорт не ставит в очередь всех остальных
    /// писателей (счётчик в одной строке держал бы её блокировку до коммита — и ловил бы deadlock с
    /// любым, кто трогает те же строки в другом порядке). Цена — откаченная транзакция тоже
    /// двигает версию: лишняя синхронизация безопасна.</item>
    /// <item><c>Sys_DataVersion</c> — по строке на таблицу: когда и какой версией её меняли в
    /// последний раз («последнее изменение — Results, 14.10 21:03»). Строку занятую — пропускаем
    /// (<c>SKIP LOCKED</c>): подпись приблизительная, версия — точная.</item>
    /// <item>Триггер <c>Sys_DataVersion_Bump</c> — statement-level, раз на оператор, а не на строку:
    /// ловит всё (админку, CLI, ручной SQL, TRUNCATE) почти бесплатно. Стоит на таблицах из
    /// <c>server/db/seed-tables.txt</c> — ровно то, что переносит синхронизация; сверяет это
    /// тест <c>DataVersionPgTests</c>.</item>
    /// <item><c>Sys_SyncState</c> — заполняется только в ЛОКАЛЬНОЙ базе скриптом
    /// <c>server/db/sync-from-prod.sh</c>: от какой версии прода снята копия.</item>
    /// </list>
    ///
    /// Функция — SECURITY DEFINER: пишет в служебную таблицу от владельца, какой бы ролью ни шла
    /// запись бизнес-таблицы. Чтение версии — роль <c>swimm_sync</c> (01-roles.sql / 02-grants.sql).
    /// </summary>
    public partial class DataVersionForSync : Migration
    {
        /// <summary>
        /// Снимок <c>seed-tables.txt</c> на 14.10.2026. Новая таблица в списке ⇒ новая миграция с
        /// триггером на ней (и строкой в Sys_DataVersion); забыть — упадёт DataVersionPgTests.
        /// </summary>
        internal static readonly string[] Tables =
        [
            "Categories", "CategoryCompetitions", "ClubCompetitionStandings", "Clubs", "CompetitionEntries",
            "CompetitionEvents", "CompetitionMeetInfos", "CompetitionNoteTexts", "CompetitionNotes",
            "CompetitionResultUrls", "CompetitionSources", "CompetitionWarmUps", "Competitions", "Countries",
            "Galleries", "GalleryItems", "NormativeStandards", "PointRulesClubs", "PointRulesClubsEntries",
            "PointRulesSwimmers", "PointRulesSwimmersEntries", "Records", "RelayMembers", "Relays", "Results",
            "Styles", "Swimmers",
            "Sys_DedupIgnoredPairs", "Sys_DiscoveredCompetitions", "Sys_ImportHistory",
            "Sys_ImportReconciliation", "Sys_RecordIssues", "Sys_RecordVerifications", "Sys_StartListPulls",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE SEQUENCE "Sys_DataVersionSeq" AS bigint;

                CREATE TABLE "Sys_DataVersion" (
                    "Table"     varchar(100) PRIMARY KEY,
                    "Version"   bigint       NOT NULL DEFAULT 0,
                    "ChangedAt" timestamptz  NULL
                );

                CREATE TABLE "Sys_SyncState" (
                    "Id"                smallint     PRIMARY KEY CHECK ("Id" = 1),
                    "SyncedFromVersion" bigint       NOT NULL,
                    "SyncedAt"          timestamptz  NOT NULL,
                    "SourceHost"        varchar(255) NULL
                );

                CREATE FUNCTION "Sys_BumpDataVersion"() RETURNS trigger
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = public AS $$
                DECLARE
                    v bigint := nextval('"Sys_DataVersionSeq"');
                BEGIN
                    UPDATE "Sys_DataVersion" d
                       SET "Version" = v, "ChangedAt" = clock_timestamp()
                     WHERE d."Table" IN (SELECT s."Table" FROM "Sys_DataVersion" s
                                          WHERE s."Table" = TG_TABLE_NAME
                                          FOR UPDATE SKIP LOCKED);
                    IF NOT FOUND AND NOT EXISTS (SELECT 1 FROM "Sys_DataVersion" WHERE "Table" = TG_TABLE_NAME) THEN
                        INSERT INTO "Sys_DataVersion" ("Table", "Version", "ChangedAt")
                        VALUES (TG_TABLE_NAME, v, clock_timestamp())
                        ON CONFLICT DO NOTHING;
                    END IF;
                    RETURN NULL;
                END $$;
                """);

            foreach (var t in Tables)
                migrationBuilder.Sql($"""
                    INSERT INTO "Sys_DataVersion" ("Table") VALUES ('{t}');
                    CREATE TRIGGER "Sys_DataVersion_Bump"
                        AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON "{t}"
                        FOR EACH STATEMENT EXECUTE FUNCTION "Sys_BumpDataVersion"();
                    """);

            // Роль чтения версии и данных для синхронизации — если уже заведена (01-roles.sql с
            // sync_password). Иначе гранты выдаст 02-grants.sql: он идёт после миграций.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'swimm_sync') THEN
                        GRANT SELECT ON "Sys_DataVersion" TO swimm_sync;
                        GRANT SELECT ON "Sys_DataVersionSeq" TO swimm_sync;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in Tables)
                migrationBuilder.Sql($"""DROP TRIGGER IF EXISTS "Sys_DataVersion_Bump" ON "{t}";""");

            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS "Sys_BumpDataVersion"();
                DROP TABLE IF EXISTS "Sys_SyncState";
                DROP TABLE IF EXISTS "Sys_DataVersion";
                DROP SEQUENCE IF EXISTS "Sys_DataVersionSeq";
                """);
        }
    }
}
