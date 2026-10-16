// prod-sql — SQL на прод-базе для агента и для Влада (решение Влада 16.10.2026).
//
//   node server/db/prod-sql.mjs read  -c "SELECT ..."      чтение на проде (транзакция read-only)
//   node server/db/prod-sql.mjs read  <file.sql>
//   node server/db/prod-sql.mjs local <file.sql>           репетиция на локальной базе (Docker)
//   node server/db/prod-sql.mjs apply <file.sql>           запись на проде
//
// Защиты записи:
//  - apply запускает только файл, который УЖЕ прошёл `local` без ошибок за последние 24 часа
//    (сверка по SHA-256: правка файла после репетиции = новая репетиция);
//  - файл обязан сам держать BEGIN … COMMIT (одна транзакция), psql с ON_ERROR_STOP;
//  - роль — DefaultConnection из appsettings.ProdAdmin.Local.json (рабочая роль сайта, без DDL);
//  - хост обязан быть *.postgres.database.azure.com;
//  - каждый запуск файла пишется в server/db/private/prod-sql-journal.jsonl (папка в .gitignore).
// Пароль уходит в psql через переменную окружения: ни в командной строке, ни в выводе его нет.
// SQL с личными данными (email и т. п.) клади в server/db/private/ — репозиторий публичный.
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const journal = path.join(root, 'server/db/private/prod-sql-journal.jsonl');
const prodConfig = path.join(root, 'server/Swimm.API/appsettings.ProdAdmin.Local.json');
const REHEARSAL_TTL_MS = 24 * 3600 * 1000;

const [mode, a1, a2] = process.argv.slice(2);
const die = (msg) => { console.error(msg); process.exit(2); };
if (!['read', 'local', 'apply'].includes(mode)) die('режим: read | local | apply (см. шапку файла)');

let sql, label;
if (mode === 'read' && a1 === '-c') {
  if (!a2) die('read -c: нужен текст запроса');
  sql = Buffer.from(a2, 'utf8'); label = '-c';
} else {
  if (!a1) die(`${mode}: нужен путь к .sql`);
  sql = fs.readFileSync(a1); label = path.relative(root, path.resolve(a1)).replaceAll('\\', '/');
}
const hash = createHash('sha256').update(sql).digest('hex');
const text = sql.toString('utf8');

const readJournal = () => fs.existsSync(journal)
  ? fs.readFileSync(journal, 'utf8').split('\n').filter(Boolean).map(l => JSON.parse(l)) : [];
const log = (entry) => {
  fs.mkdirSync(path.dirname(journal), { recursive: true });
  fs.appendFileSync(journal, JSON.stringify({ at: new Date().toISOString(), mode, file: label, sha256: hash, ...entry }) + '\n');
};

if (mode !== 'read' && !/^\s*BEGIN\s*;/im.test(text)) die('в файле нет BEGIN; — запись только одной транзакцией');
if (mode !== 'read' && !/^\s*COMMIT\s*;/im.test(text)) die('в файле нет COMMIT;');

if (mode === 'apply') {
  const ok = readJournal().some(e => e.mode === 'local' && e.ok && e.sha256 === hash
    && Date.now() - Date.parse(e.at) < REHEARSAL_TTL_MS);
  if (!ok) die('этот файл (sha256) не проходил `local` за последние 24 ч — сначала репетиция на локальной базе');
}

function psqlArgs(conninfo) {
  return ['exec', '-i', '-e', 'PGPASSWORD', '-e', 'PGOPTIONS', 'swimm-postgres', 'psql',
    ...(conninfo ? [conninfo] : ['-U', 'swimm', '-d', 'swimm']), '-X', '-v', 'ON_ERROR_STOP=1', '-f', '-'];
}

const env = { ...process.env, MSYS_NO_PATHCONV: '1', PGPASSWORD: '', PGOPTIONS: '' };
let args, target;
if (mode === 'local') {
  target = 'local swimm-postgres / swimm';
  args = psqlArgs(null);
} else {
  const cfg = JSON.parse(fs.readFileSync(prodConfig, 'utf8'));
  const kv = Object.fromEntries((cfg.ConnectionStrings?.DefaultConnection ?? '').split(';').filter(Boolean)
    .map(p => { const i = p.indexOf('='); return [p.slice(0, i).trim().toLowerCase(), p.slice(i + 1).trim()]; }));
  const host = kv.host ?? kv.server, db = kv.database, user = kv.username ?? kv['user id'] ?? kv.user;
  if (!host?.endsWith('.postgres.database.azure.com')) die('DefaultConnection смотрит не на Azure — не запускаю');
  target = `PROD ${host} / ${db} / роль ${user}`;
  args = psqlArgs(`host=${host} port=${kv.port ?? 5432} dbname=${db} user=${user} sslmode=require`);
  env.PGPASSWORD = kv.password ?? '';
  if (mode === 'read') env.PGOPTIONS = '-c default_transaction_read_only=on';
}

console.log(`[${mode}] ${target}; ${label}; sha256 ${hash.slice(0, 12)}`);
try {
  const out = execFileSync('docker', args, { input: sql, env, encoding: 'utf8', stdio: ['pipe', 'pipe', 'pipe'] });
  process.stdout.write(out);
  if (label !== '-c') log({ ok: true, tail: out.trim().split('\n').slice(-5) });
} catch (e) {
  process.stdout.write(e.stdout ?? '');
  process.stderr.write(e.stderr ?? String(e));
  if (label !== '-c') log({ ok: false, error: String(e.stderr ?? e).trim().split('\n').slice(-3) });
  process.exit(1);
}
