#!/usr/bin/env bash
# ============================================================================
# Синхронизация «прод → локально» (решение В17, docs/plans/azure-deploy-plan.md
# §0-тер-бис). После запуска главная база — прод; локальная — её копия для
# разработки. Скрипт подтягивает в ЛОКАЛЬНУЮ базу бизнес-данные прода
# (seed-tables.txt — без персональных данных) и НЕ трогает локальное своё:
# аккаунты, избранное, медиа, группы остаются (sync-merge.sql).
#
# Прод — стандартными PG*-переменными, роль swimm_sync (01-roles.sql):
#
#   PGHOST=<сервер>.postgres.database.azure.com PGUSER=swimm_sync PGDATABASE=swimm \
#   PGSSLMODE=require PGPASSWORD=... ./sync-from-prod.sh
#
# Пароль — через PGPASSWORD или ~/.pgpass (тогда пробросить PGPASSFILE нельзя:
# pg_dump идёт в контейнере, — используй PGPASSWORD в этой же строке).
#
# Флаги:
#   --yes             без вопроса «точно?» (для агента — после подтверждения в чате)
#   --skip-migrate    не накатывать локальные миграции перед сверкой схемы
#   --skip-personas   не пересоздавать тестовых персонажей utest-*
#   --keep-dump       не удалять снятый дамп (по умолчанию — во временной папке)
#
# Шаги: 1) --migrate локально; 2) сверка миграций прода и локальных — должны
# совпасть; 3) версия прода (ДО дампа: правки во время дампа дадут честное
# «отстаёт»); 4) dump-seed.sh --remote; 5) заливка в схему _sync + sync-merge.sql
# одной транзакцией; 6) последовательности; 7) Sys_SyncState; 8) --seed-personas
# --reset (локальные тестовые пловцы групп могли уйти вместе с занятыми Id).
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"

CONTAINER="${SWIMM_PG_CONTAINER:-swimm-postgres}"
LOCAL_USER="${LOCAL_PGUSER:-swimm}"
LOCAL_DB="${LOCAL_PGDATABASE:-swimm}"

YES=0; SKIP_MIGRATE=0; SKIP_PERSONAS=0; KEEP_DUMP=0
while [ $# -gt 0 ]; do
  case "$1" in
    --yes) YES=1; shift ;;
    --skip-migrate) SKIP_MIGRATE=1; shift ;;
    --skip-personas) SKIP_PERSONAS=1; shift ;;
    --keep-dump) KEEP_DUMP=1; shift ;;
    -h|--help) sed -n '2,31p' "$0"; exit 0 ;;
    *) echo "Неизвестный аргумент: $1" >&2; exit 2 ;;
  esac
done

: "${PGHOST:?не задан PGHOST — адрес прод-сервера (см. --help)}"
export PGHOST PGPORT="${PGPORT:-5432}" PGUSER="${PGUSER:-swimm_sync}" PGDATABASE="${PGDATABASE:-swimm}"
[ -n "${PGSSLMODE:-}" ] && export PGSSLMODE
[ -n "${PGPASSWORD:-}" ] && export PGPASSWORD

# Источник и цель не должны совпасть: «синхронизировать базу саму с собой» — это
# DELETE ушедших строк без единой строки прода, то есть потеря данных.
case "$PGHOST" in
  localhost|127.0.0.1|::1)
    if [ "$PGDATABASE" = "$LOCAL_DB" ]; then
      echo "!! Источник — та же локальная база ($PGHOST/$PGDATABASE). Нужен прод." >&2
      exit 1
    fi ;;
esac

local_psql() { docker exec -i "$CONTAINER" psql -U "$LOCAL_USER" -d "$LOCAL_DB" -v ON_ERROR_STOP=1 "$@"; }
prod_psql()  { docker exec -i -e PGHOST -e PGPORT -e PGUSER -e PGPASSWORD -e PGDATABASE -e PGSSLMODE \
                 "$CONTAINER" psql -v ON_ERROR_STOP=1 -X "$@"; }

docker inspect -f '{{.State.Running}}' "$CONTAINER" 2>/dev/null | grep -q true \
  || { echo "!! Контейнер $CONTAINER не запущен: docker compose -f server/docker-compose.yml up -d" >&2; exit 1; }

echo "Прод:      $PGUSER@$PGHOST:$PGPORT/$PGDATABASE"
echo "Локально:  $LOCAL_USER@$CONTAINER/$LOCAL_DB"

# ── 1. Локальные миграции ──────────────────────────────────────────────────
if [ "$SKIP_MIGRATE" = "0" ]; then
  echo "Накатываю локальные миграции..."
  (cd "$REPO_DIR" && ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/Swimm.API -- --migrate) \
    > /dev/null || { echo "!! --migrate упал (API запущен и держит сборку? см. CLAUDE.md, build-lock)" >&2; exit 1; }
fi

# ── 2. Схема прода = схема локальной ───────────────────────────────────────
MIG_SQL='SELECT coalesce(string_agg("MigrationId", '"','"' ORDER BY "MigrationId"), '"''"') FROM "__EFMigrationsHistory";'
PROD_MIG="$(prod_psql -t -A -c "$MIG_SQL")"
LOCAL_MIG="$(local_psql -t -A -c "$MIG_SQL")"
if [ "$PROD_MIG" != "$LOCAL_MIG" ]; then
  PROD_LAST="${PROD_MIG##*,}"; LOCAL_LAST="${LOCAL_MIG##*,}"
  echo "!! Миграции прода и локальной базы расходятся:" >&2
  echo "!!   прод:     $PROD_LAST" >&2
  echo "!!   локально: $LOCAL_LAST" >&2
  echo "!! Код новее прода — сначала --migrate на проде (ProdAdmin); старее — git pull в master." >&2
  exit 1
fi
echo "Схема совпадает: ${LOCAL_MIG##*,}"

# ── 3. Версия прода — ДО дампа ─────────────────────────────────────────────
PROD_VERSION="$(prod_psql -t -A -c 'SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM "Sys_DataVersionSeq";')"
LOCAL_SYNCED="$(local_psql -t -A -c 'SELECT "SyncedFromVersion" FROM "Sys_SyncState" WHERE "Id" = 1;')"
echo "Версия прода: v$PROD_VERSION; локальная копия: ${LOCAL_SYNCED:+v$LOCAL_SYNCED}${LOCAL_SYNCED:-ни разу не снималась}"

if [ "$YES" != "1" ]; then
  echo ""
  echo "Бизнес-таблицы локальной базы станут как на проде: ушедшие на проде строки удалятся"
  echo "(и локальное, что держится только за них), новые и изменённые — приедут."
  read -r -p "Продолжить? [y/N] " ans
  case "$ans" in y|Y|yes|да) ;; *) echo "Отменено."; exit 1 ;; esac
fi

# ── 4. Дамп прода ──────────────────────────────────────────────────────────
WORK="$(mktemp -d)"
cleanup() {
  # Сбой посреди слияния: транзакция откатилась, но промежуточная схема осталась — убрать.
  local_psql -q -c "DROP SCHEMA IF EXISTS _sync CASCADE" > /dev/null 2>&1 || true
  if [ "$KEEP_DUMP" = "1" ]; then echo "Дамп оставлен: $WORK/prod-seed.sql"; else rm -rf "$WORK"; fi
}
trap cleanup EXIT
"$SCRIPT_DIR/dump-seed.sh" --remote -o "$WORK/prod-seed.sql" | sed 's/^/  /' | grep -v 'Дальше на проде' || true
[ -s "$WORK/prod-seed.sql" ] || { echo "!! Дамп пуст" >&2; exit 1; }

# ── 5. В схему _sync и слияние — одной транзакцией в sync-merge.sql ────────
TABLES=()
while IFS= read -r line; do
  line="${line%%#*}"; line="$(echo "$line" | tr -d '[:space:]')"
  [ -n "$line" ] && TABLES+=("$line")
done < "$SCRIPT_DIR/seed-tables.txt"

echo "Сливаю в локальную базу..."
{
  echo "SET client_min_messages = warning;"
  echo "DROP SCHEMA IF EXISTS _sync CASCADE;"
  echo "CREATE SCHEMA _sync;"
  # Копия без ограничений и умолчаний: строки прода ложатся как есть, проверки — при слиянии.
  for t in "${TABLES[@]}"; do
    echo "CREATE TABLE _sync.\"$t\" AS SELECT * FROM public.\"$t\" WITH NO DATA;"
  done
  # Дамп целится в public — переводим COPY в _sync; setval не нужен (шаг 6).
  sed -e 's/^COPY public\./COPY _sync./' -e '/^SELECT pg_catalog\.setval/d' "$WORK/prod-seed.sql"
  echo "SET search_path = public;"
} | local_psql -q -f - > /dev/null

local_psql -q -f - < "$SCRIPT_DIR/sync-merge.sql" 2>&1 | sed -n -e 's/^.*NOTICE:  /  /p' -e '/ERROR/p'

# ── 6. Последовательности ──────────────────────────────────────────────────
local_psql -q -f - < "$SCRIPT_DIR/fix-sequences.sql" 2>&1 | sed -n -e 's/^.*NOTICE:  /  /p' -e '/ERROR/p'

# ── 7. Отметка синхронизации ───────────────────────────────────────────────
local_psql -q -c "
INSERT INTO \"Sys_SyncState\" (\"Id\", \"SyncedFromVersion\", \"SyncedAt\", \"SourceHost\")
VALUES (1, $PROD_VERSION, now(), '$PGHOST')
ON CONFLICT (\"Id\") DO UPDATE SET \"SyncedFromVersion\" = EXCLUDED.\"SyncedFromVersion\",
    \"SyncedAt\" = EXCLUDED.\"SyncedAt\", \"SourceHost\" = EXCLUDED.\"SourceHost\";"

# ── 8. Тестовые персонажи ──────────────────────────────────────────────────
if [ "$SKIP_PERSONAS" = "0" ]; then
  echo "Пересоздаю тестовых персонажей utest-*..."
  (cd "$REPO_DIR" && ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/Swimm.API --no-build -- --seed-personas --reset) \
    > /dev/null || echo "!! --seed-personas --reset не прошёл — данные синхронизированы, персонажей пересоздай руками." >&2
fi

echo ""
echo "Готово: локальная копия = прод v$PROD_VERSION."
local_psql -c "
SELECT 'Results' AS tbl, count(*) FROM \"Results\"
UNION ALL SELECT 'Swimmers', count(*) FROM \"Swimmers\"
UNION ALL SELECT 'Competitions', count(*) FROM \"Competitions\"
UNION ALL SELECT 'Clubs', count(*) FROM \"Clubs\"
ORDER BY 2 DESC;"
# Слияние шло через psql, мимо приложения, — сброс кэша сам не случился. Шлём общий сброс в
# канал моста (Services/CacheBridge.cs): запущенный локальный API сбросит всё у себя.
local_psql -q -c "SELECT pg_notify('swimm_cache', '{\"o\":\"sync-from-prod\",\"a\":true,\"r\":\"sync-from-prod.sh: локальная копия обновлена с прода\"}');" > /dev/null
echo "Кэш запущенного локального API сброшен через мост (выключен мост — кнопка на /Admin/Cache)."
