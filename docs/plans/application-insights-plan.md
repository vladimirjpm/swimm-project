# Application Insights: прод, страница в админке, блок дашборда

> План от 16.11.2026 (просьба Влада). Кода нет, ресурсов Azure нет. Закрывает мелочь В14
> из [azure-deploy-plan.md](azure-deploy-plan.md) §10 — «Нужен ли Application Insights сразу?».
> Его никто не отвергал: при запуске прода 14.10 он был помечен «опционально» и отложен.

## ⏩ Передача Opus 5.5 (16.11.2026) — читать первым

План писал Fable; контекст той сессии кончился, работу ведёт **Opus 5.5 в главной сессии**.
Везде ниже, где исполнитель «Fable», — это теперь ты. Б1 делай сам, а не субагентом; Б2 и Б3 —
Sonnet через `/delegate`, таск-документ пишешь ты после Б1.

**Где мы (18.11).** Весь план закрыт: А3.4 (память «после») снят 18.11 — телеметрия памяти почти не
добавила. Сделано и на проде: А1–А4, Б1–Б4 (PR #197, #198, #201;
прод `ac658b23`, потом `fad34070`). Страница `/Admin/AppInsights` работает в ProdAdmin — ключи
`AppInsights:WorkspaceId`/`ResourceId` вписаны в оба gitignored `*.Local.json`. Первая находка
телеметрии исправлена: сорвавшийся возврат от Google давал 500, теперь — редирект на исходную
страницу (PR #202, `fad34070`). Из §8 отвечено всё. Мерж и деплой делает агент сам
(`gh pr merge --merge` после зелёного CI → `gh workflow run azure-deploy.yml --ref master`).

**После плана — 19.11 (решения Влада в чате).** (1) Быстрые SQL (< 400 мс, `Telemetry:SqlSpanMinMs`)
в Azure не уходят: 18.11 SQL — ~40 % объёма (10 465 строк на 1 336 запросов), при паре тысяч человек
в день лимит 0,15 ГБ кончался бы к утру. Счёт всех SQL сохранён тегами на запросе
(`swimm.db.query_count`/`query_ms`). (2) Метки запросов: `swimm.source` (visitor/bot/warmup/uptime),
`swimm.cold` (первые 10 мин после старта), версия сборки → `AppVersion`; p50/p95 и «Медленные
операции» на странице — по посетителям и роботам без холодных. Причина: 18.11 из 125 запросов > 3 с
82 — в первые 10 мин после старта (21 старт за двое суток). (3) Прогрев после старта на проде
(`StartupWarmupService`, `WarmupPaths`). Находки 18.11 для будущего: 72 % SQL — фоновые службы
(пинг БД ~4 100/сутки, напоминания ~2 400), а не люди; 635 открытий соединения в сутки (p95 2,3 с);
`competition-overview` ~31 SQL за вызов, `records/freshness` ~23, `rsvp` ~29. (4) `pg_stat_statements`
(«да» Влада 19.11): параметры сервера заданы 19.11 ~07:00 UTC без перезапуска — `azure.extensions =
PG_STAT_STATEMENTS`, `pg_stat_statements.track = top`, `track_utility = off` (библиотека в
`shared_preload_libraries` по умолчанию); сбор идёт с этой минуты. Представление создаёт миграция
`AddPgStatStatements` (+ `pg_read_all_stats` роли `swimm_rw`, чтобы видеть текст запросов `swimm_ro`) —
на прод через ProdAdmin `--migrate` (docs/deploy.md п. 2). Смотреть топ:
`node server/db/prod-sql.mjs read -c "SELECT calls, round(total_exec_time) AS total_ms, round(mean_exec_time::numeric,1) AS mean_ms, rows, shared_blks_read, left(regexp_replace(query, '\s+', ' ', 'g'), 120) AS q FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 20"`
(по частоте — `ORDER BY calls DESC`; много `shared_blks_read` — чтение с диска, кандидат на индекс).
(5) Тёплый пул (`NpgsqlPoolDefaults`, оба контекста EF): `Minimum Pool Size=2` и `Keepalive=30`, если
строка их не задала. 18.11 — 635 открытий соединения в сутки (rw 443, ro 192; p95 1,2 с и 4,8 с),
первое после старта — 7,7 с. Проверить через сутки: `CONNECT` в `AppDependencies` (уходят только
≥ 400 мс) должно стать заметно меньше. `Maximum Pool Size` (rw 10, ro 20) — в настройках App
Service; поднимать по данным, когда появятся ожидания свободного соединения.

**Что дальше, по порядку:**
1. ✅ **А3.4** — память «после» снята 18.11: среднее 222 МБ (+7 % к 207), пик 334 МБ — рост в шуме, действий не нужно (см. «Ход А3»).
2. ✅ **А4** — оповещения созданы 17.11 (см. «Сделано 17.11» в А4).
3. ✅ **Б1** — сделан 17.11 (см. «Сделано 17.11» в Б1). Было: новым чатом, сам (не субагентом). До кода прочитать: «Сделано 17.11» (живая проверка —
   там имена таблиц и полей, `Success=false` у 404, ошибки — по `ResultCode` 5xx), §4.1, Б1.
   Пакет чтения — `Azure.Monitor.Query.Logs` 1.0.0 + `Azure.Identity` (`AzureCliCredential`).
   KQL сверять запросами на живых данных: без расширения `az` — REST `https://api.loganalytics.io/v1/workspaces/<customerId>/query`
   с токеном `az account get-access-token --resource https://api.loganalytics.io` (customerId —
   `az monitor log-analytics workspace show -g swimm-rg -n swimhub-logs --query customerId -o tsv`;
   в репо и в чат не писать). Есть реальный трафик прода с 17.11 11:45 UTC.
4. ✅ Б2 + Б3 — Sonnet по [appinsights-page-sonnet.md](../tasks/appinsights-page-sonnet.md), принято 17.11 (см. «Сделано 17.11 (Б2+Б3)»).
5. ✅ Б4 (PR #201, на проде 17.11): `deploy.md` («если сломалось: куда смотреть»), отметка В14, строка ROADMAP, текст о приватности; потом PR 2.

### Сделано 17.11 — А1 (Opus 5.5, «да» Влада в чате)

- `swimhub-logs` — рабочая область Log Analytics, `swimm-rg`, Israel Central, PerGB2018, хранение 30 дней.
  CLI сам зарегистрировал в подписке поставщика `Microsoft.OperationalInsights` (до этого не был).
- `swimhub-ai` — Application Insights, `kind=web`, `IngestionMode=LogAnalytics` на `swimhub-logs`
  (связь сверена), `DisableIpMasking=false`, хранение 90 дней. Строка подключения выдана, нигде не напечатана.
- Расширение `az application-insights` **не ставили**: ресурс создан встроенной `az resource create`:
  ```bash
  WS=$(az monitor log-analytics workspace show -g swimm-rg -n swimhub-logs --query id -o tsv)
  az resource create -g swimm-rg -n swimhub-ai --resource-type microsoft.insights/components \
    --api-version 2020-02-02 --is-full-object --properties "{\"kind\":\"web\",\"location\":\"israelcentral\",\"properties\":{\"Application_Type\":\"web\",\"WorkspaceResourceId\":\"$WS\",\"IngestionMode\":\"LogAnalytics\",\"DisableIpMasking\":false}}"
  ```
  Строку подключения для А3 / ProdAdmin брать так же, без расширения:
  `az resource show -g swimm-rg -n swimhub-ai --resource-type microsoft.insights/components --query properties.ConnectionString -o tsv`.
- **А1.3 дневной лимит — 0,15 ГБ** (§8.2, ответ Влада 17.11): `az monitor log-analytics workspace
  update -g swimm-rg -n swimhub-logs --quota 0.15`; сброс суток — 10:00 UTC. Действует именно лимит
  рабочей области: свой лимит у `swimhub-ai` остался по умолчанию (100 ГБ), данные всё равно ложатся
  в `swimhub-logs`. При достижении приём стоит до сброса — оповещение «Объём» (А4) на 80 %.
- Бюджет в подписке уже есть: `swimhub-monthly`, 60 в месяц — отдельный бюджетный алерт не нужен.

### Сделано 17.11 — А2 (Opus 5.5)

- **Пакеты:** `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0 (net10.0; тянет OpenTelemetry 1.15.x,
  Azure.Core 1.60, MSAL), `Npgsql.OpenTelemetry` 10.0.3 — ровно наш Npgsql 10.0.3. Уязвимостей
  NuGet не показал. Тестам — `Microsoft.AspNetCore.TestHost` 10.0.9.
- **Выборка:** умолчание 1.6.0 — rate-limited 5 трасс/с. Задано явно: `SamplingRatio=1`, `TracesPerSecond=null`.
- **⚠ Главная находка:** `UseAzureMonitor()` (исходник 1.6.0, `OpenTelemetryBuilderExtensions.cs`)
  **сам выключает** встроенную вычистку строки запроса ASP.NET Core и HttpClient — ставит
  `OTEL_DOTNET_EXPERIMENTAL_{ASPNETCORE,HTTPCLIENT}_DISABLE_URL_QUERY_REDACTION=True`, если ключ не
  задан. Без своей защиты `token=…` ушёл бы как есть. Сделано три слоя: ключи = `false` последним
  источником конфигурации (встроенная работает и заменяет все значения на `Redacted`);
  `EnrichWithHttpRequest`/`EnrichWithHttpRequestMessage` — наша вычистка сразу после тегов, раньше
  любого экспортёра; процессор `OnEnd` до `UseAzureMonitor` — для прочих спанов. У `/auth/*` и
  `/signin-google` (OAuth-код) строка запроса режется целиком.
- **Тесты:** `HostingModeTests` (выключатель, имя роли), `TelemetryRedactionTests` (правило + живой
  конвейер TestServer с `token=SEKRET`, в т.ч. с выключенной встроенной вычисткой — мутацией
  проверено, что без нашей вычистки тест падает), `TelemetryFilterTests`, `TelemetryWiringTests`
  (настоящий `UseAzureMonitor`: имя роли переживает детектор App Service при `WEBSITE_SITE_NAME`,
  вычистка остаётся включённой, выборка явная, логи в Azure — Warning+, без строки нет провайдера
  вовсе), `EmailLogMaskTests`. Полный прогон: 3924 зелёных.
- **Email в логах (А2-бис), grep `LogWarning/LogError`:** замаскированы (`i***@gmail.com`)
  `SmtpEmailSender` «Failed to send email» и `LoggingEmailSender` «SMTP не настроен» — оба идут на
  проде. Оставлены: `Admin {User}` в `ClubsAdminController`/`SwimmersAdminController` — только
  админка (ProdAdmin), имя самого Влада; `DevAdminBypass {Email}` — только Development, телеметрии
  там нет. `Include Error Detail` в строках подключения нет; `EnableSensitiveDataLogging` не включён.
- **Statsbeat — ВЫКЛЮЧЕН (Влад, 17.11):** служебная статистика SDK для Microsoft (версия SDK,
  регион, ikey, сколько пакетов ушло — без наших данных), нам пользы ноль. Выключается в коде:
  `AddSwimmTelemetry` ставит `APPLICATIONINSIGHTS_STATSBEAT_DISABLED=true` до `UseAzureMonitor` —
  так и на проде, и в ProdAdmin, настройка App Service не нужна. ⚠ Экспортёр читает переменную
  один раз и кэширует (`DefaultPlatform`, исходник 1.8.3), поэтому порядок важен. Тест
  `Statsbeat_DisabledByAddSwimmTelemetry` ловит событие экспортёра 30 «Statsbeat was disabled»
  (мутацией проверено). `CustomerSdkStats` (статистика потерянной телеметрии — в НАШ ресурс) не трогаем.
- **Для Б1:** актуальный пакет чтения — `Azure.Monitor.Query.Logs` 1.0.0 (старый
  `Azure.Monitor.Query` 1.7.1 ещё живёт).
- **Локальная проверка на живой рабочей области — ✅ 17.11** (после А1). API Release на :5091,
  Development, строка подключения — переменной окружения процесса (подстановкой из `az`, не
  печаталась), роль `swimhub-dev`. Итог, запросами в `swimhub-logs`:
  - запросы дошли за ~1 мин; у `/auth/reset-password`, `/auth/verify-email`, `/signin-google` в
    `Url` строки запроса нет; поиск — `?q=Redacted`, и `competitionId=1581` тоже `Redacted` (встроенная
    вычистка трогает все значения); `search 'LOCALSEKRET17'` по всем таблицам — 0 строк;
  - `/healthz`, `/readyz`, `/version`, `/favicon.ico`, `/css/*` в `AppRequests` не попали;
  - Postgres в `AppDependencies` (`DependencyType=postgresql`, `Target=host | db`) — SQL с
    плейсхолдерами (`@hash`, `@purpose`), значений нет; исключение 500 у `/signin-google` (поддельный
    `state`) — в `AppExceptions`, без кода;
  - **⚠ найдено и починено:** Npgsql вешает на каждый спан событие `received-first-response`, а
    экспортёр делает из него отдельную строку `AppTraces` уровня Verbose — по строке на SQL-запрос.
    Выключено `EnableFirstResponseEvent(false)` (`Swimm.Infrastructure/Data/NpgsqlTracingDefaults.cs`)
    у обоих контекстов EF (`ConfigureDataSource`) и у моста кэша. Повтор: 16 событий → 4, остаток —
    мост (до правки моста) и проверка «локальная база отстаёт от прода» (сырой `NpgsqlConnection`,
    только на старте, только локально). Тест `NpgsqlTracingPgTests` (мутацией проверено, что ловит);
  - в `AppTraces` уходят только Warning+: EF «Compiling a query which loads related collections…»
    и `DevAdminBypass` с email владельца — последний только в Development, на проде этой ветки нет;
  - таблицы есть: `AppRequests`, `AppDependencies`, `AppExceptions`, `AppTraces`, `AppMetrics`;
    поля `AppRoleName`, `Name`, `Url`, `ResultCode`, `Success`, `DurationMs` — как в плане. Для Б1:
    `Success=false` и у 404, ошибки сервера считать по `ResultCode` 5xx; `AppAvailabilityResults` не будет (§8.3);
  - запросы KQL без расширения `az log-analytics` — REST `api.loganalytics.io` с токеном
    `az account get-access-token --resource https://api.loganalytics.io` (помощник был во временной папке).
- Реестр переменных — строка в [azure-deploy-plan.md](azure-deploy-plan.md) §5; образец ключа — в
  `appsettings.ProdAdmin.Local.json.example`.

**Ответов на §8 Влад ещё не дал.** Спроси первым сообщением, одним списком. А2 их не ждёт
(кроме вопроса 1 — имя роли ProdAdmin закладывай в любом случае, включается оно ключом).

**Прочти до кода:** [deploy.md](../deploy.md) («Выкатить код»), [admin-pages/jobs.md](../admin-pages/jobs.md)
и [admin-pages/index.md](../admin-pages/index.md) (образцы страницы и блока),
[pre-push-rules.md](../pre-push-rules.md) (правила 2, 3, 14), `server/Swimm.API/Hosting/HostingMode.cs`,
`server/Swimm.API/Program.cs` (порядок middleware, `/healthz`).

**Что я НЕ проверял — проверь сам, не верь тексту плана:**

- версии и совместимость `Azure.Monitor.OpenTelemetry.AspNetCore` и `Npgsql.OpenTelemetry` с
  .NET 10 / Npgsql 10; какое у пакета умолчание выборки (sampling) в этой версии;
- вычищает ли инструментарий ASP.NET Core значения строки запроса сам — в любом случае свой
  процессор и тест с `token=SEKRET` обязательны, это главный риск всей работы;
- пакет чтения: `Azure.Monitor.Query` или новый `Azure.Monitor.Query.Logs` — взять актуальный;
- имена таблиц рабочей области и поля (`AppRequests.Success`, `ResultCode`, `DurationMs`,
  `AppRoleName`) — запросом на живых данных после А3;
- цены и бесплатные 5 ГБ — по прайсу на день создания; есть ли уже бюджетный алерт в подписке;
- стоит ли расширение `az` для Application Insights (`az extension list`);
- какие `LogWarning` / `LogError` подставляют email (А2-бис) — пройти grep-ом, список в PR.

**Команды А1 — набросок, сверить с `az … --help` перед запуском:**

```bash
az monitor log-analytics workspace create -g swimm-rg -n swimhub-logs -l israelcentral
az monitor app-insights component create --app swimhub-ai -g swimm-rg -l israelcentral --workspace <id рабочей области>
az webapp config appsettings set -g swimm-rg -n swimhub --settings APPLICATIONINSIGHTS_CONNECTION_STRING=<значение>
```

Последняя перезапускает сайт и печатает все настройки в ответ — гасить вывод (`-o none`),
значение брать подстановкой из `az … show`, в чат и файлы не выводить.

**Правила, на которых легко споткнуться:**

- любая запись в подписку Azure — только после «да» Влада на конкретный шаг; чтение (`az … show`,
  `list`) свободно;
- идентификаторы Azure (подписка, рабочая область, строка подключения) — не в репозиторий: он публичный;
- в ProdAdmin без просьбы Влада не писать; деплой жмёт Влад;
- коммит — только по просьбе, своими путями (не `git add -A`), после коммита — только хэш;
  push и PR не предлагать;
- новая фича — мелкими файлами в своей папке, в `Program.cs` только точка входа; тесты предлагать сразу;
- после каждого этапа обновлять таблицу §9; контекст выше 60 % — сказать Владу и передать дальше.

**Как я проверял Azure 16.11** (повторить можно теми же командами):
`az provider show -n microsoft.insights` → в `components` есть Israel Central;
`az provider show -n Microsoft.OperationalInsights` → в `workspaces` тоже;
`az resource list -g swimm-rg -o table` → пять ресурсов, мониторинга нет.

## 0. Зачем

Сейчас об ошибке на проде узнаём от пользователя. Есть только `/healthz`, `/readyz`, `/version`
и логи контейнера архивом (`az webapp log download`, [deploy.md](../deploy.md)). Нет графика
ошибок, медленных запросов, оповещений. Без этого нельзя переносить на прод фоновые джобы
(рекорды, входящие) — их сбои были бы тихими.

Что получаем:

1. Сайт шлёт в Azure запросы, исключения, вызовы Postgres и логи уровня Warning и выше.
2. Письмо Владу, когда растут ошибки или сайт не отвечает.
3. Страница `/Admin/AppInsights` и блок на дашборде — главные цифры без похода в портал Azure.

## 1. Что проверено 16.11

| Что | Итог |
|---|---|
| Регион | `microsoft.insights/components` и `Microsoft.OperationalInsights/workspaces` есть в **Israel Central** — телеметрия остаётся в Израиле, рядом с сайтом и базой (Б14) |
| Группа `swimm-rg` | `swimhub` (App Service), `swimhub-plan`, `swimhub-pg`, два сертификата. Ресурсов мониторинга нет |
| Вход `az` на машине Влада | работает (подписка Pay-As-You-Go) — на нём строится чтение из админки, §4.1 |
| Код | пакетов OpenTelemetry и Azure нет; .NET 10; логи — стандартный `ILogger`, уровень Default = Information |
| ⚠ Токены в адресе | `GET /auth/verify-email?token=…` и `GET /auth/reset-password?token=…` приходят на сервер с одноразовым токеном в строке запроса (`LocalAuthService.cs:100`, `:209`). Без вычистки токен ляжет в телеметрию — §3.3 |

## 2. Решения (предлагаю; спорное — в §8)

- **Только сервер.** Браузерный SDK Application Insights не ставим: это куки и согласие, а
  на сайте дети. Клиентские ошибки — отдельная задача, если понадобится.
- **Пакет `Azure.Monitor.OpenTelemetry.AspNetCore`** (текущий путь Microsoft для .NET), не
  классический `Microsoft.ApplicationInsights.AspNetCore`.
- **Включается наличием строки подключения** `APPLICATIONINSIGHTS_CONNECTION_STRING`. Нет
  переменной — ни одного вызова наружу: разработка, тесты и CI ничего не шлют.
- **Чтение для админки — под входом `az` Влада** (`AzureCliCredential`), без ключей и секретов.
  Админка живёт только на его машине (В17), а `run-prodadmin.ps1` уже ходит в Azure под этим
  входом (файрвол прод-базы).
- **Идентификаторы Azure в репозиторий не попадают** (репо публичный): строка подключения — в
  App Service; id рабочей области и ресурса — в `appsettings.*.Local.json` (gitignored), в
  `.example` — только имена ключей.
- **Дневной лимит приёма** на ресурсе, чтобы счёт не мог вырасти: 5 ГБ в месяц бесплатны на
  платёжный аккаунт, дальше ≈ $2,3 за ГБ (прайс из §9 плана деплоя, перепроверить при создании).

## 3. Часть А — подключение к проду

### А1. Ресурсы Azure

Исполнитель: **Fable** командами `az`, каждая запись в подписку — после «да» Влада в чате.

1. Рабочая область Log Analytics `swimhub-logs`, `swimm-rg`, Israel Central.
2. Application Insights `swimhub-ai` (workspace-based) на ней.
3. Дневной лимит приёма (число — §8, вопрос 2).
4. Строку подключения — сразу в настройки App Service, в чат и в файлы не печатать.
   ⚠ Смена настройки **перезапускает сайт** (несколько секунд). Делать на шаге А3, вместе с деплоем.

Приёмка: `az resource list -g swimm-rg` показывает оба ресурса в `israelcentral`.

### А2. Отправка телеметрии в коде

Исполнитель: **Fable** (граница безопасности и приватности — делегатам не отдаётся).

Файлы: новая папка `server/Swimm.API/Telemetry/` (один файл — одна ответственность),
в `Program.cs` — только вызов `builder.AddSwimmTelemetry()`.

1. Пакеты: `Azure.Monitor.OpenTelemetry.AspNetCore`, `Npgsql.OpenTelemetry` (версии —
   последние стабильные под .NET 10 и Npgsql 10; проверить совместимость до коммита).
2. Включение: только если задана строка подключения. Выключатель — рядом с остальными в
   `HostingMode` (`TelemetryEnabled`), тест в `HostingModeTests`.
3. Имя роли (`service.name`): `swimhub-web` на проде, `swimhub-prodadmin` в ProdAdmin — чтобы
   на странице и в оповещениях различать сайт и админку.
4. Что НЕ шлём: `/healthz`, `/readyz`, `/version`, статику (`/assets`, `/images`, `/css`, `/js`).
   Health-check App Service стучит раз в минуту и забил бы объём.
5. Логи: в Azure уходит Warning и выше. Консоль контейнера остаётся как есть.
6. Выборка (sampling): задать явно, на умолчание пакета не полагаться (оно менялось между
   версиями). Старт — без выборки: трафик мал, а при 100 % видна каждая ошибка. Страховка — лимит А1.
7. Postgres: `AddNpgsql()` — длительность и текст запроса без значений параметров.

### А2-бис. Приватность (обязательная часть А2)

| Риск | Что делаем | Как проверяем |
|---|---|---|
| Токен сброса пароля и подтверждения почты в строке запроса | Значения параметров строки запроса вычищаются до отправки. Новые версии инструментария ASP.NET Core делают это сами — **не верим на слово**: свой процессор-страховка, который режет строку запроса у `/auth/*` целиком | Тест: запрос `/auth/reset-password?token=SEKRET` → в собранной телеметрии строки `SEKRET` нет |
| Поиск по имени в `?q=` | то же правило вычистки значений | тот же тест с `?q=` |
| Email в тексте исключений и логов | Пройти `LogWarning`/`LogError` в `Swimm.Infrastructure` и `Swimm.API` на подстановку email; проверить, что в строке подключения нет `Include Error Detail=true` (иначе Postgres печатает значение ключа в ошибке уникальности) | Список найденного — в PR; спорное — маскировать |
| Кто пользователь | `enduser.id` и имя не ставим | в телеметрии нет поля пользователя |
| IP посетителя | Application Insights по умолчанию IP не хранит — не менять | настройка ресурса `DisableIpMasking` = false |

Тесты А2: `HostingModeTests` (выключатель), `TelemetryRedactionTests` (вычистка),
`TelemetryFilterTests` (health и статика не пишутся). Локальная проверка: Development с временной
переменной окружения и ролью `swimhub-dev` — данные дошли до портала; переменную убрать.

### А3. Деплой и проверка на проде

Исполнитель: **Fable** готовит, **Влад** жмёт деплой (В13 — только по кнопке).

1. PR → master → деплой по [deploy.md](../deploy.md) «Выкатить код». Миграций нет.
2. Настройка `APPLICATIONINSIGHTS_CONNECTION_STRING` в App Service (А1.4) — сайт перезапустится.
3. Через 5 минут: в портале есть запросы роли `swimhub-web`; запрос
   `/auth/reset-password?token=test123` — в телеметрии `test123` нет.
4. Память процесса на B1 (1,75 ГБ) до и после — записать в план.
5. ProdAdmin (если «да» в §8, вопрос 1): ключ в `appsettings.ProdAdmin.Local.json`, имя — в `.example`.

**Ход А3 (17.11):**
- ✅ п. 2 сделан РАНЬШЕ п. 1 («да» Влада): `APPLICATIONINSIGHTS_CONNECTION_STRING` задана в App Service
  10:30 UTC (значение подстановкой из `az resource show … --query properties.ConnectionString`,
  вывод погашен). Сайт перезапустился, к 10:31 `/readyz` = `database: up`. На проде старая сборка
  `57a288e5` — кода телеметрии в ней нет, строку она не читает; включится сама при деплое кода А2.
  Деплой (`azure-deploy.yml`) настройки App Service не трогает — переменная переживёт выкатку.
- ✅ п. 4 «до»: `MemoryWorkingSet` за сутки до 17.11 10:30 — в среднем 207 МБ, пик 375 МБ (из 1,75 ГБ).
  Замер «после» — через сутки после деплоя тем же запросом:
  `az monitor metrics list --resource <id swimhub> --metric MemoryWorkingSet --interval PT1H --offset 24h --aggregation Average Maximum`
  (в Git Bash — с `MSYS_NO_PATHCONV=1`, иначе id ресурса искажается; проще без id:
  `--resource swimhub --resource-group swimm-rg --resource-type Microsoft.Web/sites`).
- ✅ п. 4 «после», замер 18.11 (агент по расписанию): `MemoryWorkingSet` с 17.11 11:45 по 18.11
  11:45 UTC, 24 часовых точки — в среднем **222 МБ** (было 207, **+7 %**), пик **334 МБ** (было 375;
  19 % от 1,75 ГБ) — в первый час после деплоя 11:45, то есть холодный старт. Почасовые средние:
  170–295 МБ; дневной горб 17:45–19:45 UTC 17.11 (295/269/251) и утро 18.11 05:45–07:45 (219–243) —
  это трафик, ночью память спускается до 170–200 МБ, утечки не видно. **Вывод:** рост от телеметрии
  в пределах шума (порог беспокойства — +30 % к среднему или пик к 1 ГБ — далеко), делать ничего не нужно.
- ✅ п. 1: PR #197 влит (`d7a33136`), деплой `gh workflow run azure-deploy.yml --ref master` —
  агент сам (Влад, 17.11: деплой делает агент, «жмёт Влад» в этом плане устарело). На проде с 11:45 UTC.
- ✅ п. 3, 17.11 11:46–11:50 UTC: роль `swimhub-web`, данные через ~2,5 мин, живой трафик (пловцы,
  клубы), 5xx нет. `/auth/reset-password?token=PRODSEKRET17` → в `Url` без строки запроса, `search
  'PRODSEKRET17'` по всем таблицам — 0; поиск — `q=Redacted`; `/healthz`, `/readyz`, `/assets/*` в
  `AppRequests` нет; на 100 зависимостей Postgres — 1 строка `AppTraces` (Warning EF «Skip/Take без
  OrderBy»), событий `received-first-response` нет.
  Мелочь на потом: в `Url` схема `http://` — инструментарий снимает адрес до `UseForwardedHeaders`
  (App Service терминирует TLS). На данные не влияет.
- ✅ п. 5 (§8.1 «да», 17.11): строка подключения записана скриптом в `appsettings.ProdAdmin.Local.json` (gitignored, значение не печаталось). Роль `swimhub-prodadmin` появится при следующем запуске ProdAdmin.

### А4. Оповещения

Исполнитель: **Fable** командами `az` после «да» Влада; адрес почты задаётся в Azure, в репо его нет.

| Оповещение | Условие (старт, подкрутить по факту) |
|---|---|
| Ошибки сервера | запросов с кодом 5xx > 5 за 15 минут |
| Исключения в фоне | исключения роли `swimhub-prodadmin` или джоба напоминаний > 0 за час |
| ~~Сайт не отвечает~~ | **не ставим в Azure** (§8.3, 17.11): уже делает бесплатный UptimeRobot — `/readyz` раз в 5 минут, письмо Владу |
| Объём | приём дошёл до 80 % дневного лимита |

Приёмка: тестовое письмо группы оповещений дошло Владу.

**Сделано 17.11 (Opus 5.5, «да» Влада).** Цены сверены по `prices.azure.com` (Israel Central / West
Europe): правило по журналу с частотой 15 мин и реже — $0,50/мес, метрическое — ≈ $0,10 → **≈ $1,1/мес**.
- `swimhub-alerts` — группа действий (global, short name `swimhub`), email Влада (Gmail, как у
  UptimeRobot), получатель `Enabled`. Тест `az monitor action-group test-notifications create` падает
  «There are no valid receivers» (баг/формат CLI) — не добит; Azure сам шлёт письмо «добавлены в
  группу действий» при создании — **Влад проверяет, что оно пришло**.
- `swimhub-5xx` — метрическое правило App Service `Http5xx` total > 5 за 15 мин, проверка каждые 5 мин, sev 2.
- `swimhub-bg-errors` — правило по журналу раз в час, окно 1 ч, sev 2: `AppExceptions` **и**
  `AppTraces` с `SeverityLevel >= 3` (ловит и LogError без исключения — «Письмо НЕ отправлено») роли
  `swimhub-prodadmin` либо роли `swimhub-web` с пустым `OperationId` (вне запроса — фон). Ошибки
  внутри запросов ловит `swimhub-5xx`. ⚠ `OperationName` пуст и у исключений из запросов — по нему
  фон не отличить, только по `OperationId`/`ParentId`.
- `swimhub-ingestion-80` — правило по журналу раз в час, окно 1 сутки, sev 3:
  `Usage | where IsBillable == true | summarize mb=sum(Quantity) | where mb > 120`, повтор письма не
  чаще раза в 12 ч (`muteActionsDuration`).
- Создано без расширений `az`: правила по журналу — `az rest --method put` на
  `Microsoft.Insights/scheduledQueryRules` (api-version `2023-12-01`, `kind: LogAlert`, заголовок
  `Content-Type=application/json` обязателен), тела JSON собирались во временной папке.
- Сам Azure при создании `swimhub-ai` завёл **«Failure Anomalies - swimhub-ai»** (smart detection,
  бесплатно, своя группа «Application Insights Smart Detection») — оставлено.

## 4. Часть Б — админка

### 4.1. Откуда страница берёт данные

Админка читает рабочую область Log Analytics запросами KQL через `Azure.Monitor.Query` под
входом `az`. Страница про ПРОД-сайт и от базы не зависит — работает и в Development, и в
ProdAdmin, лишь бы был `az login` и ключи в `appsettings.*.Local.json`:
`AppInsights:WorkspaceId`, `AppInsights:ResourceId`.

Три состояния, каждое — понятной карточкой, а не ошибкой:
- **не настроено** — ключей нет: что вписать и куда;
- **нет входа** — `az login` не сделан или просрочен: команда для копирования;
- **работает** — данные; если последней телеметрии больше 15 минут — жёлтое «сайт молчит».

### Б1. Чтение: порт, сервис, запросы, API

Исполнитель: **Opus** (субагент по таск-документу; задача с развилками — схема таблиц,
ошибки доступа, пороги). Приёмка и ревью — **Fable**.

| Слой | Что |
|---|---|
| `Swimm.Application/Abstractions` | `IAppInsightsReader`: `GetOverviewAsync(range)` — для страницы, `GetSummaryAsync()` — для дашборда (24 ч, дёшево) |
| `Swimm.Application/Dtos` | `AppInsightsDtos.cs`: состояние подключения, плитки, строки таблиц |
| `Swimm.Application/Mapping` | `AppInsightsOverviewBuilder` — чистые функции: пороги и тона плиток, главная цифра блока. Тесты `AppInsightsOverviewBuilderTests` |
| `Swimm.Infrastructure/Services/AppInsights/` | `AppInsightsReader` (клиент, обработка «не настроено / нет входа / таймаут»), `AppInsightsQueries` (весь KQL в одном файле) |
| `Swimm.API/Controllers` | `AppInsightsAdminController`: `GET /api/admin/appinsights/overview?range=1h|24h|7d`, `GET /api/admin/appinsights/summary`. `[Authorize(Roles="Admin")]`, только чтение |

Правила:
- Кэш ответа в памяти 2 минуты: запрос к Log Analytics идёт 1–3 секунды и имеет лимиты частоты.
- Таймаут 15 секунд; сбой Azure не роняет страницу — состояние «не удалось прочитать» с текстом.
- Регистрация под `adminEnabled` — на проде этот код не исполняется (админки там нет).
- KQL сверять на живых данных после А3: имена таблиц (`AppRequests`, `AppExceptions`,
  `AppDependencies`, `AppTraces`, `Usage`; `AppAvailabilityResults` пустая — теста доступности в Azure нет, §8.3) проверить запросом, не по памяти.

**Сделано 17.11 (Opus 5.5, ветка `claude/app-insights-b1-5ff754`, не запушено).** Контракт API для
Б2/Б3 — в [docs/tasks/appinsights-page-sonnet.md](../tasks/appinsights-page-sonnet.md).
- **Файлы:** `Application/Abstractions/IAppInsightsReader.cs`; `Application/Dtos/AppInsightsDtos.cs`;
  `Application/Mapping/AppInsightsOverviewBuilder.cs` (+ `AppInsightsThresholds`, `AppInsightsConnections`);
  `Infrastructure/Services/AppInsights/` — `AppInsightsReader` (кэш, таймаут, состояния),
  `AppInsightsQueries` (весь KQL), `AzureLogsQueryRunner` (SDK), `AppInsightsRowMapper`,
  `AppInsightsErrorClassifier`, `AppInsightsPortalLinks`, `AppInsightsOptions`, регистрация;
  `API/Controllers/AppInsightsAdminController.cs`; `API/Telemetry/AppInsightsReaderRegistration.cs`
  (в `Program.cs` — одна строка в блоке `adminEnabled`); две строки в `ApiCatalog` (/Admin/Api).
- **Пакеты** (в Infrastructure): `Azure.Monitor.Query.Logs` 1.0.0, `Azure.Identity` 1.21.0. Тянут
  `Azure.Core` ≥ 1.53 → `Microsoft.Extensions.Hosting.Abstractions` поднят 10.0.0 → 10.0.3 (иначе NU1605).
  Итоговый `Azure.Core` в API — 1.60 (от OpenTelemetry), как и был.
- **Запросы — одним пакетом** (`QueryBatch`, один HTTP): страница — 5 запросов, блок — 1. Ответ
  живьём 1,3–1,5 с (первый после старта — до 4 с: `az account get-access-token`). Токен кэширует клиент.
  ⚠ **С 19.11 — не пакетом, а по одному параллельно** (`QueryWorkspaceAsync`): `$batch` плавающе отдавал
  `body` строкой, SDK не разбирал ответ — разбор в `docs/admin-pages/appinsights.md` («Грабли»).
  Живьём после правки: страница 2,9 с, блок 1,3 с.
- **Пороги** (`AppInsightsThresholds`, согласованы с А4): 5xx > 0 — жёлтое, доля > 5 % — красное;
  тишина сайта > 15 мин — жёлтое, > 30 мин или нет за 7 дней — красное (у ProdAdmin тишина — норма,
  серое); приём ≥ 80 % лимита — жёлтое (как письмо «Объём», 120 МБ), ≥ 100 % — красное; исключения,
  сбои вне запроса, ошибки ProdAdmin — жёлтое; медленная операция — p95 > 1000 мс (на тон не влияет,
  `info`); плитки «Ответ p95» > 1000 мс и «Postgres» p95 > 500 мс или сбой — жёлтые. Лимит 0,15 ГБ =
  150 МБ (десятичные ГБ, как в Usage), сброс 10:00 UTC — `AppInsights:DailyCapGb`/`CapResetHourUtc`, умолчания в коде.
- **Находки живой проверки (для Б2):** страницы SPA приходят в `Name` с адресом (`GET /swimmers/8318`),
  маршруты API — шаблоном → в KQL числа пути сводятся к `{id}`. Метрики `AppMetrics` сайт шлёт ~20 в
  минуту и без трафика, Postgres — пинг раз в минуту: «последняя телеметрия» — честный пульс.
  ⚠ Log Analytics **сам кэширует** результат одинакового текста запроса на минуту-другую: итоги
  сайта за 24 ч у `summary` и у `overview?range=24h` — один текст, поэтому `refresh=true` в эту
  минуту может вернуть те же цифры. Это не баг. ⚠ `appsettings.Local.json` подключается в
  `Program.cs` последним — переменная окружения `AppInsights__*` его НЕ перебивает.
- **Сбой чужого/неверного WorkspaceId** в пакете приходит не HTTP-статусом, а `ErrorCode=InvalidTokenError`
  (статус 0) — классифицируется как «нет доступа»; сырое тело ответа (там id каталогов) на страницу не идёт.
- **Проверено вживую** (API Release на :5092, Development, ключи в gitignored `appsettings.Local.json`
  worktree): `summary` и `overview` для 1h/24h/7d × web/prodadmin — реальные цифры прода (≈300 запросов,
  5xx нет, 12 медленных операций, одна 404 `lane-plans/{date}`); `range=30d`, `role=dev` → 400;
  «не настроено» (без файла ключей) — карточка с командами `az`, в Azure не ходит; «нет входа» —
  `AZURE_CONFIG_DIR` на пустой каталог → «Please run 'az login'», подсказка `az login`; «не удалось
  прочитать» — чужой WorkspaceId → «Нет доступа к рабочей области (InvalidTokenError)».
- **Тесты:** `AppInsightsOverviewBuilderTests` (тоны, плитки, сводка, формат), `AppInsightsReaderTests`
  (три состояния + сбои Azure, таймаут 15 с, отмена клиентом, кэш по периоду/роли, сбой не кэшируется,
  разбор строк, ссылки), `AppInsightsQueriesTests` (белые списки роли/периода, правила данных в KQL,
  контроллер → 400). 99 тестов.
- **Не сделано (Б1 не требует):** ключи в `appsettings.Local.json` / `appsettings.ProdAdmin.Local.json`
  основной копии Влада не вписаны — команды в `.example`, страница сама подскажет.

### Б2. Страница `/Admin/AppInsights`

Исполнитель: **Sonnet** через `/delegate` (таск-документ `docs/tasks/appinsights-page-sonnet.md`
пишет Fable после Б1, с готовым контрактом API). Образец — `/Admin/Jobs`.

Пункт сайдбара **«App Insights»** в разделе «System» (`Shared/AdminUi.cs`), после «Jobs».

Состав:
1. **Шапка:** состояние подключения, шлёт ли телеметрию ЭТОТ процесс, когда пришла последняя
   запись с прода, переключатель периода (1 ч / 24 ч / 7 дней) и роли (сайт / ProdAdmin).
   Ссылки в портал (Failures, Performance, Live metrics, Logs) собираются во время работы из
   `AppInsights:ResourceId` — в разметке и в репо идентификаторов нет.
2. **Плитки:** запросов; доля ошибок (5xx); медиана и p95 ответа; исключений; Postgres — p95 и
   сбои; принято сегодня / дневной лимит.
3. **Таблицы:** частые исключения (тип, начало сообщения, сколько, когда последний раз, операция);
   самые медленные операции (сколько, p50, p95); ошибки по маршруту и коду; сбои фоновых служб.
4. Данные грузит скрипт `wwwroot/js/admin/appinsights.js` — страница открывается сразу, цифры
   доезжают; обновление — кнопкой.

Definition of done: `docs/admin-pages/appinsights.md` + строка в README; `npm run css:build` и
закоммиченный `admin.min.css` (без `[#hex]/opacity`); тёмная тема и ширина 375 px; три состояния
из §4.1 проверены глазами в панели браузера.

### Б3. Блок на дашборде

Исполнитель: **Sonnet**, тем же таск-документом, что Б2. Образец — блок «Jobs» (п. 9 в
[admin-pages/index.md](../admin-pages/index.md)).

- Блок 10 **«App Insights»** → `/Admin/AppInsights`, своим живым запросом
  `GET /api/admin/appinsights/summary`; сбой роняет только этот блок.
- Главная цифра — ошибок сервера за 24 ч; при нуле — зелёная ✓.
- Подпись: «N запросов · p95 320 мс · телеметрия 2 мин назад».
- Метрики: ошибки 5xx, исключения, медленные операции (p95 выше порога).
- Тон: красный — телеметрия молчит дольше 30 минут при настроенном подключении или доля ошибок
  выше 5 %; жёлтый — есть исключения или приём выше 80 % лимита. Пороги — в `AppInsightsOverviewBuilder`.
- «Не настроено» и «нет входа» — серый блок с подсказкой, не красный.

**Сделано 17.11 (Б2+Б3, Sonnet по [таску](../tasks/appinsights-page-sonnet.md), приёмка Opus 5.5).**
Файлы: `Pages/Admin/AppInsights.cshtml(.cs)`, `wwwroot/js/admin/appinsights.js`, `buildAppInsightsBlock`
в `dashboard.js`, пункт сайдбара после Jobs, запись в `ServicesCatalog`, пересобранный `admin.min.css`.
Отклонения Sonnet (приняты): период и роль — в query (`?range=&role=`), hash занят якорями таблиц;
плитки «Принято сегодня» и «Последняя телеметрия» ведут к шапке; у блока дашборда при зелёной ✓ и
жёлтом/красном тоне по другой причине — точка цвета тона с причинами в `title`.
Поправлено при приёмке: значение плитки переносится, а не обрезается («0,77 / 150…»); подпись таблицы
фона «Warning+» → «Error+» (как в KQL); «данные на … (кэш 2 мин)» — только при прочитанных данных.
**Найдено на живых данных:**
- плавающий сбой Б1: SDK трижды подряд (~12:51–12:53 UTC) не разобрал ответ пакета
  (`InvalidOperationException` «requires an element of type 'Object', but … 'String'»), потом прошло само;
  сырые REST-пакеты, 30 одновременных и повтор на одном клиенте — не воспроизвели. Сделано: один
  повтор в пределах 15 с, стек — в журнал, карточка «ответ не удалось разобрать» вместо текста SDK
  (`AppInsightsErrorClassifier.IsUnreadableResponse`, три теста). ⚠ 19.11 повторилось (07:50 UTC) и
  повтор не помог (Log Analytics кэширует ответ на тот же текст) — `$batch` убран, запросы по одному
  (разбор — `docs/admin-pages/appinsights.md`, «Грабли»);
- ⚠ ответы 5xx на проде видны как `GET /error`: обработчик ошибок перезапускает запрос на `/error`,
  и исходный маршрут в `Name` теряется — для разбора 5xx это вопрос телеметрии А2, а не страницы;
- на проде 17.11 ~12:40 UTC фоновые `System.TimeoutException` и `Npgsql.NpgsqlException` («The operation
  has timed out»), 2 ответа 500 — смотреть Владу/следующему этапу.

### Б4. Документация

| Что | Кто |
|---|---|
| `docs/admin-pages/appinsights.md`, строка в README, п. 10 в `index.md` | Sonnet (в составе Б2/Б3) |
| Запись в `ServicesCatalog.Groups` (группа Azure) | Sonnet |
| [deploy.md](../deploy.md) — «если сломалось: куда смотреть», переменная в реестре §5 плана деплоя, отметка В14, строка в ROADMAP | Fable |
| Текст о приватности на сайте — проверить, не нужна ли строка о технических журналах | Fable проверяет, решает Влад |

**Б4-приватность — ✅ внесено 17.11 (решение Влада: «Да, вноси»)** — все три пункта ниже в
`en/ru/he`, дата редакции 17.11.2026, пользователям не объявляется. `/privacy` проверена глазами на
трёх языках (иврит — `rtl`), `npm run build` и `npm test` клиента зелёные. Было до правки — текст —
`client/src/content/help/privacy/{en,ru,he}.md` (`/privacy`, редакция 23.10.2026). Сейчас там:
провайдер «Microsoft Azure, Israel Central — hosting of the site and its database»; срок хранения
«technical server logs (requests and errors, which may include IP addresses) — for the limited period
needed to run and protect the site»; в «Cookies» — «no … third-party analytics». Что реально пишет
телеметрия (запросом 17.11): IP — `0.0.0.0` (маскируется), город/страна — один на все записи (похоже,
адрес прокси App Service, не посетителя), браузер и ОС из User-Agent — да, id пользователя и сессии —
нет, значения параметров адреса — `Redacted`; хранение таблиц — 90 дней.
Противоречия нет, но формулировка расплывчата. **Предложение** (во всех трёх языках):
1. В списке провайдеров: «Microsoft Azure, Israel Central region — hosting of the site, its database
   and technical monitoring».
2. В сроках хранения заменить строку логов на: «technical logs — page addresses requested (without
   search or link parameters), response times, errors and the browser and device type; the IP address
   is not stored — up to 90 days».
3. В «Cookies» добавить: «Technical monitoring runs on our server only: there is no analytics script
   or cookie in your browser.»
Дата редакции — новая; изменение не существенное для владельцев аккаунтов (объявлять не нужно).

## 5. Кто что делает

| Этап | Что | Кто | Почему он | Оценка |
|---|---|---|---|---|
| А1 | ресурсы Azure | **Fable** + «да» Влада | запись в подписку, отменяется плохо | 15 мин |
| А2 | отправка телеметрии, вычистка, тесты | **Fable** | токены и личные данные — ошибка здесь утечка | 2–3 ч |
| А3 | деплой, проверка на проде | **Fable** готовит, **Влад** жмёт | деплой только по кнопке (В13) | 30 мин |
| А4 | оповещения | **Fable** + «да» Влада | та же подписка | 20 мин |
| Б1 | чтение: порт, KQL, сборщик, API, тесты | **Opus** | есть развилки, но граница описана | 2–3 ч |
| Б2 | страница | **Sonnet** | вёрстка по образцу и готовому контракту | 1–2 ч |
| Б3 | блок дашборда | **Sonnet** | копия блока «Jobs» | 1 ч |
| Б4 | документация | Sonnet + Fable | см. таблицу Б4 | 30 мин |
| Р | ревью работы Opus и Sonnet, проверка в панели, pre-push | **Fable** | отчёт делегата — не доказательство | 1 ч |

За Владом: «да» на А1 и А4, кнопка деплоя, ответы §8, взгляд на страницу.

## 6. Порядок и PR

1. **PR 1 = А2** → А1 → А3 → А4. После этого польза уже есть: портал и письма.
2. **PR 2 = Б1 + Б2 + Б3 + Б4** — после А3, потому что запросы KQL сверяются на живых данных.
   Б2 и Б3 идут после Б1 (нужен контракт), между собой — одним заданием.

Деплой PR 2 сайту ничего не меняет (админки на проде нет), но пакеты `Azure.Identity` и
`Azure.Monitor.Query` едут в образ — проверить размер publish-выхода.

## 7. Грабли

- Смена настройки App Service перезапускает сайт — не делать в час соревнований.
- Health-check и статика без фильтра съедят объём (А2.4).
- Идентификаторы Azure — не в коммит: ни в `appsettings.json`, ни в MD, ни в ссылках разметки.
- Вход `az` истекает — страница должна говорить об этом словами, а не падать.
- `Usage` появляется в рабочей области не сразу — «нет данных» ≠ ошибка.
- Пакет экспорта поднимает свой фоновый поток — посмотреть память на B1 (А3.4). ✅ 18.11: +7 % к среднему, не проблема.
- Новые Tailwind-классы → пересборка бандла, иначе CI «Бандл админки пересобран» упадёт.

## 8. Открытые решения — ждут Влада

1. ✅ **ProdAdmin тоже шлёт телеметрию — ДА** (Влад, 17.11). Рекомендовал да: там идут джобы и импорт, их сбои и есть
   главный повод. Цена — ключ в локальном файле.
2. ✅ **Дневной лимит приёма** — 0,15 ГБ (Влад, 17.11). Рекомендовал 0,15 ГБ в день (≈ 4,5 ГБ в месяц, в бесплатных 5 ГБ).
   При достижении запись останавливается до следующих суток.
3. ✅ **Платная проверка доступности — НЕ ставим** (Влад, 17.11). Уже есть бесплатный монитор
   UptimeRobot «swimhub.co.il» с 17.10: `https://swimhub.co.il/readyz` каждые 5 минут, письмо
   владельцу при падении и подъёме ([azure-deploy-handoff.md](azure-deploy-handoff.md), карточка на
   `/Admin/Services`). Он же ловит истёкший сертификат и слетевший DNS.
4. ✅ **Куда слать оповещения** — тот же Gmail Влада, что у UptimeRobot (17.11). Адрес — только в группе действий Azure, в репо его нет.
5. ✅ **Название пункта** — «App Insights» (Влад, 17.11).

## 9. Как вернуться к работе

Статус — в таблице ниже; обновлять после каждого этапа.

| Этап | Статус |
|---|---|
| А1 ресурсы | ✅ 17.11 `swimhub-logs` + `swimhub-ai`, дневной лимит 0,15 ГБ |
| А2 код | ✅ 17.11 PR #197 (`d7a33136`), на проде |
| А3 деплой | ✅ 17.11 на проде, телеметрия идёт, вычистка проверена, ProdAdmin (§8.1) — ключ записан; 18.11 память «после»: 222/334 МБ против 207/375 — в шуме |
| А4 оповещения | ✅ 17.11 `swimhub-alerts` + 3 правила (≈ $1,1/мес); ждёт: Влад видит письмо группы |
| Б1 чтение | ✅ 17.11 код + тесты, эндпоинты проверены вживую на данных прода (ветка `claude/app-insights-b1-5ff754`, не запушено); таск Б2+Б3 — `docs/tasks/appinsights-page-sonnet.md` |
| Б2 страница | ✅ 17.11 Sonnet по таску, принято Opus: `/Admin/AppInsights` проверена глазами (3 состояния, 375 px; тема у админки одна — тёмная), не запушено |
| Б3 блок | ✅ 17.11 блок 10 дашборда, грузится отдельно от `Promise.all`; проверен в «работает» и «нет входа» |
| Б4 документация | ✅ 17.11 часть Sonnet (`admin-pages/appinsights.md`, README, п. 10 `index.md`, `ServicesCatalog`) и часть Fable — `deploy.md` («Что где» + «Если сломалось» п. 5), В14 в плане деплоя, строка ROADMAP, текст `/privacy` (решение Влада, §Б4-приватность) |
