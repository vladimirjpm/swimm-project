#!/usr/bin/env bash
# ============================================================================
# Снимает дамп бизнес-данных для наполнения прода (решение Р3).
#
# Только СТРОКИ, без схемы: схему в проде создают миграции, и она остаётся
# единственным источником правды. Список таблиц — seed-tables.txt (там же
# написано, что НЕ переносится и почему; персональных данных в дампе нет).
#
#   ./dump-seed.sh                          # из локального docker-контейнера
#   ./dump-seed.sh -o /tmp/seed.sql         # в конкретный файл
#   PGHOST=... PGUSER=... PGDATABASE=... ./dump-seed.sh --direct
#
# --direct — брать pg_dump с хоста вместо docker exec (для не-локальной БД).
# Пароль — через ~/.pgpass или PGPASSWORD, в аргументах его не передаём.
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TABLE_LIST="$SCRIPT_DIR/seed-tables.txt"

CONTAINER="${SWIMM_PG_CONTAINER:-swimm-postgres}"
PGUSER="${PGUSER:-swimm}"
PGDATABASE="${PGDATABASE:-swimm}"
OUT="$SCRIPT_DIR/seed-data.sql"
DIRECT=0

while [ $# -gt 0 ]; do
  case "$1" in
    -o|--out) OUT="$2"; shift 2 ;;
    --direct) DIRECT=1; shift ;;
    -h|--help) sed -n '2,18p' "$0"; exit 0 ;;
    *) echo "Неизвестный аргумент: $1" >&2; exit 2 ;;
  esac
done

[ -f "$TABLE_LIST" ] || { echo "Не найден $TABLE_LIST" >&2; exit 1; }

# Список таблиц: срезаем комментарии (и хвостовые тоже) и пустые строки.
TABLES=()
while IFS= read -r line; do
  line="${line%%#*}"
  line="$(echo "$line" | tr -d '[:space:]')"
  [ -n "$line" ] && TABLES+=("$line")
done < "$TABLE_LIST"

[ ${#TABLES[@]} -gt 0 ] || { echo "Список таблиц пуст" >&2; exit 1; }
echo "Таблиц в дампе: ${#TABLES[@]}"

ARGS=(--data-only --no-owner --no-privileges --no-comments)
for t in "${TABLES[@]}"; do ARGS+=(--table="public.\"$t\""); done

if [ "$DIRECT" = "1" ]; then
  command -v pg_dump >/dev/null || { echo "pg_dump не найден в PATH" >&2; exit 1; }
  pg_dump -U "$PGUSER" -d "$PGDATABASE" "${ARGS[@]}" > "$OUT"
else
  docker exec -i "$CONTAINER" pg_dump -U "$PGUSER" -d "$PGDATABASE" "${ARGS[@]}" > "$OUT"
fi

# Сторож: в дампе не должно быть ни одной таблицы с персональными данными.
# Дешевле поймать здесь, чем обнаружить в проде.
LEAKED="$(grep -oE 'COPY public\."(Sys_(AppUsers|AppUserRoles|UserExternalLogins|UserLocalCredentials|UserSecurityTokens|UserLoginHistory|UserFavorites|UserMedia|UserMediaPublications|UserReactions|AdminAudit|HubGroup[A-Za-z]*|Training[A-Za-z]*))"' "$OUT" || true)"
if [ -n "$LEAKED" ]; then
  echo "!! В дампе оказались таблицы с персональными данными:" >&2
  echo "$LEAKED" >&2
  rm -f "$OUT"
  exit 1
fi

ROWS="$(grep -c '^COPY public\.' "$OUT" || true)"
echo "Готово: $OUT"
echo "  размер:        $(du -h "$OUT" | cut -f1)"
echo "  секций COPY:   $ROWS из ${#TABLES[@]} (пустые таблицы pg_dump тоже выводит)"
echo ""
echo "Дальше на проде: 01-roles.sql -> --migrate -> 02-grants.sql -> restore-seed.sh"
