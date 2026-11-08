#!/usr/bin/env bash
# Сверка мест всех соревнований с loglig-источником (dry-run, только чтение).
# Аргумент: папка sweep. Пишет summary.tsv: id, имя, строк источника/нашей, сопоставлено,
# «כללי» к правке, медаль меняется, возрастные расхождения, время/год, без пары, статус.
SW="$1"
cd /c/_Proj/swimm-project || exit 1
out="$SW/summary.tsv"
[ -f "$out" ] || printf 'id\tname\tsrc\tdb\tmatched\topen_fix\topen_medal\tage_diff\ttime_diff\tunmatched\tstatus\n' > "$out"
while read -r id; do
  [ -z "$id" ] && continue
  grep -q "^$id	" "$out" && continue          # уже сделано — продолжение после обрыва
  log="$SW/log-$id.txt"
  # Сторож: `timeout` в Git Bash не снимает дерево dotnet на Windows (ночью завис №49 на 8 ч).
  dotnet run --project server/Swimm.API --no-build --launch-profile ProdAdmin -- \
    --pull-places "$id" --report "$SW/report-$id.tsv" > "$log" 2>&1 &
  bpid=$!
  for s in $(seq 1 180); do kill -0 "$bpid" 2>/dev/null || break; sleep 5; done
  if kill -0 "$bpid" 2>/dev/null; then
    taskkill //F //T //PID "$(cat /proc/$bpid/winpid)" > /dev/null 2>&1
    wait "$bpid" 2>/dev/null
    code=124
  else
    wait "$bpid"; code=$?
  fi
  head=$(grep -m1 '^«' "$log")
  name=$(echo "$head" | sed -E 's/^«(.*)»:.*/\1/')
  src=$(echo "$head" | grep -oE 'источника [0-9]+' | grep -oE '[0-9]+')
  db=$(echo "$head" | grep -oE 'у нас [0-9]+' | grep -oE '[0-9]+')
  matched=$(echo "$head" | grep -oE 'сопоставлено [0-9]+' | grep -oE '[0-9]+')
  ofix=$(grep -m1 '«כללי» — место к правке' "$log" | grep -oE 'правке: [0-9]+' | grep -oE '[0-9]+')
  omed=$(grep -m1 '«כללי» — место к правке' "$log" | grep -oE 'меняется у [0-9]+' | grep -oE '[0-9]+')
  age=$(grep -m1 'возрастные секции' "$log" | grep -oE '[0-9]+$')
  tdiff=$(grep -m1 'время или год не совпали' "$log" | grep -oE '[0-9]+$')
  unm=$(grep -m1 'без пары:' "$log" | grep -oE '[0-9]+$')
  status="ok"
  [ $code -ne 0 ] && status="exit$code"
  grep -q 'НЕПРИМЕНИМО' "$log" && status="dup-keys"
  [ -z "$head" ] && status="no-report:$(grep -m1 -E 'Exception|не найдена|нет LogligId|не импортировано' "$log" | cut -c1-120)"
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$id" "$name" "$src" "$db" "$matched" "$ofix" "$omed" "$age" "$tdiff" "$unm" "$status" >> "$out"
  echo "$(date -u +%T) $id $status open=$ofix age=$age time=$tdiff unm=$unm"
done < "$SW/ids.txt"
echo "SWEEP DONE"
