-- ============================================================================
-- Шаг 2 из 2: ГРАНТЫ НА ТАБЛИЦЫ. Запускается ПОСЛЕ миграций.
--
--     01-roles.sql  →  dotnet run -- --migrate  →  02-grants.sql
--
--   psql -h <host> -U <owner> -d <db> -f server/db/02-grants.sql
--
-- Локально:
--   docker exec -i swimm-postgres psql -U swimm -d swimm < server/db/02-grants.sql
--
-- Файл идемпотентен — гоняй сколько угодно.
--
-- ⚠ ЭТО ЕДИНСТВЕННЫЙ СПИСОК публичных таблиц в проекте. Новая публичная таблица ⇒
--   (1) добавить её сюда И (2) добавить GRANT в её миграцию (там он обёрнут в
--   проверку существования роли — образец в любой миграции с грантом).
--   Забыть — значит сломать публичный read-путь громко в деве, а не молча расширить
--   доступ read-роли к чувствительным данным. Это fail-closed by design.
-- ============================================================================

\set ON_ERROR_STOP on

-- ── swimm_rw: DML на всё, что уже существует ────────────────────────────────
-- Будущие таблицы покрыты ALTER DEFAULT PRIVILEGES из 01-roles.sql; здесь —
-- догоняющий грант для таблиц, созданных до того шага.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES   IN SCHEMA public TO swimm_rw;
GRANT USAGE, SELECT                  ON ALL SEQUENCES IN SCHEMA public TO swimm_rw;

-- ── swimm_ro: SELECT только на публичные бизнес-таблицы ─────────────────────
-- НЕ выдаётся на Sys_*, __EFMigrationsHistory и через default privileges.
GRANT SELECT ON
    "Categories",
    "CategoryCompetitions",
    "ClubCompetitionStandings",
    "Clubs",
    "CompetitionEvents",
    "CompetitionNoteTexts",
    "CompetitionNotes",
    "CompetitionResultUrls",
    "Competitions",
    "Countries",
    "Galleries",
    "GalleryItems",
    "HubGroupMembers",
    "HubGroups",
    "NormativeStandards",
    "PointRulesClubs",
    "PointRulesClubsEntries",
    "PointRulesSwimmers",
    "PointRulesSwimmersEntries",
    "Records",
    "RelayMembers",
    "Relays",
    "Results",
    "Styles",
    "Swimmers"
TO swimm_ro;

-- Единственная Sys_-таблица в RO-гранте, и это осознанно: спорные официальные рекорды
-- показываются на публичной витрине (docs/plans/records-quality-plan.md). Персональных
-- данных в ней нет — только ссылки на записи справочника рекордов и причина пометки.
GRANT SELECT ON "Sys_RecordIssues" TO swimm_ro;

-- NOTE: Sys_UserFavorites и Sys_UserMedia намеренно НЕ здесь. Фавориты и медиа —
-- данные конкретного залогиненного пользователя; они ходят ТОЛЬКО через SwimmDbContext
-- (роль swimm_rw, authenticated path). RO-путь — публичный анонимный read.

-- ── Проверка ────────────────────────────────────────────────────────────────
\echo ''
\echo 'Гранты применены. Таблиц, доступных swimm_ro на чтение (ожидается 26):'
SELECT count(DISTINCT table_name) AS ro_tables
  FROM information_schema.table_privileges
 WHERE grantee = 'swimm_ro' AND privilege_type = 'SELECT';
