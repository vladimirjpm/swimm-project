#!/usr/bin/env node
// Отбор ОДНОГО ДНЯ из JSON разбора протокола — для соревнования, которое ещё идёт
// (docs/pull-to-prod.md, путь Б). PDF loglig у идущего старта печатает ВСЕ события: непроплытые —
// строками без времени (time_fail=true), и всем строкам ставит последнюю дату диапазона. Импорт
// такого файла как есть завёл бы сотни ложных неявок и сложил бы дни в один.
//
//   node server/db/day-json.mjs <разбор.json>                       — отчёт по всем событиям
//   node server/db/day-json.mjs <разбор.json> --date 05/11/2026 \
//        --disciplines "100|individual_medley,50|freestyle,4X50|freestyle" \
//        [--pool 25m] [--include-unfinished] --out <день.json>
//
// <разбор.json> — из `--repull <discoveredId> --dry-run --json-out <файл>`.
// --disciplines — дисциплины ЭТОГО дня по стартовому протоколу (distance|style; у эстафеты
// дистанция «4X50»); пол не указывается — берутся все секции дисциплины (мальчики, девочки, «כללי»).
// Событие считается проплытым, если в его ПОСЛЕДНЕМ заплыве есть хоть одно время: так эстафета,
// у которой проплыт только первый заплыв, в файл не попадёт (--include-unfinished — взять всё равно).
//
// Эстафеты: PDF сводит ВСЕ эстафеты дисциплины за день (все полосы и оба пола) в одну секцию
// без пола, места сквозные. Проплыто ли — смотреть по стартовому протоколу (сколько команд в
// каждой полосе), а не по этой секции. Если проплыты все команды с временем, а NS — это полосы,
// которые ещё не плыли: --relays-timed-only берёт только команды с временем. После импорта
// обязательно `--pull-relays <discoveredId>` (пол, полоса, место — со страниц событий loglig).
import fs from 'node:fs';

const args = process.argv.slice(2);
const opt = name => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };
const input = args[0];
if (!input || input.startsWith('--')) {
  console.error('Usage: node server/db/day-json.mjs <parsed.json> [--date dd/MM/yyyy --disciplines "50|freestyle,..." --out day.json] [--pool 25m] [--include-unfinished]');
  process.exit(1);
}

const rows = JSON.parse(fs.readFileSync(input, 'utf8'));
const hasTime = r => !r.time_fail && /[1-9]/.test(r.time ?? '');
const discKey = r => `${r.event_style_len}|${r.event_style_name}`;

// Событие = заголовок протокола (дисциплина × секция пола/возраста).
const events = new Map();
for (const r of rows) {
  const e = events.get(r.event) ?? { disc: discKey(r), relay: !!r.is_relay, rows: [] };
  e.rows.push(r);
  events.set(r.event, e);
}
for (const e of events.values()) {
  const lastHeat = Math.max(...e.rows.map(r => r.heat ?? 0));
  e.timed = e.rows.filter(hasTime).length;
  e.finished = e.rows.some(r => (r.heat ?? 0) === lastHeat && hasTime(r));
}

const wanted = opt('--disciplines')?.split(',').map(s => s.trim()).filter(Boolean);
const date = opt('--date');
const out = opt('--out');
const pool = opt('--pool');
const includeUnfinished = args.includes('--include-unfinished');
const relaysTimedOnly = args.includes('--relays-timed-only');

// Сводка по дисциплинам: сколько событий, строк, с временем, проплыто ли.
const byDisc = new Map();
for (const [name, e] of events) {
  const d = byDisc.get(e.disc) ?? { events: 0, rows: 0, timed: 0, finished: 0, names: [] };
  d.events++; d.rows += e.rows.length; d.timed += e.timed; d.finished += e.finished ? 1 : 0;
  if (!e.finished && e.timed > 0) d.names.push(name);
  byDisc.set(e.disc, d);
}
console.log('дисциплина'.padEnd(26), 'событий', 'строк', 'с врем.', 'проплыто событий');
for (const [disc, d] of byDisc) {
  const mark = wanted ? (wanted.includes(disc) ? '→' : ' ') : ' ';
  console.log(`${mark} ${disc.padEnd(24)} ${String(d.events).padStart(7)} ${String(d.rows).padStart(5)} ${String(d.timed).padStart(7)}   ${d.finished}/${d.events}`
    + (d.names.length ? `  ⚠ идёт: ${d.names.join('; ')}` : ''));
}

if (!out) process.exit(0);
if (!date || !wanted?.length) {
  console.error('Для --out нужны --date и --disciplines');
  process.exit(1);
}
const unknown = wanted.filter(d => !byDisc.has(d));
if (unknown.length) {
  console.error(`Нет в разборе: ${unknown.join(', ')} (ключ — distance|style, как в таблице выше)`);
  process.exit(1);
}

const picked = [];
const skipped = [];
for (const [name, e] of events) {
  if (!wanted.includes(e.disc)) continue;
  if (e.relay && relaysTimedOnly) {
    for (const r of e.rows.filter(hasTime)) picked.push({ ...r, date, ...(pool ? { pool_type: pool } : {}) });
    if (e.timed < e.rows.length) skipped.push(`${name}: без времени ${e.rows.length - e.timed} команд (не плыли)`);
    continue;
  }
  if (!e.finished && !includeUnfinished) { skipped.push(`${name} (${e.timed}/${e.rows.length} с временем)`); continue; }
  for (const r of e.rows) picked.push({ ...r, date, ...(pool ? { pool_type: pool } : {}) });
}
fs.writeFileSync(out, JSON.stringify(picked));
const fails = picked.filter(r => !hasTime(r));
console.log(`\nЗаписано ${picked.length} строк (${picked.length - fails.length} с временем, ${fails.length} без: DQ/NS протокола) → ${out}`);
if (skipped.length) console.log(`Пропущено как непроплытое: ${skipped.join('; ')}`);
