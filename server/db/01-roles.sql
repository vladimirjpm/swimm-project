-- ============================================================================
-- Шаг 1 из 2: РОЛИ. Запускается на ещё пустой БД, ДО миграций.
--
-- Почему отдельно от грантов. Тут замкнутый круг: гранты ссылаются на таблицы,
-- которых до миграций нет, а миграции ссылаются на роль swimm_ro, которой нет до
-- этого шага. Разрывается он только порядком:
--
--     01-roles.sql  →  dotnet run -- --migrate  →  02-grants.sql
--
-- Три уровня привилегий (изолируют анонимный публичный read от записи и DDL —
-- главный рычаг blast radius вместо разделения деплоя):
--
--   owner (админ-логин БД)  — владеет схемой, накатывает миграции (DDL).
--                             Используется только MigrationConnection / --migrate.
--   swimm_rw                — рантайм-DML без DDL. AdminConnection (auth, админка, импорт).
--   swimm_ro                — SELECT только на публичные бизнес-таблицы. ReadConnection.
--                             Намеренно НЕ видит Sys_* — SQL-инъекция в публичном
--                             read-пути не прочитает ПДн и SecurityStamp и ничего не запишет.
--
-- Пароли в файле НЕ зашиты — передаются переменными psql:
--
--   psql -h <host> -U <owner> -d <db> \
--        -v rw_password='...' -v ro_password='...' [-v owner=swimm] \
--        -f server/db/01-roles.sql
--
-- Локально (дев-пароли те же, что в appsettings.json):
--
--   docker exec -i swimm-postgres psql -U swimm -d swimm \
--     -v rw_password=swimm_rw_local_dev -v ro_password=swimm_ro_local_dev \
--     < server/db/01-roles.sql
--
-- Повторный запуск безопасен и заодно ротирует пароли.
-- Список таблиц для swimm_ro живёт в 02-grants.sql — и только там.
-- ============================================================================

\set ON_ERROR_STOP on

\if :{?rw_password}
\else
\echo '!! Не задан rw_password. Пример: -v rw_password=... -v ro_password=...'
DO $$ BEGIN RAISE EXCEPTION 'rw_password не задан'; END $$;
\endif

\if :{?ro_password}
\else
\echo '!! Не задан ro_password.'
DO $$ BEGIN RAISE EXCEPTION 'ro_password не задан'; END $$;
\endif

-- Владелец схемы = админ-логин сервера. На Azure Flexible Server его удобно назвать
-- swimm: тогда MigrationConnection и ALTER DEFAULT PRIVILEGES ниже работают без правок.
\if :{?owner}
\else
\set owner swimm
\endif

-- ── Роли ────────────────────────────────────────────────────────────────────
-- CREATE — только если роли ещё нет; пароль выставляем всегда, отдельным шагом,
-- поэтому повторный запуск работает и как ротация. \gexec на пустой выборке
-- не выполняет ничего.
SELECT format('CREATE ROLE swimm_rw LOGIN PASSWORD %L', :'rw_password')
 WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_rw')
\gexec

SELECT format('CREATE ROLE swimm_ro LOGIN PASSWORD %L', :'ro_password')
 WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'swimm_ro')
\gexec

SELECT format('ALTER ROLE swimm_rw PASSWORD %L', :'rw_password')
\gexec

SELECT format('ALTER ROLE swimm_ro PASSWORD %L', :'ro_password')
\gexec

-- ── Ни одна рантайм-роль не создаёт объекты ─────────────────────────────────
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM swimm_rw, swimm_ro;

-- Имя БД берём из подключения: на Azure оно может не совпадать с локальным swimm.
SELECT format('GRANT CONNECT ON DATABASE %I TO swimm_rw, swimm_ro', current_database())
\gexec

GRANT USAGE ON SCHEMA public TO swimm_rw, swimm_ro;

-- ── Будущие объекты ─────────────────────────────────────────────────────────
-- Таблицы и последовательности, которые создаст владелец (то есть миграции),
-- сразу получают DML для swimm_rw. Ровно поэтому шаг идёт ДО миграций: применённые
-- задним числом default privileges на уже созданные таблицы не распространяются.
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO swimm_rw;
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner" IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO swimm_rw;

\echo ''
\echo 'OK: роли swimm_rw и swimm_ro готовы.'
\echo 'Дальше: dotnet run --project server/Swimm.API -- --migrate, затем 02-grants.sql'
