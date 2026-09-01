-- ============================================================================
-- ЭТОТ ФАЙЛ БОЛЬШЕ НЕ ЗАПУСКАЕТСЯ. Разбит на два шага 2026-08-27.
--
-- Почему: он не работал на чистой БД. Гранты внутри него ссылались на таблицы,
-- которых до миграций не существует, а миграции ссылались на роль swimm_ro, которой
-- не существует до него. Замкнутый круг разрывается только порядком:
--
--     server/db/01-roles.sql            -- роли, ДО миграций
--     dotnet run --project server/Swimm.API -- --migrate
--     server/db/02-grants.sql           -- гранты на таблицы, ПОСЛЕ миграций
--
-- Заодно ушли два хардкода: пароли ролей (теперь переменные psql, в репозитории их
-- нет) и имя базы swimm в GRANT CONNECT (теперь current_database() — на Azure имя
-- может быть другим).
--
-- Список публичных таблиц для swimm_ro переехал в 02-grants.sql и живёт только там.
-- Подробности — docs/plans/azure-deploy-plan.md Б9.
-- ============================================================================

\echo ''
\echo '!! server/db/setup-roles.sql разбит на два файла и больше не запускается.'
\echo '!! Порядок:'
\echo '!!   1) psql ... -v rw_password=... -v ro_password=... -f server/db/01-roles.sql'
\echo '!!   2) dotnet run --project server/Swimm.API -- --migrate'
\echo '!!   3) psql ... -f server/db/02-grants.sql'
\echo ''

DO $$
BEGIN
    RAISE EXCEPTION 'setup-roles.sql устарел: запусти 01-roles.sql, затем --migrate, затем 02-grants.sql';
END $$;
