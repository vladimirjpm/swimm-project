# Прод: как устроен, как выкатывать, что делать, если сломалось

Сайт — **https://swimhub.co.il** (запущен 15.10.2026). Короткая памятка на каждый день. Как его
создавали и почему так — [plans/azure-deploy-plan.md](plans/azure-deploy-plan.md); команды создания с
нуля — [plans/azure-a7-commands.md](plans/azure-a7-commands.md).

## Что где

| | |
|---|---|
| Сайт | Azure App Service `swimhub` (группа `swimm-rg`, Israel Central), 1 инстанс B1 Linux |
| База | Azure PostgreSQL `swimhub-pg` (PG 16, B1ms), база `swimm`; бэкапы — 14 дней |
| Домен, DNS, почта входящая | Cloudflare (все записи сайта — DNS only, серое облако) |
| Письма с сайта | Resend, `noreply@swimhub.co.il` |
| Вход | Google OAuth: прод-клиент `SwimHub prod`, dev-клиент `SwimProjectOAuth2` (localhost) |
| Админка | **на проде нет** (решение В17). Кнопка **swimm-api-prodadmin** — локальная админка против прод-базы, красная полоса «PRODUCTION DATABASE» |
| Секреты | менеджер паролей Google; на машине — `server/Swimm.API/appsettings.ProdAdmin.Local.json` (в git не попадает) |

**Главная база — прод.** Всё, что должно оказаться на сайте, делается в ProdAdmin. Локальная база —
копия, обновляется синхронизацией **прод → локально** (`/db-sync-from-prod`). **Обратно, локальная →
прод, данные не возят никогда:** заливка стёрла бы аккаунты, избранное, группы и медиа пользователей.

## Выкатить код

1. Изменения влиты в `master` (CI зелёный).
2. Если в изменениях есть миграции — **сначала** накатить их на прод: кнопка `swimm-api-prodadmin`
   останови, затем из master `dotnet run --project server/Swimm.API --launch-profile ProdAdmin -- --migrate`.
3. GitHub → Actions → «Деплой на Azure App Service» → Run workflow (`master`), или
   `gh workflow run azure-deploy.yml --ref master`.
4. Проверка: `https://swimhub.co.il/version` показывает новый SHA (подменяется через 1–3 минуты после
   зелёного прогона), `/readyz` — `"database":"up"`.

## Работа с данными

- **Обычное** (импорт соревнования, правка пловца, склейка пары, модерация) — сразу в ProdAdmin.
  Мост кэша показывает правку на сайте сразу.
- **Рискованное** (`--repull`, массовые склейки и чистки, странный протокол) — сначала
  `/db-sync-from-prod`, сделать операцию **локально**, посмотреть сайт локально; потом повторить в
  ProdAdmin.
- Решения по находкам `/Admin/Health` («принять как есть») — только в ProdAdmin: служебные таблицы
  `Sys_*` не синхронизируются, у каждой базы свои.
- **Разовый SQL на проде** (перенос строк из локальной, точечная правка) — `node server/db/prod-sql.mjs`:
  `read -c "SELECT …"` смотрит (только чтение); запись — файл `BEGIN … COMMIT` в `server/db/private/`,
  сначала `local <file>`, потом `apply <file>` (без репетиции того же файла откажет). Роль рабочая,
  схему не меняет; журнал — `server/db/private/prod-sql-journal.jsonl`. Агент запускает его сам, но
  запись — только по твоей просьбе. Пример: перенос медиа и избранного 16.10.2026.

## Если сломалось

От лёгкого к тяжёлому — бери первый подходящий.

### 1. Неудачный импорт соревнования
В ProdAdmin: `/Admin/Competitions` → удалить соревнование (результаты, эстафеты, промежуточные уходят
с ним, действие пишется в журнал) → `/Admin/Swimmers` → «Сироты» → «Удалить всех сирот» (пловцы,
которых создал только этот импорт) → затянуть заново. Сайт увидит исправленное сразу (мост кэша).

### 2. Выкатили плохой код
Перезапустить деплой на предыдущем коммите:
`gh workflow run azure-deploy.yml --ref <прошлый SHA или тег>`. Пакет подменяется целиком.
⚠ Миграции так не откатываются — если новая миграция уже на проде, старый код может с ней не
работать; тогда чинить вперёд (новым коммитом), а не назад.

### 3. Испорчены данные (массовая операция удалила или склеила не то)
Восстановление базы **на любую минуту за последние 14 дней** — в **новый** сервер, текущий не трогается:

```powershell
# время — UTC, на минуту ДО поломки (Израиль летом UTC+3, зимой UTC+2)
az postgres flexible-server restore -g swimm-rg --name swimhub-pg-restore `
  --source-server swimhub-pg --restore-time "2026-10-20T09:30:00Z"
```

10–30 минут. Восстановленный сервер — копия на ту минуту, с теми же логинами и паролями. Дальше:

- **Точечно (обычно правильно):** забрать с `swimhub-pg-restore` только пострадавшие таблицы/строки и
  вернуть их в `swimhub-pg` (через контейнер `swimm-postgres`: `pg_dump --data-only -t …` с
  восстановленного → `psql` в прод). Пользовательские действия после той минуты сохраняются.
- **Целиком (если испорчено всё):** переключить сайт и ProdAdmin на восстановленный сервер —
  `ConnectionStrings__*` в App Service и в `appsettings.ProdAdmin.Local.json` (хост
  `swimhub-pg-restore.postgres.database.azure.com`); перед этим — firewall восстановленного сервера
  (правила не копируются: домашний IP и адреса сайта, как в runbook §3.3). Всё, что пользователи
  сделали после той минуты, теряется.

После — **удалить временный сервер** (`az postgres flexible-server delete -g swimm-rg -n
swimhub-pg-restore --yes`), он стоит денег. Самая ранняя доступная точка —
`az postgres flexible-server show -g swimm-rg -n swimhub-pg --query backup.earliestRestoreDate`.

### 4. Сайт не открывается
`https://swimhub.co.il/healthz` (жив ли процесс) и `/readyz` (видит ли базу). Лог старта:
`az webapp log download -g swimm-rg -n swimhub --log-file logs.zip` → `LogFiles/*_default_docker.log`.
Перезапуск: `az webapp restart -g swimm-rg -n swimhub`.

## Частые мелочи

- **ProdAdmin или `prodpsql` не подключаются к базе** («Connection refused», «no pg_hba.conf entry») —
  сменился домашний IP. Добавить правило firewall (старое потом удалить):
  `az postgres flexible-server firewall-rule create -g swimm-rg -s swimhub-pg -n home-<дата> --start-ip-address <ip> --end-ip-address <ip>`;
  текущий IP — `(Invoke-RestMethod https://api.ipify.org)`.
- **`az` — только из PowerShell.** Git Bash переписывает аргументы `/home/...` в Windows-пути
  (так однажды сломался путь ключей Data Protection); если всё-таки bash — `MSYS_NO_PATHCONV=1`.
- **Секреты в чат и в git не попадают** — значения вводятся `Read-Host -AsSecureString` (вставка правой
  кнопкой мыши), проверяется только длина.
