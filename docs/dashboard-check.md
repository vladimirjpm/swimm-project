# Проверка дашборда: что агент чинит сам, что несёт Владу

Ранбук скилла `/myswimm-dashboard-check`. Влад запускает его, когда хочет «пройтись по дашборду»:
агент смотрит то же, что показывает `/Admin` прода, **сам чинит то, что чинится штатной
командой без решения человека**, а остальное приносит одним списком с готовым вопросом.

Решение Влада 18.11.2026: запуск `/myswimm-dashboard-check` — это и есть его просьба писать в прод
для действий из колонки «сам» таблицы ниже. Всё, чего в этой колонке нет, — только после его «да»
в чате (правило CLAUDE.md «агент без явной просьбы в ProdAdmin не пишет» для остального в силе).

Карта блоков дашборда — [admin-pages/index.md](admin-pages/index.md); реестр проверок —
[admin-pages/health.md](admin-pages/health.md); что делать с кривыми данными —
[data-integrity.md](data-integrity.md) (чек-лист «вижу кривые данные»).

---

## 0. Как агент смотрит дашборд без входа

Страница `/Admin` и `GET /api/admin/dashboard/status` требуют входа. Агенту он не нужен: те же
числа лежат в прод-базе, а реестр проверок пересчитывается консолью.

- **Чтение прода** — `node server/db/prod-sql.mjs read -c "…"` (только чтение, разрешено всегда).
- **Консоль против прода** — сборка `origin/master` в отдельной папке, прод-настройки из основного
  чекаута (там лежит gitignored `appsettings.ProdAdmin.Local.json`, в worktree его нет):

```bash
# свежий master в своей папке (не основной чекаут — там соседи переключают ветки)
git -C C:/_Proj/swimm-project fetch -q origin
test -d C:/_Proj/swimm-dashcheck || git -C C:/_Proj/swimm-project worktree add --detach C:/_Proj/swimm-dashcheck origin/master
git -C C:/_Proj/swimm-dashcheck checkout -q --detach origin/master

# сборка в свою папку: запущенный ProdAdmin держит Debug-сборку основного чекаута (build-lock)
dotnet build C:/_Proj/swimm-dashcheck/server/Swimm.API -v q -nologo

# любая консольная команда против прода
PROD="dotnet C:/_Proj/swimm-dashcheck/server/Swimm.API/bin/Debug/net10.0/Swimm.API.dll \
  --environment ProdAdmin --contentRoot C:/_Proj/swimm-project/server/Swimm.API"
$PROD --check-data
```

⚠ В логе будет предупреждение «ProdAdmin запущен с ветки …, а не с master» — ветку он читает из
`--contentRoot` (основной чекаут), а код в `swimm-dashcheck` — master. Это ложная тревога.
⚠ Перед командой, которая пишет, сверь, что прод на том же коде: `/version` = `origin/master`.
Если сайт отстал, а в master новая миграция, — команда новой сборки может ждать колонку, которой
на проде нет. Тогда не чини, а принеси Владу пункт «выкатить master» (раздел 3).

## 1. Что собрать (порядок)

1. **Сайт:** `curl -s https://swimhub.co.il/version` и `/readyz`. SHA сравнить с `origin/master`,
   `"database":"up"`.
2. **Свежий диагноз:** `$PROD --check-data` (читающий, пишет только находки — `Sys_DataCheck*`).
   Без этого шага картина — на дату последнего прогона, а она бывает недельной.
3. **Сводка реестра** (Severity: 0 info, 1 warning, 2 error; `Resolution` NULL = открыта):

```sql
SELECT s."CheckId", s."Severity", s."Total", s."Failed",
  (SELECT count(*) FROM "Sys_DataCheckFindings" f WHERE f."CheckId"=s."CheckId" AND f."Resolution" IS NULL) AS open,
  (SELECT count(*) FROM "Sys_DataCheckFindings" f WHERE f."CheckId"=s."CheckId" AND f."Resolution"='accepted') AS accepted
FROM "Sys_DataCheckStates" s WHERE s."Total" > 0 OR s."Failed" ORDER BY s."Severity" DESC, s."Total" DESC;
```

   Строки открытых находок по нужной проверке:
   `SELECT "Id","EntityId","FixEntityId","SubjectName",left("Message",140),"Link","FixKind" FROM "Sys_DataCheckFindings" WHERE "CheckId"='…' AND "Resolution" IS NULL;`
4. **Фоновые службы:** `SELECT "Name","LastAttemptAt","LastSuccessAt","LastError" FROM "Sys_JobRuns";`
   и сбои за сутки: `SELECT "Name","StartedAt",left("Error",160) FROM "Sys_JobRunLog" WHERE NOT "Success" AND "StartedAt" > now() - interval '24 hours';`
   ⚠ Расписания на проде крутятся **только пока запущен ProdAdmin** (В10/В17): «просрочено» при
   выключенном ProdAdmin — не поломка, а факт, который надо сказать Владу одной строкой.
5. **Кандидаты на смену клуба** (Р98): `SELECT "Verdict", count(*) FROM "Sys_SwimmerClubCandidates" GROUP BY 1;`
6. **Жалобы на медиа:** `SELECT "Status", count(*) FROM "Sys_MediaReports" GROUP BY 1;`
7. **Свежесть рекордов:** последняя проверка каждого источника —
   `SELECT DISTINCT ON ("Source") "Source","CheckedAt","Outcome","AddedCount","ChangedCount","AppliedAt","Error" FROM "Sys_RecordSourceChecks" ORDER BY "Source","CheckedAt" DESC;`
   «Пора проверить» — старше 7 дней, ни разу или упала последняя попытка; «ждёт Apply» — есть
   `Added/Changed`, а `AppliedAt` пуст.
8. **Локальная база** (только если дальше работа с данными локально): отстаёт ли от прода —
   Warning в логе локального API / жёлтая полоса админки.

Блоки, которые агент не видит без входа, — **Кэш** (`/Admin/Cache`) и **App Insights**: их
не проверяем, а в отчёте пишем «не смотрел». App Insights при нужде — `az monitor app-insights query`
по `swimhub-ai` (docs/plans/application-insights-plan.md), только чтение.

## 2. Что агент чинит сам

Каждое действие: **сначала dry-run, прочитать план, потом запись**. План выглядит не так, как
ожидалось (в разы больше строк, не те сущности, ошибка) — **не применять**, перенести в раздел 3.
После всех правок — повторный `$PROD --check-data` и сравнение «было → стало».

| Находка / сигнал | Действие | Почему без Влада |
|---|---|---|
| `swimmers.en-name-mismatch` открытые | `$PROD --fix-en-names` (dry-run, читает карточки loglig, 1 с на пловца) → `$PROD --fix-en-names --apply --ids <id тех, где план чинит>` | планировщик чинит только уверенные случаи, аудит `swimmer.en-name-fix` на строку; прозвища и вторые имена он сам оставляет — их «Принять» решает Влад (раздел 3) |
| `clubs.empty` | dry-run у команды нет — план = открытые находки `clubs.empty` (SQL выше); затем `$PROD --delete-empty-clubs` | клуб без пловцов и результатов — мусор парсера; сервис сам перепроверяет предикат и отказывает склеенным, приёмникам, псевдо, клубам из избранного; аудит `club.delete-empty-all` |
| цепочки склеек клубов (`Clubs.MergedIntoId` указывает на склеенный) | `$PROD --flatten-club-merges --dry-run` → без `--dry-run` | только перевешивает надгробия на конечный канон, витрина не меняется (`ResolveClubIdAsync` и так шёл по цепочке) |
| источник рекордов «пора проверить» (старше 7 дней / сбой) | `$PROD --records-check` (fetch + дифф, без Apply) | только читает источник и пишет строку журнала `Sys_RecordSourceChecks`; Apply — нет (раздел 3) |
| `swimmer-club-check` давно не шёл, кандидаты `pending`/`old` | `$PROD --refresh-swimmer-clubs --dry-run` → без `--dry-run` | меняет клуб только по подтверждению loglig (Р98), каждая смена — в журнале `Sys_SwimmerClubChanges` с откатом через `/Admin/Swimmers`; `archive`, другой клуб, нет loglig-id — не трогает |
| локальная база отстаёт от прода | `/db-sync-from-prod` | пишет только в локальную |
| `Failed = true` у проверки (проверка упала) | прочитать текст ошибки в её находке, найти причину в коде; если это наш баг — починить в коде на ветке (без мержа) | диагноз, а не данные |

**Не чинит сам никогда**, даже если «очевидно»: склейки пловцов и клубов (необратимо),
удаление пловцов и соревнований, `--repull`, Apply рекордов, «Принять как есть», пол пловца
(`results.no-gender`, `results.gender-vs-card`), любой `prod-sql.mjs apply`, деплой и миграции.

## 3. Что несёт Владу (одним списком)

Формат: по пункту на строку — **что**, **сколько**, **что предлагаю**, ссылка на админку
(`http://localhost:5078/Admin/...` в ProdAdmin или `Link` находки). Если для пункта уже есть
готовая команда — приложить её dry-run, чтобы «да» стоило одного слова.

| Находка / сигнал | Что подготовить |
|---|---|
| `clubs.dedup-sure`, `swimmers.dedup-sure` | пары с именами и числом результатов; для клубов same-name — вывод `$PROD --merge-same-name-clubs` (dry-run). Склейка — на «да»: `--merge-same-name-clubs --apply` или `/Admin/Clubs` / `/Admin/Swimmers` |
| `results.no-gender` | имя, ссылка на loglig-карточку, если есть `LogligId`; пол ставится кнопкой ♂/♀ на `/Admin/Health` |
| `results.gender-vs-card` | расклад «в карточке X, заплывов Y из Z» из `Message`; ⚠ «Выровнять пол» переписывает протокол — только на «да» |
| `results.exact-duplicate`, `results.merged-sessions`, `import.reconciliation-mismatch` (Error) | соревнование, что не сошлось; путь лечения по [data-integrity.md](data-integrity.md) (обычно переимпорт); `--repull` — только на «да», по [pull-to-prod.md](pull-to-prod.md) |
| `results.official-club-points-mismatch` | соревнование, наши/официальные очки; пояснение или правило — решение Влада (`/Admin/PointsRules`) |
| `entries.no-show-unmatched` | сколько и по каким соревнованиям; обычно ждёт протокола |
| `swimmers.en-name-mismatch`, что `--fix-en-names` оставил | имя HE/EN и почему план не тронул; «Принять» или правка — Влад |
| кандидаты смены клуба `archive` / другой клуб / нет loglig-id | до 10 пловцов на причину, ссылка `/Admin/Swimmers?club=<id>` |
| жалобы на медиа (`Sys_MediaReports` открытые) | сколько, ссылка `/Admin/MediaReports` — решает только человек |
| рекорды «ждёт Apply» | дифф из журнала; Apply — на «да», и тогда по правилу 11 [pre-push-rules.md](pre-push-rules.md) (архив источника, `SHA256SUMS.txt`, итог в data-integrity) |
| сайт отстал от `origin/master` | список коммитов `git log --oneline <prod SHA>..origin/master`, есть ли миграции; выкат — на «да» по [deploy.md](deploy.md) |
| службы просрочены / падают | ProdAdmin выключен → одной строкой; падает → причина из `Error` и что предлагаю |
| `swimmers.orphans`, `competitions.empty` | число; удаление — кнопками в ProdAdmin, только на «да» |

## 4. Отчёт

В конце — короткий отчёт в чате, три части:

1. **Починил сам:** действие → было/стало (числа из повторного `--check-data`).
2. **Нужно твоё решение:** список из раздела 3, каждый пункт с готовым предложением.
3. **Не смотрел / не смог:** Кэш, App Insights, что упало и почему.

Строку о прогоне — в журнал ниже (дата, что починено, что оставлено). Решение по данным,
принятое Владом в ответ на список, — в [data-integrity.md](data-integrity.md), как всегда.

## Журнал прогонов

| Дата | Починено само | Оставлено Владу |
|---|---|---|
| 18.11.2026 (режим отчёта, прогон #123) | ничего: `clubs.empty` 1255/1259 не удалены — на них ссылается `Sys_SwimmerClubChanges` (FK нет, удаление оборвало бы журнал смен и откат) | 4 error (2 сверки импорта, 1 полный дубль, 1 склейка сессий), 16 пар дублей пловцов (8 — гереш `‘` U+2018 против `׳`), 1 пара клубов (суффикс «/ SW»), 13 без пола, 12 расхождений очков, 6 неявок, 33 кандидата смены клуба |
| 18.11.2026 (после PR #220, прогон #124) | код: Р99–Р102 выкачены (`fc7508a6`); данные с «да» Влада: 1819 фантом помечен (Р103), Лига 5 — `--audit-imports --id 98` (501 = 501). Закрыто 5 находок, открытых error — 0 | 16 пар дублей пловцов, 1 пара клубов, 13 без пола, 12 расхождений очков, 6 неявок, 33 кандидата смены клуба |
| 18.11.2026 (склейки по таблице, «да» Влада) | `--merge-swimmers --apply` 15 пар (7003←64732 … 64804←64985); «Не дубли» 8131/64724 (два разных יבסייב, comp 1810); клуб 1471→441; сброс кэша. Дублей пловцов и клубов — 0 | всплыло: `swimmers.two-clubs-in-competition` у 60635 (comp 1834) — клуб 1262 «Bnei Herzeliya» = 439 «בני הרצליה» (NameEn совпадает), дубль клуба, который дедуп не видит (сравнивает Name, не NameEn); 13 без пола, 12 очков, 6 неявок, 33 кандидата |
| 18.11.2026 («да» Влада) | клуб 1262 «Bnei Herzeliya» → 439 «בני הרצליה» (+ 1359 перевешен на 439), цепочек 0; `two-clubs-in-competition` закрыта. Открытых error — 0 | 13 без пола, 12 расхождений очков, 6 неявок, 33 кандидата смены клуба; код: дедуп клубов не сравнивает `Name` с `NameEn` канона |
| 18.11.2026 (пол, «да» Влада) | `results.no-gender` 13 → 0: пол по карточкам loglig (64621 м, 64655 ж, 64716 м, 64805 ж) кнопкой fix-gender; ברנשטיין לאה — двойник 64764 (год 2046) склеен в 7351 (1946, ж, loglig 112959), 6 строк ж | 6 строк 7351 в 1819/1842/1843: возраст «−21», категория «mix-0» из файла с годом 2046 — сверить с PDF мастерса и перезабрать / поправить; 12 расхождений очков, 6 неявок, 33 кандидата смены клуба |
| 18.11.2026 (ברנשטיין לאה, «да» Влада) | возраст 79 в 6 строках 7351 (SQL `2026-11-18-bernstein-age-79.sql`, local → apply); импорт узнаёт год ±100 при том же имени и клубе (PR #221, Р104) | Владу: попросить федерацию исправить год в loglig 112959 (2046 → 1946) |
