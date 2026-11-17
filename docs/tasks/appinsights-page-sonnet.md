# Задание (Sonnet): страница /Admin/AppInsights + блок 10 дашборда (Б2 + Б3)

Контекст: план [docs/plans/application-insights-plan.md](../plans/application-insights-plan.md),
части **Б2** (страница) и **Б3** (блок дашборда). Сайт swimhub.co.il шлёт телеметрию в Azure
Application Insights; админка (только локально у Влада — Development и ProdAdmin, решение В17)
читает её через готовый API. **Б1 (серверное чтение) сделан 17.11.2026** — твоя работа только
вёрстка и скрипты поверх готового контракта. Сервер не меняй, кроме мелочи из шага 1.

Прочти до начала: этот файл целиком; [admin-pages/jobs.md](../admin-pages/jobs.md) (образец
страницы); [admin-pages/index.md](../admin-pages/index.md) п. 9 «Jobs» (образец блока); корневой
`CLAUDE.md` разделы «Admin pages map» и «Admin/home CSS (Tailwind v4)»; [pre-push-rules.md](../pre-push-rules.md)
правила 2 и 3.

## Решения (зафиксированы, не пересматривать)

- **Маршрут** `/Admin/AppInsights`, заголовок «App Insights», пункт сайдбара **«App Insights»** в
  разделе «System», **сразу после «Jobs»** (`Pages/Admin/Shared/AdminUi.cs`), иконка `activity`
  или `gauge` (обе есть в `_Icon.cshtml`; новую иконку не рисовать).
- **Данные — только скриптом** `wwwroot/js/admin/appinsights.js`: Razor-страница открывается сразу
  с каркасом и «Загрузка…», цифры доезжают запросом. Сервер в `OnGet` в Azure НЕ ходит.
- **Обновление — кнопкой** «Обновить» (запрос с `refresh=true`). Автообновления нет.
- **Язык админки — русский** (как на /Admin/Jobs). Правило «UI только English» — про публичный сайт.
- **Тоны** приходят с сервера готовыми (`ok | warning | danger | info | neutral`) — на клиенте пороги
  НЕ считать. Цвета — как `TileTone` в `Pages/Admin/Jobs.cshtml` (токены `admin-danger`,
  `admin-warning`, `admin-success`, `admin-accent`).
- **Ни одного идентификатора Azure в разметке, JS и MD** (репо публичный): ссылки в портал берутся
  только из ответа API (`links`), он собирает их из локального конфига.
- **Всё текстовое из ответа — через экранирование** (`esc`) при вставке в HTML: сообщения
  исключений и логов — недоверенный текст с прода.

## Контракт API (готов, не менять)

Оба эндпоинта: `[Authorize(Roles="Admin")]`, только GET, ответ всегда **200** с карточкой
состояния (`connection`), если Azure недоступен; **400** — только на неверный параметр. JSON —
camelCase. Время — UTC ISO-8601 (показывать в локальном времени браузера, как остальная админка).

### `GET /api/admin/appinsights/overview?range=1h|24h|7d&role=web|prodadmin&refresh=true`

Умолчания: `range=24h`, `role=web`, `refresh=false`. Ответ кэшируется на сервере 2 минуты по паре
(range, role); `refresh=true` идёт мимо кэша. Время ответа — 1–2 с, первый после старта API — до
5 с, при сбое Azure — до 15 с (таймаут).

```jsonc
{
  "connection": {
    "state": "ok",                 // ok | not-configured | no-login | failed
    "title": "Подключено",         // заголовок карточки состояния
    "text": "Данные читаются из …",// пояснение (у failed — причина)
    "hint": null                   // что скопировать: "az login" (no-login) или 2 команды az через \n (not-configured); иначе null
  },
  "process": { "sending": false, "roleName": "swimhub-dev" },  // шлёт ли телеметрию ЭТОТ процесс и под какой ролью
  "range": "24h", "role": "web", "roleName": "swimhub-web",
  "generatedAt": "2026-11-17T12:21:30Z",   // когда прочитано из Azure (кэш 2 мин)
  "tone": "ok",                            // здоровье роли: ok | warning | danger; neutral — данных нет
  "reasons": [],                           // почему такой тон: ["ошибок сервера (5xx): 3 из 40 — 8 %", …]
  "lastTelemetryAt": "2026-11-17T12:20:48Z",
  "lastTelemetryAgo": "40 с назад",        // null при state != ok
  "tiles": [                               // 8 плиток в этом порядке; пусто при state != ok
    { "key": "requests",   "label": "Запросов",            "value": "262",          "sub": "за 24 ч",                  "tone": "neutral", "href": "#slow" },
    { "key": "errors",     "label": "Ошибки 5xx",          "value": "0 %",          "sub": "0 из 262",                 "tone": "ok",      "href": "#errors" },
    { "key": "latency",    "label": "Ответ p95",           "value": "1295 мс",      "sub": "медиана 15 мс",            "tone": "warning", "href": "#slow" },
    { "key": "exceptions", "label": "Исключения",          "value": "0",            "sub": "вне запроса (фон): 0",     "tone": "ok",      "href": "#exceptions" },
    { "key": "db",         "label": "Postgres p95",        "value": "71 мс",        "sub": "вызовов 1255, сбоев 0",    "tone": "ok",      "href": "#slow" },
    { "key": "slow",       "label": "Медленные операции",  "value": "12",           "sub": "p95 выше 1000 мс",         "tone": "info",    "href": "#slow" },
    { "key": "ingestion",  "label": "Принято сегодня",     "value": "0,1 / 150 МБ", "sub": "0,1 % дневного лимита, сброс в 10:00 UTC; все роли", "tone": "ok", "href": "#ingestion" },
    { "key": "last",       "label": "Последняя телеметрия","value": "21 с назад",   "sub": "17.11.2026 14:20",         "tone": "ok",      "href": "#last" }
  ],
  "exceptions": [   // до 10, частые сверху
    { "type": "System.InvalidOperationException", "message": "…до 300 символов…", "count": 3,
      "lastAt": "2026-11-17T11:02:00Z", "operation": "GET api/clubs/{id:int}/overview" }   // operation "" — вне запроса (фон)
  ],
  "slowOperations": [   // до 10, по p95 убыванию; числа в пути уже заменены на {id}
    { "operation": "GET api/clubs/{id:int}/overview", "count": 2, "p50Ms": 1631.2, "p95Ms": 4492.6, "errors5xx": 0 }
  ],
  "errors": [   // ответы с кодом 400+, до 15; 5xx первыми (serverError=true)
    { "operation": "GET api/hub-groups/{id:int}/lane-plans/{date}", "code": 404, "count": 4,
      "lastAt": "2026-11-17T12:08:24Z", "serverError": false }
  ],
  "background": [   // сбои вне запроса (фоновые службы), до 15, свежие сверху
    { "kind": "exception", "source": "Npgsql.NpgsqlException", "message": "…", "count": 1, "lastAt": "…" }   // kind: exception | log (source = категория лога)
  ],
  "links": {        // null, если ResourceId не задан/кривой — тогда блок ссылок не показывать
    "overview": "https://portal.azure.com/#@/resource/…/overview",
    "failures": "…/failures", "performance": "…/performance", "liveMetrics": "…/quickPulse", "logs": "…/logs"
  }
}
```

При `state` ≠ `ok`: `tone` = `neutral`, `tiles`/`exceptions`/`slowOperations`/`errors`/`background`
пустые, `lastTelemetryAt`/`lastTelemetryAgo` = null; `links` может быть (если ResourceId задан).

### `GET /api/admin/appinsights/summary`

Сайт (`swimhub-web`) за 24 ч, один дешёвый запрос, кэш 2 мин, без параметров.

```jsonc
{
  "connection": { "state": "ok", "title": "Подключено", "text": "…", "hint": null },
  "generatedAt": "2026-11-17T12:20:56Z",
  "tone": "ok",              // тон блока: danger | warning | ok; neutral — не настроено / нет входа / не прочитано
  "reasons": [],             // строки «почему» (как у overview)
  "value": "✓",              // главная цифра: число 5xx за 24 ч; при нуле "✓"; без данных "—"
  "valueTone": "ok",         // тон главной цифры (ok при нуле 5xx)
  "caption": "262 запроса · p95 1295 мс · телеметрия 7 с назад",   // без данных — connection.title
  "errors5xx": 0, "requests": 262, "p95Ms": 1294.9,
  "lastTelemetryAt": "2026-11-17T12:20:48Z",
  "metrics": [               // 5 метрик в этом порядке; пусто без данных
    { "key": "errors",     "label": "Ошибки 5xx",            "value": "0",            "tone": "ok" },
    { "key": "exceptions", "label": "Исключения",            "value": "0",            "tone": "ok" },
    { "key": "background", "label": "Сбои фона и ProdAdmin", "value": "0",            "tone": "ok" },
    { "key": "slow",       "label": "Медленные операции",    "value": "12",           "tone": "info" },
    { "key": "ingestion",  "label": "Принято сегодня",       "value": "0,1 % лимита", "tone": "ok" }
  ]
}
```

Откуда тоны (для справки, считает сервер — `AppInsightsOverviewBuilder`/`AppInsightsThresholds`):
красный — сайт молчит > 30 мин или 5xx > 5 % запросов; жёлтый — есть 5xx, исключения, сбои фона
или ProdAdmin, приём ≥ 80 % лимита, тишина > 15 мин. Серый — нет данных.

## Шаги

1. **Страница (Б2).** `Pages/Admin/AppInsights.cshtml` + `.cshtml.cs` (`[Authorize(Roles = "Admin")]`,
   `OnGet` пустой — данных не грузит). `ViewData["Title"] = "App Insights"`, подзаголовок
   «Телеметрия прод-сайта: ошибки, медленные запросы, исключения». Состав сверху вниз:
   1. **Шапка-панель:** карточка состояния (`connection.title` + `text`; у `hint` — `<pre>` с кнопкой
      «Скопировать»); строка «этот процесс: шлёт телеметрию как `roleName` / не шлёт» (`process`);
      «последняя запись с прода: `lastTelemetryAgo`»; переключатели **периода** (1 ч / 24 ч / 7 дней)
      и **роли** (Сайт / ProdAdmin) — сегментные кнопки; кнопка «Обновить»; «данные на HH:MM
      (кэш 2 мин)» из `generatedAt`; ссылки в портал (Failures, Performance, Live metrics, Logs,
      Overview — `target="_blank" rel="noopener"`), только если `links` не null.
      Выбор периода и роли — в `location.hash` или query (`?range=&role=`), чтобы ссылка открывалась так же.
   2. **Плитки** — сетка как на /Admin/Jobs (`grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4`), разметка
      плитки и тоны — как `TileTone` в `Jobs.cshtml`, но рисует JS. `href` плитки — якорь секции ниже
      (`#slow`, `#errors`, `#exceptions`, `#ingestion`, `#last` — у последних двух якорем может быть сама
      плитка/шапка). Если `reasons` не пусто — строка-баннер над плитками с тоном `tone` и списком причин.
   3. **Таблицы** (каждая — `admin-card`, заголовок + «пусто — хорошо» при нуле строк):
      «Частые исключения» `#exceptions` (тип, сообщение, сколько, когда последний раз, операция; пустая
      операция → «фон»); «Самые медленные операции» `#slow` (операция, сколько, p50, p95, 5xx; p95 >
      1000 мс подсветить `text-admin-warning`); «Ошибки по маршруту и коду» `#errors` (операция, код,
      сколько, последний раз; 5xx — `text-admin-danger`, 4xx — приглушённо); «Сбои фоновых служб»
      `#background` (вид, источник, сообщение, сколько, последний раз). Длинные сообщения —
      `line-clamp`/`truncate` с полным текстом в `title`. На 375 px таблицы скроллятся по горизонтали
      внутри карточки (`overflow-x-auto`), страница — нет.
   4. **Три состояния без данных** (`connection.state` ≠ `ok`): вместо плиток и таблиц — одна карточка
      состояния крупно: «Не настроено» (серая, `hint` — две команды), «Нет входа az» (серая, `hint` =
      `az login`), «Не удалось прочитать» (жёлтая рамка, `text` — причина). Не красные.
   5. Сбой самого запроса (сеть, 500) — карточка «Не удалось загрузить» с кнопкой «Повторить».
2. **Скрипт** `wwwroot/js/admin/appinsights.js` — по образцу других `wwwroot/js/admin/*.js`
   (подключение в конце страницы, как у них; `esc()` для всего текста; `fetch` с `credentials: 'same-origin'`).
   Пока идёт запрос — плитки-скелеты / «Загрузка…», кнопки переключателей неактивны.
3. **Сайдбар:** в `Pages/Admin/Shared/AdminUi.cs` после строки `new("Jobs", …)` —
   `new("App Insights", "activity", "/Admin/AppInsights", "System")` с комментарием-строкой как у соседей.
4. **Блок 10 дашборда (Б3)** в `wwwroot/js/admin/dashboard.js`, функция `buildAppInsightsBlock(s)` по
   образцу `buildJobsBlock`: `key: 'appinsights'`, `title: 'App Insights'`, `href: '/Admin/AppInsights'`.
   - **⚠ Грузить ОТДЕЛЬНО от `Promise.all` в `loadStatus()`**: summary может идти до 15 с (таймаут
     Azure) и не должен задерживать остальные блоки. Сначала отрисовать блок «Загрузка…», по ответу —
     `renderRows()` ещё раз. Сбой запроса (`catch`) роняет только этот блок: `heroValue: '—'`,
     `heroSubs: 'сводка App Insights не загрузилась'`.
   - `heroValue`: `s.value` с цветом по `s.valueTone` (`ok` → `text-admin-success`, `warning` →
     `text-admin-warning`, `danger` → `text-admin-danger`, иначе приглушённый).
   - `heroSubs`: `s.caption`; если `s.reasons` не пусто — первая причина перед подписью.
   - Тон блока — `s.tone` (как блок Jobs показывает проблему); `neutral` — серый блок, не красный.
     При `connection.state` ≠ `ok` в панели деталей — `connection.text` и `hint` (команда в `<code>`).
   - `metrics`: из `s.metrics` — `{ v: m.value, label: m.label, tone: … , href: '/Admin/AppInsights#…' }`
     (якоря: errors → `#errors`, exceptions → `#exceptions`, background → `#background`, slow → `#slow`,
     ingestion → `#ingestion`). Если пилюли метрик принимают только числа — нечисловое (`ingestion`)
     вынести в `rowNote` («принято 0,1 % лимита»), см. как это сделано у блока «Кэш».
   - Порядок блоков у Влада хранится в браузере (drag-reorder) — новый ключ должен просто появиться
     в конце, ничего не ломая (проверь, как `dashboard.js` обходится с ключом, которого нет в
     сохранённом порядке).
5. **Каталог служб** `/Admin/Services` (`Pages/Admin/Shared/ServicesCatalog.cs`, группа «Хостинг —
   Microsoft Azure»): запись «Application Insights — телеметрия» — что это (запросы, исключения,
   Postgres, логи Warning+ сайта и ProdAdmin; оповещения на почту; страница /Admin/AppInsights),
   ссылки только общие: `https://portal.azure.com/#browse/microsoft.insights%2Fcomponents` и
   `https://portal.azure.com/#view/Microsoft_Azure_Monitoring/AzureMonitoringBrowseBlade/~/alertsV2`.
   **Без id подписки/ресурса.**
6. **Документация:** `docs/admin-pages/appinsights.md` (что показывает, файлы, API, три состояния,
   откуда пороги, грабли — ниже) + строка в таблице `docs/admin-pages/README.md` (после Jobs);
   п. **10 «App Insights»** в `docs/admin-pages/index.md` по образцу п. 9.
7. **CSS:** `cd server/Swimm.API && npm run css:build` и закоммитить `wwwroot/css/admin.min.css`.
   Никаких `[#hex]/opacity` — только токены (`border-admin-danger/60`) или `[rgba(...)]`.

## Проверка (обязательно глазами, в панели браузера приложения)

- Сборка: `dotnet build server/Swimm.sln` (если DLL занят процессом Влада на :5078 — НЕ убивать,
  собирать `-c Release` и поднимать API на своём порту, например 5093).
- Ключи: в gitignored `server/Swimm.API/appsettings.Local.json` ТВОЕЙ копии раздел
  `"AppInsights": { "WorkspaceId": …, "ResourceId": … }` — значения подставь командами из
  `appsettings.Local.json.example` (подстановкой в файл, **в чат и в коммит не печатать**). В
  Development вход в админку — `DevAdminBypass` из `appsettings.Development.json` (скопируй его из
  основной копии, если в worktree нет).
- Три состояния (каждое — перезапуск API):
  - **работает** — с ключами и `az login` (уже есть у Влада): плитки и таблицы с реальными цифрами
    прода, переключатели периода/роли меняют данные, «Обновить» перечитывает;
  - **не настроено** — временно переименуй `appsettings.Local.json` (переменная окружения его НЕ
    перебивает: файл подключается в `Program.cs` последним), потом верни;
  - **нет входа** — запусти API с `AZURE_CONFIG_DIR=<пустой временный каталог>` (вход Влада не трогать,
    `az logout` НЕ делать).
- Тёмная тема и ширина 375 px (resize панели) — без горизонтального скролла страницы.
- Дашборд `/Admin`: блок «App Insights» появился, остальные блоки рендерятся сразу, не дожидаясь его.
- Тесты: `dotnet test server/Swimm.Tests -c Release` — зелёные (сервер ты не менял, но проверь).
- Останови свой процесс API после проверки.

## Грабли

- Log Analytics сам кэширует ответ на одинаковый текст запроса минуту-другую: «Обновить» сразу после
  загрузки может вернуть те же цифры — это не баг, не «чинить» на клиенте.
- `range=30d`, `role=dev` → 400 — переключатели шлют только `1h|24h|7d` и `web|prodadmin`.
- У роли ProdAdmin тишина — норма (её запускают руками): плитка «Последняя телеметрия» серая.
- Сообщения исключений и логов могут содержать что угодно — только `esc()`; в `title` — тоже экранировать.
- Не добавляй Azure-идентификаторы ни в разметку, ни в MD, ни в примеры (в этом файле ссылки — с `…`).

## Вне скоупа (не делать)

- Любые изменения сервера: KQL, пороги, DTO, контроллер (Б1 закрыт; нашёл проблему — опиши в отчёте).
- Графики/спарклайны по времени, автообновление, браузерный SDK Application Insights.
- Запись в Azure (оповещения, настройки) — только чтение.
- Коммит/пуш — по указанию того, кто запустил задание.
