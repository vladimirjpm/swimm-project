-- ============================================================================
-- Least-privilege DB roles for Swimm (Phase 3 hardening).
--
-- Three privilege tiers, isolating the unauthenticated public read path from
-- write/DDL capability — the main blast-radius lever (instead of splitting the
-- deployment).
--
--   owner  (existing role `swimm`)  — owns schema + tables, runs MIGRATIONS (DDL).
--                                      Used only by MigrationConnection / `--migrate`.
--   swimm_rw                         — runtime DML (SELECT/INSERT/UPDATE/DELETE),
--                                      NO DDL. Used by AdminConnection (auth + admin + import).
--   swimm_ro                         — runtime SELECT on PUBLIC BUSINESS tables ONLY.
--                                      Used by ReadConnection (public results browsing).
--                                      Deliberately has NO access to Sys_* tables, so a
--                                      SQL-injection in the read path cannot read user PII
--                                      / SecurityStamp, and cannot write anything.
--
-- Run ONCE per database, as a superuser or the owner `swimm`:
--   docker exec -i swimm-postgres psql -U swimm -d swimm < server/db/setup-roles.sql
--
-- Passwords below are LOCAL DEV ONLY (mirrors the committed swimm_local_dev password).
-- In production, set them out-of-band and inject the connection strings via secrets/env.
-- ============================================================================

\set ON_ERROR_STOP on

-- ── Roles (idempotent) ──────────────────────────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_rw') THEN
        CREATE ROLE swimm_rw LOGIN PASSWORD 'swimm_rw_local_dev';
    END IF;
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro') THEN
        CREATE ROLE swimm_ro LOGIN PASSWORD 'swimm_ro_local_dev';
    END IF;
END$$;

-- Neither runtime role should ever create objects.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM swimm_rw, swimm_ro;

GRANT CONNECT ON DATABASE swimm TO swimm_rw, swimm_ro;
GRANT USAGE   ON SCHEMA   public TO swimm_rw, swimm_ro;

-- ── swimm_rw: full DML on every table, present and future ───────────────────
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES    IN SCHEMA public TO swimm_rw;
GRANT USAGE, SELECT                  ON ALL SEQUENCES  IN SCHEMA public TO swimm_rw;

-- Future tables/sequences created by the owner (migrations) inherit the grants.
ALTER DEFAULT PRIVILEGES FOR ROLE swimm IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO swimm_rw;
ALTER DEFAULT PRIVILEGES FOR ROLE swimm IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO swimm_rw;

-- ── swimm_ro: SELECT on PUBLIC BUSINESS tables ONLY ─────────────────────────
-- NOT granted on Sys_* tables, __EFMigrationsHistory, or via default privileges.
-- A new *public* table served to anonymous users must be added here explicitly
-- (fail-closed: forgetting it breaks the read path loudly in dev, never silently
-- widens RO access to sensitive tables).
GRANT SELECT ON
    "Competitions",
    "Clubs",
    "Swimmers",
    "Relays",
    "Galleries",
    "GalleryItems",
    "Styles",
    "Countries",
    "Results",
    "RelayMembers",
    "PointRulesClubs",
    "PointRulesClubsEntries",
    "PointRulesSwimmers",
    "PointRulesSwimmersEntries",
    "Categories",
    "CategoryCompetitions",
    "CompetitionEvents",
    "CompetitionResultUrls",
    "Records",
    "NormativeStandards",
    "HubGroups",
    "HubGroupMembers"
TO swimm_ro;

-- NOTE: Sys_UserFavorites и Sys_UserMedia намеренно НЕ добавлены в этот грант.
-- Фавориты и медиа — данные конкретного залогиненного пользователя; они ходят ТОЛЬКО
-- через SwimmDbContext (роль swimm_rw, authenticated path). RO-путь (swimm_ro) —
-- публичный анонимный read; давать ему доступ к Sys_* таблицам нельзя.
