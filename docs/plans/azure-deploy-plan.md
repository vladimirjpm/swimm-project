# План — вывод в продакшн на Azure (App Service + Flexible PostgreSQL)

**Ревизия 2 — 2026-08-27.** Полностью заменяет редакцию от 2026-07-29: та описывала
репозиторий, которого больше нет (оба клиентских workflow снесены в `d3b25ab`, появились
чистые URL, страница спортсмена, season-best, правила очков, фоновые джобы loglig).

Основание — решение о хостинге в [ROADMAP.md](../ROADMAP.md): **весь стек на Azure, один
origin**, App Service раздаёт прод-сборку клиента из `wwwroot`. Отдельный фронт-хостинг
отвергнут: второй origin ломает cookie-модель.

Статусы: ☐ не начато · ◐ в работе · ✅ готово

---

## 0. Что изменилось с прошлой редакции

| Пункт ревизии 1 | Сегодня |
|---|---|
| «Есть два деплоя, оба удалить» (§2.1) | ✅ Сделано в `d3b25ab`. Теперь в `.github/` **вообще нет** workflow — деплоя не существует ни одного |
| «net10.0 — риск номер один» (§2.5) | Понижен. .NET 10 — LTS, GA ноябрь 2025. Проверка `az webapp list-runtimes` остаётся, но это pre-flight, а не риск формы деплоя |
| «Данные с нуля + сидеры» (§3.6) | ❌ **Технически невозможно.** `--seed-records` мёртв: пять файлов, из которых он читает, удалены в `e1dcb8e`. См. Б11 |
| «Коллизия `home.html`» (§2.2) | Подтверждена, решение так и не принято |
| §2.4: 6 переменных окружения | Их **27**. Полный реестр — §5 |
| Про Data Protection, ForwardedHeaders, права первого админа, фоновые джобы, юр. сторону — ни слова | Это блокеры Б5, Б6, Б12, Б13, Б14 |

---

## 1. Рамка и принятые решения

**Цель:** кнопка в GitHub Actions → рабочий сайт на `https://<app>.azurewebsites.net`: API,
клиент, админка, Google-логин, письма. Миграции — явный шаг, не автостарт.

Решения, подтверждённые Владом 2026-08-27:

| # | Решение |
|---|---|
| Р1 | Домен на старте — `*.azurewebsites.net`. Свой домен покупается позже, отдельным шагом |
| Р2 | Объём — **минимум рабочего**: App Service B1 Linux + Flexible Server B1ms, один инстанс, без staging-слота |
| Р3 | Данные — **дамп бизнес-таблиц** (решение пересмотрено 27.08: сценарий «с нуля» нереализуем, см. Б11). `pg_dump` с `--exclude-table` на все 28 `Sys_*` и `tmp_*` |
| Р5 | Главная страница прода — **клиентская** (EN, React). ⚠ **Уточнено 28.08:** серверная страница НЕ удаляется, а переезжает в `wwwroot/admin-home.html` — коллизии больше нет, а вход в админку (ссылки на `/Admin`, `/Admin/Health`, Google-логин, баннер «БД недоступна») сохраняется. Корень прода по-прежнему клиентский |
| Р6 | На старте сайт **закрыт от индексации**: `robots.txt` + `noindex` на страницах пловцов, клубов, групп, медиа и админки |
| Р4 | Подписка — существующая `Pay-As-You-Go`, группа ресурсов `swimm-rg` |

**Не в этом плане:** кастомный домен/CDN, staging-слот, Redis, автомасштабирование,
перенос медиа в Blob Storage. См. §11.

---

## 2. Фактическое состояние

### 2.1 Azure (по скриншоту 2026-08-27, вкладка «Недавние»)

| Ресурс | Тип | Судьба |
|---|---|---|
| `Pay-As-You-Go` | Подписка | ✅ целевая |
| `swimm-rg` | Группа ресурсов | Переиспользуем |
| `swimm-project-free` | Static Web App (`blue-tree-0e916eb10`) | 🗑 Снести: раздаёт клиент без API, то есть заведомо нерабочий сайт по публичному URL |
| `testcore21` | App Service | Проверить и снести |
| `test-rg1`, `test-app-service-plan`, `sportmp-cdn`, `rss`, `db-migrate` | Наследие 8–9 лет | Проверить счёт и снести |

Список неполный (вкладка «Недавние»). Полный инвентарь — `az resource list -o table`;
кто ест деньги — `az consumption usage list`. **Секрет `AZURE_STATIC_WEB_APPS_API_TOKEN_
BLUE_TREE_0E916EB10` всё ещё лежит в GitHub Secrets** — удалить вместе с ресурсом.

### 2.2 Репозиторий

- **Рантайм:** `net10.0` во всех шести проектах. `RuntimeIdentifier`, `SelfContained`,
  `PublishTrimmed`, `PublishAot`, `global.json`, `Directory.Build.props`, `NuGet.config`,
  `Dockerfile` — **ничего нет**. SDK на машине 10.0.400, `Microsoft.AspNetCore.App` 10.0.11.
- **Нативных зависимостей нет вообще.** Разбор всех 41 записи в `Swimm.API.deps.json`: ни
  одной секции `runtimeTargets`. PdfPig — pure managed, ClosedXML 0.105 несёт встроенный
  шрифт Carlito. Классический блокер «libgdiplus/libfontconfig на Linux» здесь не выстрелит,
  ставить системные библиотеки не придётся. Windows-специфичных путей в коде тоже нет.
  ➜ **Держать инвариант:** любой новый пакет для PDF/картинок/шрифтов проверять на
  `runtimeTargets` до добавления.
- **`wwwroot` = 2 файла:** `home.html` и `css/admin.min.css`. Сборки клиента там нет и не
  будет — MSBuild-таргета с `BeforeTargets="Publish"` не существует (единственный кастомный
  таргет — `BuildAdminCss`, [Swimm.API.csproj:40](../../server/Swimm.API/Swimm.API.csproj)).
- **CI/CD нет.** `.github/` содержит ровно один файл — `copilot-instructions.md`.
- **Тесты годны для CI как есть:** ни докера, ни живой БД не требуют (два PG-теста
  самоотключаются). Прогонять в пайплайне можно без `services: postgres`.
- **Мусор в артефакте:** `client/public/data/json` (13 МБ, 14 протоколов) и
  `client/public/data/excel` (2,4 МБ) не читает ни один модуль клиента — наследие Фазы 2.
  Плюс 348 МБ в `.git` от удалённых видео (лечится `fetch-depth: 1`, историю не трогаем).
- **`remove-azure-videos.mjs` / `build:azure`** — мёртвые: видео в репозитории уже нет.

### 2.3 Локальная БД (источник для прода)

56 таблиц, из них **28 `Sys_*`**. Размер 57 МБ.

| Таблица | Строк |
|---|---|
| `Results` | 81 463 |
| `RelayMembers` | 14 070 |
| `NormativeStandards` | 6 510 |
| `Swimmers` | 5 551 |
| `Records` | 1 685 |
| `Clubs` | 235 |
| `Competitions` | 152 |

Плюс два временных хвоста ручных операций: `tmp_relay_age_backup_20260731`,
`tmp_results_backup_1528_20260823` — дропнуть перед снятием дампа (§10-В9).

---

## 3. Блокеры

Четырнадцать вещей, каждая из которых по отдельности делает прод нерабочим. Помечены
`[код]` — чиню я, `[Влад]` — только владелец аккаунта/секретов.

### Б1. Клиент вообще не попадает в `wwwroot` `[код]`

`dotnet publish` сегодня даёт сайт, где работают только серверный `home.html` и `/Admin/*`,
а **все публичные страницы отдают 404**. Rewrite чистых URL
([Program.cs:995–1020](../../server/Swimm.API/Program.cs)) ссылается на `results_main.html`,
`swimmer.html`, `club.html`, `season-best.html` — этих файлов в `wwwroot` нет.

➜ Написать шаг публикации клиента (§4/A1). **Не** `BeforeTargets="Build"` — иначе каждый
локальный `dotnet build` потянет npm-сборку и упрётся в build-lock.

### Б2. `base: './'` в Vite — белый экран на всех вложенных URL `[код]`

[vite.config.js:63](../../client/vite.config.js): `base: command === 'serve' ? '/' : './'`.
В `dist/swimmer.html` это даёт `<script src="./swimmer.js">`. Браузер, получив этот HTML по
адресу `/swimmers/123`, разрешит путь как `/swimmers/swimmer.js` → 404 → **страница не
загружается вообще**, не «иконки не те».

Ломается на: `/swimmers/{id}`, `/clubs/{id}`, `/competitions/{id}`, `/groups/{slug}`,
`/groups/{slug}/results`. Тем же механизмом отваливаются `filter-data.js`,
`club-icons-manifest.json` и `images/*` (через `import.meta.env.BASE_URL`).

➜ `base: '/'`. Обоснование «тот же dist работает и на GH Pages» протухло вместе с
удалением workflow в `d3b25ab`. **Обязательно одновременно** снять устаревший запрет в
[docs/tasks/club-page-route-sonnet.md:123–124](../tasks/club-page-route-sonnet.md)
(«base './' — не трогай, это осознанное решение»), иначе следующий агент откатит фикс.

### Б3. Коллизия `home.html` — корень сайта сменит владельца молча `[Влад решает]`

Существуют оба: серверный (RU, Tailwind-бандл админки, баннер «БД недоступна», `#auth-area`)
и клиентский (EN, React). `UseDefaultFiles` ищет `home.html` первым, копирование `dist`
затрёт серверный. ✅ Решено (Р5): корень остаётся **клиентским**.

⚠ **Уточнение 28.08 (Влад).** Сначала серверную страницу удалили совсем — и вместе с ней
потерялся вход в админку: локально `:5078/home.html` (адрес прописан в `launchSettings.json`,
Visual Studio открывает его сама при запуске) стал отдавать 404, а заодно ушла строка
`@source` — то есть половина её стилей.

Правильный размен: не удалять, а **развести имена**. Серверная страница живёт в
`wwwroot/admin-home.html`, клиентская забирает `/` и `/home.html`. Коллизии нет, ни одна из
сторон не потеряна.

`DefaultFileNames` = `home.html`, `admin-home.html`, `index.html` — **порядок значим**:
в проде клиент в `wwwroot` есть и забирает `/`, локально его там нет (он живёт на Vite :5173),
и `/` откатывается на админскую страницу. Судьба баннера «БД недоступна» — §10-В2.

### Б4. Деплоя не существует `[код]` + `[Влад]`

Ни одного workflow. Сегодня единственный путь в прод — `publish` с машины Влада, а это
прямо ведёт к Б7.

### Б5. Нет `UseForwardedHeaders` — за прокси App Service схема будет `http` `[код]`

Grep по всему репозиторию на `ForwardedHeaders|KnownProxies|X-Forwarded` — **ноль
совпадений**. При этом:
- [AuthController.cs:398](../../server/Swimm.API/Controllers/AuthController.cs):
  `BaseUrl() => $"{Request.Scheme}://{Request.Host}"` — отсюда строятся ссылки
  `/auth/verify-email?token=` и `/auth/reset-password?token=` → **письма уйдут с `http://`**;
- Google OAuth соберёт `redirect_uri` на `http://` → **несовпадение с Console → вход
  сломан**;
- rate-limiter партиционируется по `Connection.RemoteIpAddress` → за прокси это один адрес
  на всех → **429 прилетают соседям**, а в истории логинов `/Admin/Users` один и тот же IP.

➜ Первым middleware конвейера, до `UseHttpsRedirection`
([Program.cs:880](../../server/Swimm.API/Program.cs)):
`app.UseForwardedHeaders(...)` с `XForwardedProto | XForwardedFor` и очищенными
`KnownNetworks`/`KnownProxies` (фронтенд App Service — не приватная сеть).
Альтернатива «одной галочкой» — `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` в App Service;
делаем **оба**, но код первичен — он виден в репозитории.

> Уточнение верификатора: «петля редиректов» из старых страшилок здесь не случится —
> чинить надо схему, а не петлю.

### Б6. Data Protection не настроен — каждый деплой разлогинивает всех `[код]` + `[Влад]`

Grep на `AddDataProtection|PersistKeysTo` — ноль. При этом
[Program.cs:128–140](../../server/Swimm.API/Program.cs) регистрирует cookie `Swimm.Auth`
со сроком 7 дней, а [:112](../../server/Swimm.API/Program.cs) — antiforgery.

На App Service кольцо ключей по умолчанию живёт в файловой системе контейнера: рестарт или
деплой → новые ключи → у всех слетают сессии и **ломаются antiforgery-токены в админке**.

➜ Явно задать хранилище + `SetApplicationName("Swimm")`. Минимум для Р2: персистентный
`/home` App Service. Правильнее — Blob Storage (+ Key Vault для шифрования кольца, если
готовы платить, §10-В6).

### Б7. Живые секреты уезжают в publish-артефакт `[код]` + `[Влад]`

`server/Swimm.API/appsettings.Development.json` (gitignored, в git **не утёк** — проверено
`git log -S` по всей истории) содержит:
- `:7` → `"ClientSecret": "GOCSPX-…"` — настоящий Google-секрет, не заглушка;
- `:13` → `"ApiKey": "b3ca77…"` — ключ serper.dev;
- `DevAdminBypass`.

Web SDK гребёт `appsettings*.json` как Content — файл **уже лежит** в `bin/Release/net10.0`
и попадёт в publish. Плюс `appsettings.Local.json` подключается на
[Program.cs:25](../../server/Swimm.API/Program.cs), то есть **после** провайдера переменных
окружения → если он окажется в артефакте, он **перебьёт прод-строки подключения**.

➜ Основная мера — в csproj: `<Content Update="appsettings.Development.json;
appsettings.Local.json" CopyToPublishDirectory="Never" />` (работает и при ручном деплое).
Дополнительно — деплоить только из CI. Ключи ротировать (§10-В5).

### Б8. Четыре строки подключения с рабочими localhost-значениями закоммичены `[код]` + `[Влад]`

[appsettings.json:3–6](../../server/Swimm.API/appsettings.json) содержит непустые строки на
`Host=localhost;Port=5445`. Значит паттерн «не задал переменную → упадёт понятно» не
работает: приложение молча пойдёт в несуществующую базу. Забытая
`ConnectionStrings__AdminConnection` ещё хуже — она **фоллбечится на owner-строку**
([DependencyInjection.cs:24–26](../../server/Swimm.Infrastructure/DependencyInjection.cs)),
то есть рантайм тихо поедет под правами владельца схемы, мимо least-privilege.

Ни в одной строке нет `SslMode` — **Azure Flexible Server требует TLS**. Пул не ограничен:
три пула по 100 против ~50 соединений на B1ms.

### Б9. Миграции падают на чистой БД `[код]`

Девять безусловных `GRANT` на роль `swimm_ro`, которой на свежей Azure-БД ещё нет. Образец
правильного кода уже есть в репозитории —
[PointRulesSchema.cs:142](../../server/Swimm.Infrastructure/Migrations/20260727124030_PointRulesSchema.cs) оборачивает грант
в `DO $$ IF EXISTS (SELECT FROM pg_roles …) $$`.

`server/db/setup-roles.sql` при этом **тоже не запускается на пустой БД** — он грантует на
ещё не созданные таблицы. Плюс `:31`/`:34` хардкодят дев-пароли ролей, а `:42` —
`GRANT CONNECT ON DATABASE swimm`, то есть имя БД.

➜ Разбить на `01-roles.sql` (только `CREATE ROLE` + `CONNECT`/`USAGE` +
`ALTER DEFAULT PRIVILEGES`) и `02-grants.sql` (табличные гранты). Пароли — через
psql-переменные, значения вводит Влад. Порядок: роли → `--migrate` → гранты.

### Б10. Без `Authentication:Google:ClientSecret` логин выключается молча `[Влад]`

[Program.cs:115–118](../../server/Swimm.API/Program.cs):
`googleEnabled = !IsNullOrWhiteSpace(ClientId) && !IsNullOrWhiteSpace(ClientSecret)`.
`ClientId` лежит открыто в `appsettings.json:17`, `ClientSecret` — только в
gitignored-файле. Вместе с middleware
[:949–952](../../server/Swimm.API/Program.cs) (аноним на `/admin` редиректится **сразу на
Google**) это даёт: нет секрета → **админка недостижима, 500 вместо страницы входа**.

➜ Влад задаёт `Authentication__Google__ClientSecret` и прописывает в Google Cloud Console
redirect URI `https://<app>.azurewebsites.net/signin-google` (`CallbackPath` жёстко
`/signin-google`, [Program.cs:157](../../server/Swimm.API/Program.cs)).
Я добавляю `LogCritical` на старте, если `!googleEnabled` и не Development.

### Б11. Сценарий «прод с нуля» нереализуем `[Влад решает]`

[DependencyInjection.cs:120–139](../../server/Swimm.Infrastructure/DependencyInjection.cs)
и `RecordsSeeder` читают пять файлов — `normative.js`, `normative-masters.js`,
`normative-records.js`, `normative-age-records.js`, `normative-masters-records.js`.
**Все пять удалены** коммитом `e1dcb8e` («Phase 2.7: drop legacy normative*.js statics»)
— проверено, в `client/public/data/` остался только `filter-data.js`.

Значит `--seed-records` сегодня падает, и **6 510 нормативов взять неоткуда**. Без них
гаснет цветная шкала уровня по всей витрине. Плюс:
- 152 соревнования = ~2 часа сетевого забора **и повторный ручной дедуп** клубов
  (231→128) и пловцов-теней (41 склеена, 85 ждут) — эта работа не воспроизводится сама;
- **правила очков** (5 клубных + 2 пловца + 128 строк шкал), те самые, что свели зачёт
  Маккаби в ноль, заведены руками в админке и в коде не существуют;
- двух категорий (`results-8-99`, `result-maccabiah`) нет в `HasData` — пакетный импорт в
  проде не проставит категорию.

➜ **Рекомендую сменить Р3 на «дамп бизнес-таблиц»**: 57 МБ, десять минут, без Sys_*-таблиц
с персональными данными. Детали и альтернатива — §10-В1.

### Б12. Первого админа в проде создать нечем `[код]` + `[Влад]`

Роль выдаётся только записью в `Sys_AppUserRoles`; CLI-команды нет. После деплоя админка
недостижима до ручного `INSERT` в прод-БД.

➜ Добавить флаг `--grant-admin <email>` по образцу остальных команд (находит `AppUser`,
вставляет роль, бампает `SecurityStamp` — как это делает
[AdminRepository.cs:70–72](../../server/Swimm.Infrastructure/Repositories/AdminRepository.cs)). Тогда шаг
повторяем и документируем.

### Б13. Нет точки отката `[код]` + `[Влад]`

B1 не поддерживает слоты (Р2), а **две миграции необратимы по конструкции** — `Down` пустой
с комментарием «откат только восстановлением из бэкапа».

➜ Включить `WEBSITE_RUN_FROM_PACKAGE=1` — деплой становится атомарной подменой пакета,
откат = передеплой предыдущего zip. Написать рунбук «миграция сломала прод»: PITR в новый
сервер → переключить `ConnectionStrings__*` → передеплой предыдущего пакета.

### Б14. Персональные данные детей публикуются без единого правового документа `[Влад]`

Сайт публикует имена, годы рождения, клубы и видео несовершеннолетних (5 551 пловец). В
репозитории **нет** политики приватности, условий использования, возрастного гейта при
регистрации и удаления аккаунта. При этом
[client/public/robots.txt:3](../../client/public/robots.txt) — `Disallow:` (пустой), то есть
**явное разрешение индексировать всё**, включая поимённые результаты.

Данные взяты из открытых протоколов федерации, но публикация на своём домене с индексацией
— уже наша позиция, а не пересказ чужой. Проверить применимость израильского Закона о
защите приватности (поправка 13 действует с августа 2025) — субъекты израильские, даже если
БД в EU.

➜ Минимум до публичного запуска: закрыть от индексации, написать `/privacy` с контактом
для запросов на удаление, сделать операцию «удалить пользователя». Решения — §10-В3, В4.

---

## 4. Этапы работ

### Сделано 27.08 (A1 + A2, тесты 1464/1464, проверено на живой publish-сборке)

**A1 — клиент теперь реально едет в прод.**
- `base: '/'` в [vite.config.js](../../client/vite.config.js); устаревший запрет снят в
  [club-page-route-sonnet.md](../tasks/club-page-route-sonnet.md).
- Таргет `PublishClientApp` в [Swimm.API.csproj](../../server/Swimm.API/Swimm.API.csproj):
  `npm ci` + `npm run build` + копирование `client/dist` в `$(PublishDir)wwwroot`, только на
  Publish. ⚠ Копирование идёт `AfterTargets="Publish"`, а НЕ через `ResolvedFileToPublish`:
  первый вариант молча выбрасывал `css/admin.min.css` — админка уезжала бы без стилей.
  `data/json` и `data/excel` исключены (−15,4 МБ, `wwwroot` = 9,7 МБ).
- Серверный `wwwroot/home.html` удалён (решение Р5), строка `@source` убрана из
  `Styles/admin.css`, бандл пересобран.
  ⚠ **Пересмотрено 28.08:** страница возвращена как `wwwroot/admin-home.html` вместе со
  строкой `@source`; `launchUrl` в `launchSettings.json` переведён на неё. См. Б3.
- **Проверено на publish-сборке под `ASPNETCORE_ENVIRONMENT=Production`:** `/`, `/results`,
  `/swimmers/1`, `/clubs/1`, `/competitions/1`, `/groups/x/results`, `/season-best`,
  `/about` — все 200; пути в HTML абсолютные (`/swimmer.js`); `/data/filter-data.js` и
  `/css/admin.min.css` отдаются; главная — клиентская (`SwimHub — Home`).

**A2 — прод-швы в [Program.cs](../../server/Swimm.API/Program.cs).**
- `UseForwardedHeaders` первым в конвейере. **Проверено:** без заголовка `/results` → 307 на
  `https://`, с `X-Forwarded-Proto: https` → 200, HSTS-заголовок приходит.
- `AddDataProtection().SetApplicationName("Swimm")` + необязательный
  `DataProtection:KeysDir` (пусто = дев-поведение).
- `UseHsts` и `UseHttpsRedirection` в не-Development, **`/healthz` выведен из-под редиректа**:
  health-check App Service считает здоровым только 2xx, а 307 сочтёт отказом и начнёт
  перезапускать живой инстанс.
- `UseExceptionHandler` + `UseStatusCodePagesWithReExecute` + `/error`: для `/api/*` —
  ProblemDetails, для остального — брендированная страница. Раньше в проде был пустой экран.
- `/healthz` (только процесс), `/readyz` (статус БД из уже готового `DbStatusService` —
  пакет health-checks не нужен), `/version` (отдаёт git SHA сам, через SourceLink).
- `--grant-admin <email>` — выдача первой роли Admin. Проверены обе ветки отказа.
- Диагностика на старте (только вне Development): Critical на выключенный Google, на
  отсутствие SMTP, на `localhost` в строках подключения; Warning на незаданные строки, на
  отсутствие `SslMode`, на `AllowedHosts: "*"`, на незаданный `DataProtection:KeysDir`.
  **Проверено:** все восемь срабатывают.
- `Secure`-флаг на обе cookie-схемы и antiforgery (в Development — `SameAsRequest`).
- `appsettings.Local.json` подключается только в Development — иначе перебивал бы прод-строки.
- `LoggingEmailSender` больше не пишет тело письма в лог вне Development (внутри одноразовый
  токен сброса пароля); уровень Error вместо Information. Прикрыт тестом-сторожем
  [LoggingEmailSenderTests](../../server/Swimm.Tests/LoggingEmailSenderTests.cs).
- EF перестал печатать каждый SQL-запрос на уровне Information.

**Р6 — закрыто от индексации:** [robots.txt](../../client/public/robots.txt) + `noindex` на
`swimmer.html`, `club.html`, `groups.html`, `media.html`; после решения В3-бис — ещё и на
`results_main.html`, `competitions.html`, `season-best.html`. Итого 7 страниц, 9 путей.

**Не сделано в A3:** ротация Google ClientSecret и ключа serper.dev — это шаг Влада.

### Сделано 27.08 (A4 + A5, проверено полным прогоном на чистой БД)

**A4 — порядок на пустой базе перестал быть хрупким.**
- Все **11** грантов `swimm_ro` в миграциях обёрнуты в `DO $$ IF EXISTS pg_roles $$` — образец
  взят из уже существовавшей `PointRulesSchema`. Правка старых миграций безопасна: EF не
  сверяет их содержимое, а перезапускать применённые не будет.
- `setup-roles.sql` разбит на [01-roles.sql](../../server/db/01-roles.sql) (роли, `DO` миграций)
  и [02-grants.sql](../../server/db/02-grants.sql) (гранты, после). Ушли три хардкода: пароли
  ролей (теперь переменные psql, в репозитории их нет), имя БД `swimm` в `GRANT CONNECT`
  (теперь `current_database()`), имя владельца в `ALTER DEFAULT PRIVILEGES` (переменная).
  Старый файл не удалён, а отказывается запускаться с подсказкой — тихо сделать не то нельзя.
- **Список публичных таблиц теперь ровно один** — в `02-grants.sql`, 26 таблиц.
- **Прогон на пустой БД:** `01-roles.sql` → `--migrate` (80 миграций, 54 таблицы) →
  `02-grants.sql`. Найдено попутно: **до третьего шага `swimm_ro` не видит даже `Results`** —
  грантов из миграций одних недостаточно, шаг обязателен. `swimm_rw` при этом получает DML
  автоматически, через `ALTER DEFAULT PRIVILEGES` из первого шага — ровно поэтому он идёт
  до миграций, а не после.

**A5 — наполнение прода.**
- [seed-tables.txt](../../server/db/seed-tables.txt) — что переносится и что нет. Правило
  отбора механическое: переносим таблицу, если у неё нет FK на `Sys_AppUsers` и нет колонки
  актора (`ActorName`/`ActorUserId`/`IpAddress`/`Email`). Итог — 29 таблиц.
- [dump-seed.sh](../../server/db/dump-seed.sh) — `pg_dump --data-only` по этому списку
  (схему в проде создают миграции, она остаётся единственным источником правды). Со
  сторожем: если в дамп попала таблица с ПДн, файл удаляется и скрипт падает.
- [restore-seed.sh](../../server/db/restore-seed.sh) — заливка в одной транзакции.
- `server/db/seed-data.sql` добавлен в `.gitignore` — 14 МБ строк из боевой базы.

**Три вещи, которые всплыли только на реальном прогоне:**
1. **Циклическая FK** `Clubs.MergedIntoId → Clubs.Id` (след от склейки клубов) — pg_dump сам
   предупреждает, что `--data-only` без `--disable-triggers` не восстановится. А
   `--disable-triggers` требует **суперпользователя, которого на Azure Flexible Server нет**.
   Решение: на время загрузки делаем внешние ключи отложенными и возвращаем как было перед
   коммитом — доступно владельцу таблиц, и битые данные откатываются целиком.
2. **Шесть таблиц засевают сами миграции** (`Categories`, `Countries`, `Styles`,
   `PointRulesClubs`/`Entries`, `Sys_RecordIssues`), и локальные версии богаче: 7 категорий
   против 5 и 7 правил очков против 2. Дамп их заменяет, а не дополняет.
   ⚠ Побочно это **закрывает Б-пункт про две недостающие категории** (`results-8-99`,
   `result-maccabiah`) — отдельная миграция не нужна, они приезжают с дампом.
3. **`setval` из дампа покрыл не все последовательности** — 12 из них остались на единице.
   В проде это конфликт первичного ключа на первой же вставке. Восстановление теперь
   пересчитывает последовательности от фактического максимума.

**Проверка переноса (исходная БД → чистая прод-подобная):** `Results` 81 463, `RelayMembers`
14 070, `NormativeStandards` 6 510, `Swimmers` 5 551, `Records` 1 685, `Clubs` 235,
`Competitions` 152 — совпало всё. Правила очков (7 + 128 строк шкал) и решения дедупа (16)
доехали. `Sys_AppUsers`, `Sys_UserMedia`, `Sys_UserFavorites`, `Sys_AdminAudit`,
`Sys_UserLoginHistory`, `HubGroups`, `Sys_TrainingResults` — по нулям.

**Что НЕ переносится и требует ручного шага в проде:** `HubGroups` — единственная FK через
границу (`OwnerUserId NOT NULL → Sys_AppUsers`). Локально это одна тестовая группа на 19
участников; в проде создаётся заново после первого входа.

**Индексация (В3-бис):** закрыты и `/results`, и `/competitions`. Заодно `/season-best` — он
рендерит те же имена. Итого 7 страниц с `noindex` и 9 путей в `robots.txt`.

### Сделано 27.08 (A6, решение В13: publish-profile и только по кнопке)

Два раздельных пайплайна — CI обязан зеленеть на PR, деплой запускается руками.

**[ci.yml](../../.github/workflows/ci.yml)** — на PR и push в master, три параллельных джоба:
- `server` — restore (с кэшем NuGet) → build → `dotnet test`. **Postgres в CI не поднимаем:**
  два PG-теста сами отключаются, когда база недоступна, остальным она не нужна.
- `client` — `npm ci` → `npm run typecheck` → `npm run build`. Гейт типов включён **жёстким**:
  прогнал `tsc --noEmit` перед этим — **ноль ошибок**, так что включать безопасно.
  Добавлен скрипт `typecheck` в `client/package.json`; `vite build` типы не проверяет, он их
  стирает, поэтому без этого шага ошибка типа доезжает до прода.
- `admin-css` — пересобирает бандл админки на Linux и требует совпадения с закоммиченным.
  Ловит «забыл `npm run css:build` после новых Tailwind-классов».

**[azure-deploy.yml](../../.github/workflows/azure-deploy.yml)** — только `workflow_dispatch`:
- `dotnet publish` с `-p:InformationalVersion=${{ github.sha }}` (это же значение потом
  отдаёт `/version`). Клиент собирается таргетом из csproj, отдельным шагом его тут НЕ
  собираем — чтобы путь до артефакта был ровно тот же, что при локальном `dotnet publish`.
- **Проверка артефакта до выкладки** — кодифицированы ровно те две поломки, что уже случались
  вживую: не меньше 8 html-страниц в `wwwroot`, наличие `admin.min.css` / `swimmer.js` /
  `filter-data.js`, отсутствие `appsettings.Development.json` и `.Local.json`, абсолютные пути
  к ассетам. Проверил на настоящем артефакте (проходит) и на заведомо битом (падает по всем
  пунктам) — сторож умеет и то, и другое.
- Выкладка `azure/webapps-deploy@v3` по publish-profile.
- **Дымовая проверка после деплоя:** ждёт `200` от `/healthz` (до 5 минут — на B1 холодный
  старт), сверяет SHA в `/version`, смотрит `/readyz` и пишет сводку в Summary прогона.

**[.gitattributes](../../.gitattributes)** — новый, из-за сторожа бандла: при `core.autocrlf=true`
рабочая копия на Windows была бы с CRLF, сборка на раннере — с LF, и джоб `admin-css` падал бы
на пустом месте. Правило узкое (бандл + `server/db/*.sh`): общее `*.sql eol=lf`
перенормализовало бы давно лежащие в репозитории файлы и дало бы дифф не по делу.

**Джоба миграций сознательно НЕТ.** Она упирается в нерешённый В8 — у раннеров GitHub
динамические IP, и достучаться до Flexible Server нечем, пока не выбран способ. Выкладывать
workflow, который заведомо падает, хуже, чем не выкладывать: порядок ручного применения
описан в §6, а джоб добавляется одним движением, как только В8 закрыт.

**Что нужно от Влада, чтобы деплой поехал:**
- secret `AZURE_WEBAPP_PUBLISH_PROFILE` — кнопка «Get publish profile» на странице App Service;
- variable `AZURE_WEBAPP_NAME` — имя App Service (можно вместо этого вводить руками при запуске);
- удалить протухший secret `AZURE_STATIC_WEB_APPS_API_TOKEN_BLUE_TREE_0E916EB10`.


### A0. Pre-flight ◐ `[Влад — одна команда]`

```bash
az webapp list-runtimes --os-type linux --query "[?contains(@,'DOTNET')]" -o tsv
```

Есть `DOTNETCORE:10.0` → идём framework-dependent, ничего не меняем. Нет → два запасных
пути: `--self-contained -r linux-x64` (толще артефакт) или контейнер (нужен Dockerfile).

Параллельно я: `dotnet publish -c Release` локально (ни разу в этом репо не запускался —
папки publish не существует), замер размера артефакта, сборка `npm run build` в `client/`.

**Приёмка:** известна форма деплоя; локально лежит publish-папка с понятным содержимым.

### A1. Сборка клиента внутрь API ✅ (27.08) `[код]` — закрыл Б1, Б2, Б3

1. `base: '/'` в `vite.config.js`, снять устаревший запрет в
   `docs/tasks/club-page-route-sonnet.md:123–124`.
2. Шаг публикации: `npm ci && npm run build` в `client/` → копирование `client/dist/**` в
   `wwwroot` publish-выхода. **Только на publish.**
3. Решить коллизию `home.html` (§10-В2); если уезжает серверный — убрать его из
   `@source`-скана в `Styles/admin.css`.
4. Исключить из копирования `client/public/data/json` и `/excel` (−15,4 МБ), `index.html`,
   `helpers.html`.

**Приёмка:** локальный запуск publish-сборки отдаёт `/`, `/results`, `/swimmers/1`,
`/clubs/1`, `/competitions/1`, `/groups/x/results`, `/season-best` — **без единого 404 в
Network**, эмблемы клубов и иконки стилей на месте.

### A2. Прод-швы в коде ✅ (27.08) `[код]` — закрыл Б5, Б12; Б6 и Б13 — код готов, ждут переменных

- `UseForwardedHeaders` первым в конвейере.
- `AddDataProtection().SetApplicationName("Swimm").PersistKeysTo…`.
- `UseHsts()` в не-Development ветке.
- `AddProblemDetails()` + `UseExceptionHandler()` + `UseStatusCodePages` — сейчас в
  Production пользователь при ошибке видит **пустой экран**.
- `/healthz` — только процесс, БД не трогает (для health-check App Service);
  `/readyz` — отдаёт `DbStatusService.IsAvailable` (для алертов). Оба исключить из
  maintenance-middleware, иначе в режиме заглушки health-check уронит инстанс.
  ⚠️ Пакет `AddHealthChecks` не нужен: статус БД уже лежит в singleton.
- `/version` → `InformationalVersion` (в workflow передавать `-p:InformationalVersion=${{ github.sha }}`).
- `--grant-admin <email>`.
- Fail-fast/Critical на старте: `!googleEnabled` в Production; `IEmailSender` оказался
  `LoggingEmailSender`; не заданы `AdminConnection`/`ReadConnection`.
- В `LoggingEmailSender` убрать тело письма из лога (оставить To+Subject) — сейчас
  одноразовый токен сброса пароля печатается в лог целиком.
- `Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command = Warning` — иначе EF
  печатает **каждый SQL-запрос** на уровне Information.

**Приёмка:** запуск с `ASPNETCORE_ENVIRONMENT=Production` за локальным прокси —
`Request.Scheme` = https, `/healthz` 200, без Google-секрета в логе видно Critical.

### A3. Гигиена секретов ◐ `[код]` + `[Влад]` — код закрыл Б7 и диагностику Б8; ротация за Владом

- csproj: `CopyToPublishDirectory="Never"` для `appsettings.Development.json` и
  `appsettings.Local.json`.
- `appsettings.Local.json` подключать только в Development (сейчас он перебивает
  переменные окружения — [Program.cs:25](../../server/Swimm.API/Program.cs)).
- Вычистить `ConnectionStrings` из `appsettings.json` или добавить fail-fast «не
  Development + `Host=localhost` → исключение».
- Дописать `SslMode=Require` и `Maximum Pool Size` (ro=20, rw=10, migration=2) в шаблон
  прод-строк.
- **Влад:** ротировать Google `ClientSecret` и ключ serper.dev.

### A4. БД: роли, миграции, гранты ✅ (27.08) `[код]` — закрыл Б9

Разбить `setup-roles.sql` на `01-roles.sql` / `02-grants.sql`, параметризовать пароли,
убрать хардкод имени БД. Обернуть девять `GRANT` в миграциях в `DO $$ IF EXISTS pg_roles $$`
(правка старых миграций безопасна — они применялись только на локальной БД).

### A5. Наполнение прод-данных ✅ (27.08) `[код]` — закрыл Б11

По решению §10-В1. Если дамп: написать `server/db/dump-prod-seed.sh` (pg_dump с
`--exclude-table` на все `Sys_*` и `tmp_*`) и `restore-to-azure.sh` (`sslmode=require`).
Отдельно разобрать единственную FK через границу — `HubGroups` → `Sys_AppUsers`.

### A6. CI/CD ✅ (27.08) `[код]` — закрыл Б4; за Владом остались secret и variable

Два workflow:
- **`ci.yml`** (on: pull_request, push): job `server` — checkout `depth:1` → setup-dotnet
  10.0.x → кэш `~/.nuget` → restore → build -c Release → `dotnet test --no-build`;
  job `client` (параллельно) — setup-node 20 + кэш npm → `npm ci` → `tsc --noEmit` →
  `vite build`. ⚠️ Перед включением `tsc --noEmit` как гейта — прогнать один раз локально
  и посмотреть, сколько ошибок накопилось (TS 4.9, типы давно никто не проверял).
- **`azure-app-service.yml`** (master + `workflow_dispatch`): publish → `azure/webapps-deploy@v3`.
  **Отдельный ручной job `migrate`**, не в основном пайплайне.
- Сторож admin-CSS: `npm run css:build && git diff --exit-code -- wwwroot/css/admin.min.css`
  — падение = «забыл пересобрать CSS».

Открытый вопрос — как job миграции достучится до Flexible Server (§10-В8).

### A7. Ресурсы в Azure ☐ `[только Влад]`

Точные команды и чек-лист готовлю я. **Четыре параметра выбираются один раз и меняются
только пересозданием сервера:** режим сети, geo-redundant backup, локаль БД, регион.

### A8. Первый запуск и приёмка ☐

Порядок — §6. Прокликать: главная, `/results`, `/competitions/{id}`, `/swimmers/{id}`,
`/clubs/{id}`, `/groups/{slug}`, `/season-best`, чистые URL, light/dark, RTL и ивритские
названия клубов; Google-логин end-to-end; register→verify→login→favorites→logout-all;
`/Admin/*` под админом и 403 под обычным; один импорт XLSX (единственный путь, где вообще
исполняется ClosedXML); `/Admin/Health` — реестр проверок целостности зелёный.

### A9. Документация ☐ `[код]`

`docs/deploy.md` — единственный источник порядка первого запуска, полного списка переменных,
рунбука отката и правила «ровно один инстанс». Раздел «Хостинг» в ROADMAP → ✅ + фактические
имена ресурсов.

---

## 5. Реестр переменных окружения App Service

27 ключей, которые код действительно читает. Собрано grep'ом по всему `server/`.
`Environment.GetEnvironmentVariable` в не-тестовом коде **не используется ни разу** — весь
конфиг идёт через `IConfiguration`, схема `__` работает везде.

### Секреты — задать обязательно

| Переменная | Что сломается без неё |
|---|---|
| `ConnectionStrings__DefaultConnection` | всё |
| `ConnectionStrings__MigrationConnection` | миграции |
| `ConnectionStrings__AdminConnection` | тихо поедет под owner-ролью (Б8) |
| `ConnectionStrings__ReadConnection` | то же |
| `Authentication__Google__ClientSecret` | вход + админка (Б10) |
| `Email__Smtp__User`, `Email__Smtp__Password` | письма |
| `CandidateSearch__ApiKey` | поиск кандидатов loglig тихо отключится |

Все четыре строки — с `SslMode=Require` и `Maximum Pool Size`.

### Не секреты, но задать

`Authentication__Google__ClientId` · `Email__Smtp__Host` (**сам переключатель SMTP/лог**) ·
`Email__Smtp__Port` (587) · `Email__Smtp__EnableSsl` (true) · `Email__Smtp__From` ·
`Email__Smtp__FromName` · `AllowedHosts` (сузить с `*` — иначе host-injection в ссылку
сброса пароля) · `PublicSite__BaseUrl` (в проде пуст → ломает ссылки на публичный сайт из
админки) · `Loglig__SeasonId` (дефолт 1715 продублирован в **двух** местах, протухает раз в
год) · `ASPNETCORE_ENVIRONMENT=Production` · `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` ·
`WEBSITE_RUN_FROM_PACKAGE=1` · `WEBSITE_TIME_ZONE=Asia/Jerusalem` (§10-В10) ·
`Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Warning`.

Плюс в портале: **Always On = вкл** (без него импорт и пакетный забор, живущие в памяти
процесса, будут обрываться на середине), **Scale out = 1 инстанс**, autoscale выключен.

### Можно оставить по дефолту

`RecordsImport__WorldAquaticsNationalCountryId` (Израиль) ·
`RecordsImport__IsrOrg*Url*` (пусто = автопоиск ссылок со страницы) ·
`Discovery__SnapshotDir` (пусто = снапшоты выключены) · `Logging__LogLevel__Default`.

### Не задавать

`DevAdminBypass` · `CandidateSearch__Provider` (мёртвый ключ, не читается нигде).

### Исходящий трафик, который нужен приложению

`api.worldaquatics.com` · `isr.org.il` · `accounts.google.com` · `google.serper.dev` ·
`www.youtube.com/oembed`, `vimeo.com/api/oembed.json` · **`loglig.com:2053` —
нестандартный порт**, проверить до деплоя (§10-В11).

---

## 6. Порядок первого запуска

Единственный источник. Шаги взаимозависимы — не переставлять.

1. **Создать** Flexible Server (регион = регион App Service; версия PG; локаль
   `ENCODING 'UTF8' LC_COLLATE 'en_US.utf8' LC_CTYPE 'en_US.utf8'` — паритет с локальной,
   иначе поедет сортировка имён) и БД. Админ-логин назвать **`swimm`** — тогда
   `MigrationConnection` и `ALTER DEFAULT PRIVILEGES FOR ROLE swimm` работают без правок.
2. **Роли:** `01-roles.sql` с реальными паролями (без табличных грантов).
3. **Миграции:** `--migrate`.
4. **Гранты:** `02-grants.sql`.
5. **Данные:** восстановление дампа бизнес-таблиц (или сидеры — по решению В1).
6. **App Service:** задать все переменные §5, Always On, Scale out = 1.
7. **Деплой** пакета из GitHub Actions.
8. **Google Console:** redirect URI `https://<app>.azurewebsites.net/signin-google`.
9. **Войти** через Google на проде (создастся `Sys_AppUsers`), затем `--grant-admin <email>`.
10. **Приёмка** A8.

---

## 7. Кто что делает

**Я (проверяемо локально):** A1, A2, A3-код, A4, A5-скрипты, A6, A9 + точные команды и
чек-лист для A7, разбор логов по итогам A8.

**Только Влад:** `az login` и создание ресурсов; **все секреты** (строки подключения, пароли
ролей, SMTP-пароль, Google client secret, publish-profile/OIDC); прогон SQL на прод-БД;
redirect URI в Google Console; удаление Static Web App и старого хлама; ротация ключей;
решения §10.

---

## 8. Риски

**8.1. Всё состояние — в памяти одного процесса.** Настройки админки
([AdminSettingsService.cs:8–11](../../server/Swimm.Infrastructure/Services/AdminSettingsService.cs): «значения
сбрасываются при перезапуске»), кэш, очередь импорта, состояние пакетного забора. Следствия:
**`MaintenanceMode` сбрасывается в `false` при каждом деплое** — заглушка снимается с
публичного сайта сама; при scale-out фоновые сервисы задублируют записи в БД. ➜ Жёсткое
правило «ровно один инстанс» + перенос хотя бы `MaintenanceMode` в БД (§10-В7).

**8.2. Ночная сверка loglig включена жёстко и стартует немедленно.**
`LogligVerifyEnabled` читается со значением по умолчанию `true`, но самого ключа **нет в
реестре настроек** — из админки его не выключить. При каждом рестарте инстанса джоб сразу
идёт на чужой сайт `loglig.com:2053` и пишет в БД. ➜ Досеять ключи, добавить стартовую
задержку и джиттер.

**8.3. 230-секундный таймаут App Service.** Долгие синхронные админ-операции (stamp-loglig,
пересчёт standings) в него не уложатся и отдадут 502. Пакетный импорт уже устроен правильно
(фон + poll) — есть с чего копировать.

**8.4. Cold start на B1.** После рестарта кэши пустые, `/api/athletes/career` на холодную
~5 с (известный остаток фазы 3.5). Не блокер, но на приёмке не пугаться.

**8.5. Письма без SPF/DKIM/DMARC уедут в спам** — это ломает и регистрацию, и сброс пароля.
Настраивается на домене отправителя, то есть упирается в §10-В12.

**8.6. Мониторинга нет** — о падении узнаете, открыв сайт.

**8.7. Косметика, которая уедет в прод как есть:** `/about` отдаёт заглушку
«ABOUT 11 : undefined / undefined», два пункта меню без ссылок, ни одной og-метки.

**8.8. Не трогать `IncludeAssets` у EF Core Design.** В publish-выход тащится ~59 МБ
Roslyn/MSBuild (включая `BuildHost-net472` — хост для .NET Framework 4.7.2, на Linux мёртвый
груз). Соблазн убрать `runtime` из `IncludeAssets` **сломает `dotnet ef`**: инструменты EF
ищут `Design.dll` именно среди runtime-copy-local ассетов. Разметка — штатный шаблон NuGet
для developmentDependency. Оставить как есть; 59 МБ — цена, а не баг.

---

## 9. Стоимость (West Europe, pay-as-you-go, USD/мес)

| Позиция | Ориентир |
|---|---|
| App Service B1 Linux | ≈ 13 |
| Flexible Server B1ms (1 vCore / 2 GiB) | ≈ 12–13 compute + ≈ 4 storage (32 ГБ) |
| Storage Account под ключи Data Protection | < 1 |
| **Итого по Р2** | **≈ 30** |
| *Опционально:* Application Insights | 5 ГБ/мес бесплатно, дальше ≈ 2,3/ГБ |
| *Опционально:* availability-тест | 1–3 |
| *Опционально:* S1 ради слотов и мгновенного отката | +≈ 56 |

Egress: первые 100 ГБ/мес бесплатно. ➜ Завести **бюджетный алерт** в Azure — это бесплатно
и снимает главный страх pay-as-you-go.

---

## 10. Решения, ждущие Влада

**В1. Наполнение прода.** ✅ **РЕШЕНО 27.08: дамп бизнес-таблиц.** `--seed-records` мёртв (Б11).
→ **Рекомендую: дамп бизнес-таблиц** (57 МБ, ~10 минут, `--exclude-table` на 28 `Sys_*`).
Альтернатива «с нуля» стоит ~2 часа забора **плюс повторение ручного дедупа**, который не
воспроизводится, и всё равно не даёт 6 510 нормативов и правил очков.
*Если всё-таки «с нуля»* — надо восстановить пять `normative*.js` из `e1dcb8e^` (класть в
`server/db/seed/records/`, **не** в `client/public` — иначе снова уедут в dist) и дописать
`isrorg-masters` в порядок `--records-refresh` (иначе 726 из 1 685 рекордов не приедут).

**В2. Главная страница прода.** ✅ **РЕШЕНО 27.08: клиентская (EN, React).** Была ли она — серверная (RU, Tailwind) или клиентская (EN, React)?
→ **Рекомендую клиентскую**: по правилу проекта UI только English, серверная главная на
русском. Тогда же — судьба баннера «БД недоступна» (он есть только в серверной).

**В3-бис. Индексация списков результатов.** ✅ **РЕШЕНО 27.08: закрыть.** Выполнено для
`/results`, `/competitions` и `/season-best`.

**В3. Индексация.** ✅ **РЕШЕНО 27.08: на старте закрыть.** → **Рекомендую на старте закрыть** (`noindex` +
`Disallow` на `/swimmers/`, `/clubs/`, `/groups/`, `/my-media`, `/Admin/`). SEO всё равно не
работает — мета-тегов нет ни одного, а Б14 стоит острее.

**В4. Кто юридически оператор данных** — физлицо или под это заводится структура? Без ответа
нельзя написать политику приватности. Плюс: аккаунты для родителей/тренеров или дети
регистрируются сами (от этого зависит форма регистрации и текст условий)?

**В5. Ротировать Google ClientSecret и ключ serper.dev сейчас?** В git они не утекли — это
гигиена, а не инцидент. Но если первый деплой пойдёт zip'ом с машины, ротировать **до** него.

**В6. Data Protection:** Blob Storage + `SetApplicationName` (дёшево, закрывает главный риск,
ключи в блобе незашифрованы) или ещё и Key Vault?

**В7. Настройки админки:** быстро (стартовые значения из App Service Settings) или правильно
(таблица по образцу готового `Sys_DebugOptions`, ~день работы)?

**В8. Как job миграции достучится до Flexible Server?** У раннеров GitHub динамические IP.
Варианты: (а) job сам открывает/закрывает временное firewall-правило через `az` (нужен OIDC
и Contributor на сервере); (б) миграция по SSH в контейнер App Service; (в) вручную с
домашнего IP. → **Рекомендую (в) на первый запуск, (а) — когда пойдут регулярные релизы.**

**В9. Режим сети Flexible Server** — public access + firewall (проще, дешевле) или
VNet/private endpoint (безопаснее)? **Несменяемо после создания.** Отдельно: «Allow public
access from any Azure service» — **не включать**, это дыра шириной во весь Azure.
Плюс: geo-redundant backup (тоже несменяемо), retention 7 → 14–35 дней, версия PG (локально
16.14 — брать 16 или сразу 17?), и можно ли дропнуть две `tmp_*`-таблицы перед дампом.

**В10. Регион.** С 2024 есть **Israel Central** — быстрее для аудитории, данные остаются в
стране субъектов; обычно на 10–20% дороже West Europe. → Решение разовое, меняется только
пересозданием всего. Плюс `WEBSITE_TIME_ZONE=Asia/Jerusalem`: контейнер живёт в UTC, а
статусы соревнований «live/upcoming» считаются от локальной даты.

**В11. Разрешён ли с App Service исходящий трафик на `loglig.com:2053`?** Проверить **до**
первого деплоя: если порт режется, отвалятся сверка зачёта, привязка loglig-id и автозабор
регламентов. Заодно — нужно ли уведомить isr.org.il/loglig о регулярном доступе с
облачного IP.

**В12. SMTP-провайдер и адрес отправителя?** Resend / Postmark / SendGrid / Azure
Communication Services — все дают SMTP-endpoint, под который написан `SmtpEmailSender`.
Упирается в домен (SPF/DKIM), а домен по Р1 ещё не куплен — то есть на старте локальный вход
(регистрация + сброс пароля) может остаться нерабочим, а вход через Google — работать.
**Это надо принять сознательно.**

**В13. Форма деплоя.** ✅ **РЕШЕНО 27.08: publish-profile и только по кнопке.** Реализовано в A6.

**В14. Мелочи:** удалить `client/public/data/json` и `/excel` (15,4 МБ, ни один модуль их не
читает)? Добавлять ли `postgres:16` в CI ради двух PG-тестов? Нужен ли Application Insights
сразу?

---

## 11. Не в этом плане

- Кастомный домен + TLS + www-редирект (Р1), SPF/DKIM/DMARC.
- Staging-слот и обкатка миграций на копии (компромисс: поднять временный B1ms того же
  региона на сутки, прогнать §6 целиком, удалить — стоит центы).
- Redis вместо `MemoryCacheService`, лидер-выбор для фоновых сервисов, autoscale.
- Перенос `client/public/images` в Blob Storage + CDN.
- Ретеншен IP-адресов фоновым сервисом (сейчас чистка срабатывает, только когда админ
  откроет страницу) и чистка `Sys_AdminAudit`.
- Наполнение `/about`, og-метки, брендированные страницы 404/500.
