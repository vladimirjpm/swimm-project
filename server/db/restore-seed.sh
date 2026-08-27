#!/usr/bin/env bash
# ============================================================================
# Заливает дамп бизнес-данных (dump-seed.sh) в уже мигрированную БД.
#
# Порядок первого запуска целиком:
#   01-roles.sql  ->  --migrate  ->  02-grants.sql  ->  restore-seed.sh
#
#   ./restore-seed.sh                                  # в локальный контейнер
#   ./restore-seed.sh -f /tmp/seed.sql
#   PGHOST=... PGUSER=swimm PGDATABASE=swimm PGSSLMODE=require ./restore-seed.sh --direct
#
# Пароль — через ~/.pgpass или PGPASSWORD, в аргументах не передаём.
#
# Почему не «просто psql < dump»: в схеме есть циклическая внешняя ссылка
# (Clubs.MergedIntoId -> Clubs.Id, след от склейки клубов), и pg_dump сам про неё
# предупреждает. Штатный ответ pg_dump — --disable-triggers, но он требует
# суперпользователя, которого на Azure Flexible Server НЕТ. Поэтому здесь другой
# путь, доступный владельцу таблиц: на время загрузки делаем внешние ключи
# отложенными, а перед коммитом возвращаем как было — проверка срабатывает внутри
# той же транзакции, так что битые данные откатятся целиком, а не лягут в прод.
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

CONTAINER="${SWIMM_PG_CONTAINER:-swimm-postgres}"
PGUSER="${PGUSER:-swimm}"
PGDATABASE="${PGDATABASE:-swimm}"
DUMP="$SCRIPT_DIR/seed-data.sql"
DIRECT=0
FORCE=0

while [ $# -gt 0 ]; do
  case "$1" in
    -f|--file) DUMP="$2"; shift 2 ;;
    --direct) DIRECT=1; shift ;;
    --force) FORCE=1; shift ;;
    -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
    *) echo "Неизвестный аргумент: $1" >&2; exit 2 ;;
  esac
done

[ -f "$DUMP" ] || { echo "Не найден дамп: $DUMP" >&2; exit 1; }

run_psql() {
  if [ "$DIRECT" = "1" ]; then
    psql -U "$PGUSER" -d "$PGDATABASE" -v ON_ERROR_STOP=1 "$@"
  else
    docker exec -i "$CONTAINER" psql -U "$PGUSER" -d "$PGDATABASE" -v ON_ERROR_STOP=1 "$@"
  fi
}

# ── Проверки перед заливкой ────────────────────────────────────────────────
MIGRATIONS="$(run_psql -t -A -c 'SELECT count(*) FROM "__EFMigrationsHistory";' 2>/dev/null || echo 0)"
if [ "$MIGRATIONS" = "0" ]; then
  echo "!! В базе нет применённых миграций. Сначала: 01-roles.sql, затем --migrate." >&2
  exit 1
fi
echo "Миграций в базе: $MIGRATIONS"

EXISTING="$(run_psql -t -A -c 'SELECT count(*) FROM "Results";')"
if [ "$EXISTING" != "0" ] && [ "$FORCE" != "1" ]; then
  echo "!! В базе уже $EXISTING результатов — заливка поверх даст конфликт первичных ключей." >&2
  echo "!! Это не пустая база. Если так и задумано, перезапусти с --force." >&2
  exit 1
fi

echo "Заливаю $DUMP ($(du -h "$DUMP" | cut -f1))..."

# Часть таблиц засевают сами миграции через HasData (Categories, Countries, Styles,
# PointRulesClubs/Entries, Sys_RecordIssues). Локальные версии богаче — там же лежат
# правила очков и категории, заведённые руками. Дамп их ЗАМЕНЯЕТ, поэтому перед
# заливкой чистим ровно те таблицы, что перечислены в seed-tables.txt.
# DELETE, а не TRUNCATE CASCADE: cascade снёс бы и таблицы вне списка (например
# пользовательские медиа и фавориты), а этого мы не просили.
TABLE_LIST="$SCRIPT_DIR/seed-tables.txt"
[ -f "$TABLE_LIST" ] || { echo "Не найден $TABLE_LIST" >&2; exit 1; }
CLEAR_SQL=""
while IFS= read -r line; do
  line="${line%%#*}"
  line="$(echo "$line" | tr -d '[:space:]')"
  [ -n "$line" ] && CLEAR_SQL="$CLEAR_SQL"$'\n'"DELETE FROM \"$line\";"
done < "$TABLE_LIST"

# ── Одна транзакция: пролог + дамп + эпилог ────────────────────────────────
{
  cat <<'PROLOGUE'
BEGIN;

-- Запоминаем, какие внешние ключи были НЕотложенными, чтобы вернуть ровно их.
-- Имена храним схемо-квалифицированными: дамп pg_dump сбрасывает search_path в пустой,
-- и после него неквалифицированное имя таблицы уже не резолвится.
CREATE TEMP TABLE _swimm_fk_revert ON COMMIT DROP AS
SELECT format('%I.%I', n.nspname, c.relname) AS tbl, con.conname
  FROM pg_constraint con
  JOIN pg_class c     ON c.oid = con.conrelid
  JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE con.contype = 'f'
   AND n.nspname = 'public'
   AND NOT con.condeferrable;

DO $$
DECLARE r record;
BEGIN
    FOR r IN SELECT * FROM _swimm_fk_revert LOOP
        EXECUTE format('ALTER TABLE %s ALTER CONSTRAINT %I DEFERRABLE INITIALLY DEFERRED',
                       r.tbl, r.conname);
    END LOOP;
END $$;

SET CONSTRAINTS ALL DEFERRED;
PROLOGUE

  echo "-- Освобождаем таблицы, которые заменяет дамп (сид миграций и старые данные)."
  echo "$CLEAR_SQL"

  cat "$DUMP"

  cat <<'EPILOGUE'

-- Дамп оставил после себя пустой search_path — возвращаем рабочий.
SET search_path = public;

-- Здесь срабатывают все отложенные проверки. Если данные несогласованны —
-- падаем ДО коммита, и в базе не остаётся ничего.
SET CONSTRAINTS ALL IMMEDIATE;

DO $$
DECLARE r record;
BEGIN
    FOR r IN SELECT * FROM _swimm_fk_revert LOOP
        EXECUTE format('ALTER TABLE %s ALTER CONSTRAINT %I NOT DEFERRABLE', r.tbl, r.conname);
    END LOOP;
END $$;

COMMIT;
EPILOGUE
} | run_psql -q -f -

# ── Последовательности ─────────────────────────────────────────────────────
# pg_dump кладёт setval в дамп, но ошибиться тут дорого: разъехавшаяся
# последовательность — это конфликт первичного ключа на первой же вставке в
# проде. Поэтому пересчитываем от фактического максимума и проверяем.
echo "Сверяю последовательности..."
run_psql -q -c "
DO \$\$
DECLARE r record; mx bigint; cur bigint;
BEGIN
    FOR r IN
        SELECT c.relname AS tbl, a.attname AS col, pg_get_serial_sequence(quote_ident(c.relname), a.attname) AS seq
          FROM pg_class c
          JOIN pg_namespace n ON n.oid = c.relnamespace
          JOIN pg_attribute a ON a.attrelid = c.oid
         WHERE n.nspname = 'public' AND c.relkind = 'r' AND a.attidentity <> ''
    LOOP
        IF r.seq IS NULL THEN CONTINUE; END IF;
        EXECUTE format('SELECT coalesce(max(%I), 0) FROM %I', r.col, r.tbl) INTO mx;
        SELECT last_value INTO cur FROM pg_sequences
         WHERE schemaname = 'public' AND sequencename = split_part(r.seq, '.', 2);
        IF mx > coalesce(cur, 0) THEN
            PERFORM setval(r.seq, mx);
            RAISE NOTICE 'seq % -> %', r.seq, mx;
        END IF;
    END LOOP;
END \$\$;"

echo ""
echo "Готово. Что получилось:"
run_psql -c "
SELECT 'Results' AS tbl, count(*) FROM \"Results\"
UNION ALL SELECT 'Swimmers', count(*) FROM \"Swimmers\"
UNION ALL SELECT 'Competitions', count(*) FROM \"Competitions\"
UNION ALL SELECT 'Clubs', count(*) FROM \"Clubs\"
UNION ALL SELECT 'Records', count(*) FROM \"Records\"
UNION ALL SELECT 'NormativeStandards', count(*) FROM \"NormativeStandards\"
UNION ALL SELECT 'RelayMembers', count(*) FROM \"RelayMembers\"
ORDER BY 2 DESC;"

echo "Дальше: войти на сайте через Google, затем --grant-admin <email>,"
echo "и прогнать реестр проверок на /Admin/Health."
