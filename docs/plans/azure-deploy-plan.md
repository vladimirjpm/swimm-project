# План — деплой на Azure (App Service + Flexible PostgreSQL)

Составлен 2026-07-29 (Влад в офлайне; решения, требующие его слова, собраны в §3).
Решение о хостинге зафиксировано в [ROADMAP.md](../ROADMAP.md#хостинг--решение-2026-07-15-зафиксировано-не-реализовано):
**весь стек на Azure, один origin**, App Service раздаёт прод-сборку клиента из `wwwroot`.

Статусы: ☐ не начато · ◐ в работе · ✅ готово

---

## 1. Рамка

**Цель:** `git push` в `master` → рабочий сайт на Azure: API, клиент, админка, Google-логин,
email. Миграции — **явный шаг**, не автостарт.

**Не в этом плане:** кастомный домен/CDN, staging-слот, мониторинг/алерты, автобэкапы сверх
дефолтных, перенос данных из локальной БД (отдельная задача — см. §7).

---

## 2. Что обнаружено в репозитории (это меняет объём работ)

### 2.1 Уже есть два деплоя, и оба противоречат решению

| Файл | Куда | Проблема |
|---|---|---|
| [deploy.yml](../../.github/workflows/deploy.yml) | GitHub Pages, только клиент (`npm run build`) | сайт без API; ещё и `build`, а не `build:azure` (видео не вырезаны) |
| [azure-static-web-apps-blue-tree-0e916eb10.yml](../../.github/workflows/azure-static-web-apps-blue-tree-0e916eb10.yml) | Azure Static Web Apps, только клиент (`build:azure`) | **второй origin** — ломает cookie-auth |

Почему второй origin ломает логин — подтверждается кодом: `Program.cs:105,120` ставит
`SameSite=Lax` на auth-cookie. С отдельного фронт-хоста куки на `/api` не поедут, потребуется
`SameSite=None` + CORS с credentials + правка OAuth-редиректов. Именно это решение и было
отвергнуто в роадмапе. Значит **оба workflow подлежат удалению**, а не доработке.

Существует и Azure-ресурс Static Web App (`blue-tree-0e916eb10`) + секрет
`AZURE_STATIC_WEB_APPS_API_TOKEN_BLUE_TREE_0E916EB10` в GitHub — решение о его судьбе в §3.1.

### 2.2 Коллизия `home.html` — в прод поедут ДВЕ разные главные

- `server/Swimm.API/wwwroot/home.html` — RU, Tailwind-бандл админки (`/css/admin.min.css`),
  заголовок «Swimm — Home», баннер «БД недоступна», блок `#auth-area`.
- `client/home.html` → `dist/home.html` — EN, React (`src/pages/home-page.tsx`),
  «SwimHub — Home», тёмный фон `#020a18`.

`app.UseDefaultFiles` (`Program.cs:463`) ищет **`home.html` первым**, а публикация клиента в
`wwwroot` затрёт серверный файл. То есть корень сайта молча сменит владельца. Решение — §3.2.

⚠️ Побочно: серверный `home.html` — единственный потребитель `admin.min.css` вне `/Admin/*`
(см. корневой CLAUDE.md). Если он уезжает, ссылка на бандл из него уходит вместе с ним, но
`@source`-скан в `Styles/admin.css` продолжит его сканировать — надо будет поправить.

### 2.3 `wwwroot` сейчас почти пуст

`server/Swimm.API/wwwroot/` = `css/` + `home.html`. Сборки клиента там нет и в git её нет
(`dist/` в `.gitignore`). Значит **шаг «скопировать `client/dist` → `wwwroot`» надо написать** —
сам по себе `dotnet publish` клиента не видит.

### 2.4 Конфигурация: что придётся отдать в переменные окружения

Из [appsettings.json](../../server/Swimm.API/appsettings.json) — **четыре** строки подключения
под разные роли (не одна!):

| Ключ | env-имя для App Service | Роль в БД |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | `swimm` (owner) |
| `ConnectionStrings:MigrationConnection` | `ConnectionStrings__MigrationConnection` | `swimm` (owner) |
| `ConnectionStrings:AdminConnection` | `ConnectionStrings__AdminConnection` | `swimm_rw` |
| `ConnectionStrings:ReadConnection` | `ConnectionStrings__ReadConnection` | `swimm_ro` |
| `Authentication:Google:ClientSecret` | `Authentication__Google__ClientSecret` | — (сейчас только в user-secrets) |
| `Email:Smtp:*` | `Email__Smtp__*` | — (без них `LoggingEmailSender`) |

`Authentication:Google:ClientId` лежит в `appsettings.json` открыто — это нормально (не секрет),
но прод-`ClientSecret` в репозиторий не попадает и не должен.
`googleEnabled` (`Program.cs:92`) = ClientId **и** ClientSecret непусты → **без секрета в
проде Google-логин просто выключится молча**. Это надо проверять на приёмке явно.

### 2.5 Риск номер один: `net10.0` на App Service

`Swimm.API.csproj` → `<TargetFramework>net10.0</TargetFramework>`. Перед созданием ресурса надо
**проверить, что нужный рантайм есть в App Service Linux** (`az webapp list-runtimes`). Варианты
на случай отсутствия — §6.1. Это единственный пункт, способный поменять форму деплоя целиком,
поэтому он идёт в pre-flight, а не в конец.

### 2.6 Мелочи, которые сэкономят час отладки

- `BuildAdminCss` в csproj выполняется **только если есть `server/Swimm.API/node_modules`**.
  В CI его не будет → таргет скипнется → возьмётся закоммиченный `admin.min.css`. Это
  штатный, задокументированный путь; в CI Node для админ-CSS ставить **не нужно**.
- `UseAppHost=false` стоит только для `Debug` — publish соберёт нормальный хост, ок.
- Чистые URL в проде уже реализованы (`Program.cs:410–461`, третье зеркало контракта) —
  писать заново не нужно, но проверить на живом (`/swimmers/123`, `/groups/x/results`) обязательно.
- Middleware `/admin` для анонима редиректит **сразу на Google** (`Program.cs:392`) — если в
  проде Google выключен (нет секрета), админка станет недостижимой. Связано с 2.4.
- Health-эндпоинта нет; ближайшее — `/api/db-status` (`AllowAnonymous`) и `/auth/me`.
  Для health-check App Service лучше завести отдельный (§4, A5).
- `AllowedHosts: "*"` — на проде стоит сузить до реального хоста.

---

## 3. Решения, которые за Владом (мои рекомендации — первым вариантом)

**3.1. Что делать с существующим Static Web App `blue-tree-0e916eb10`?**
→ **Рекомендую: удалить ресурс в Azure и оба workflow из репо.** Он раздаёт клиент без API —
то есть заведомо нерабочую версию сайта, которая при этом живёт по публичному URL и может
индексироваться. Альтернатива (оставить как «витрину-заглушку») требует явного смысла.
GitHub Pages-деплой — удалить безусловно.

**3.2. Какая главная страница у прода — серверная (RU, Tailwind) или клиентская (EN, React)?**
→ **Рекомендую: клиентская React-главная** (`client/home.html`), а серверную удалить/переименовать.
Причины: UI проекта по правилу — **только English**, серверная главная на русском и с
«Swimming Results Platform / Результаты соревнований»; клиентская — часть общего дизайна.
Тогда же: убрать `home.html` из `@source`-скана `Styles/admin.css` и решить судьбу баннера
«БД недоступна» (сейчас он есть только в серверной главной).
*Вариант Б:* оставить серверную как лендинг, а React-главную публиковать под другим именем —
но это разъезд двух главных страниц, который придётся поддерживать.

**3.3. Порядок миграций при деплое.**
→ **Рекомендую на первом запуске: миграция отдельным ручным шагом** (`workflow_dispatch`-джоб
или локальный `dotnet run -- --migrate` против прод-строки), деплой кода — отдельно. Дальше,
когда пойдут регулярные релизы, — «остановить → миграция → деплой → запустить» либо
обратно-совместимые миграции без простоя. Автомиграцию на старте не включаем (решение роадмапа).

**3.4. Ветка-триггер деплоя.**
→ **Рекомендую `master` + `workflow_dispatch`.** Текущая рабочая ветка —
`feature/point-rules-schema`, и в ней есть незакоммиченное; деплоить с фичеветок не надо.

**3.5. Регион.**
→ **West Europe** для App Service и Flexible Server (в одном регионе — требование
перф-бюджета из роадмапа).

**3.6. Данные в проде: с нуля или перенос локальной БД?**
→ **Рекомендую: с нуля + сидеры/импорт** (`--seed-records`, затем импорт протоколов из админки).
Перенос дампа — отдельная задача, там надо решать судьбу синтетики и `Sys_*`-таблиц с личными
данными. См. §7.

---

## 4. Этапы

### A0. Pre-flight (без Azure-аккаунта, делаю сам) ☐
- Проверить, что `dotnet publish -c Release` собирает API целиком и что `admin.min.css`
  попадает в publish без Node.
- Собрать `npm --prefix client run build:azure`, посмотреть размер `dist` и убедиться, что
  `remove-azure-videos.mjs` реально срезает вес.
- Зафиксировать список env-переменных (§2.4) в чек-листе.
- **Приёмка:** локально в одной папке лежит publish API + `dist` клиента; понятно, что копировать.

### A1. Публикация клиента внутрь API ☐
- MSBuild-таргет (или шаг в workflow) «`npm ci` + `build:azure` в `client/` → копировать
  `client/dist/**` в `$(PublishDir)wwwroot`» — **только на publish**, не на каждый `dotnet build`
  (иначе локальная разработка станет медленной и полезет build-lock).
- Разрешить коллизию `home.html` по решению §3.2.
- **Приёмка:** `dotnet publish` даёт папку, где `wwwroot` содержит все 8 html-страниц клиента
  + `css/admin.min.css`; локальный запуск publish-сборки отдаёт `/`, `/results`, `/swimmers/1`,
  `/groups/x/results` и `/Admin/*`.

### A2. Прод-конфиг и швы ☐
- Убрать из `appsettings.json` всё, что должно приходить извне; `appsettings.Production.json`
  либо не заводить, либо только с неsecret-значениями.
- Сузить `AllowedHosts`, проверить HTTPS-redirect/HSTS для Production.
- Явная диагностика на старте: если `googleEnabled == false` в Production — писать
  **warning в лог** (чтобы 2.4 не выстрелил молча).
- **Приёмка:** локальный запуск с `ASPNETCORE_ENVIRONMENT=Production` и переменными окружения
  вместо `appsettings` работает; без Google-секрета в логе видно предупреждение.

### A3. Один workflow вместо двух ☐
- Удалить `deploy.yml` и SWA-workflow (§3.1).
- Новый `azure-app-service.yml`: `master` + `workflow_dispatch` → setup-node + setup-dotnet →
  `npm ci && npm run build:azure` → `dotnet publish -c Release` → `azure/webapps-deploy@v3`
  с publish-profile или OIDC.
- Отдельный **ручной** джоб `migrate` (§3.3), не в основном пайплайне.
- **Приёмка:** workflow проходит на пуш в `master` и деплоит; `migrate` запускается только руками.

### A4. Ресурсы в Azure — **шаги Влада** (я готовлю точные команды/чек-лист) ☐
- App Service Linux B1 (рантайм по итогам A0/§2.5) + Flexible Server B1ms, оба West Europe.
- `server/db/setup-roles.sql` под owner на прод-БД; 4 строки подключения в App Service Config.
- Secrets: `Authentication__Google__ClientSecret`, `Email__Smtp__*`, publish-profile/OIDC в
  GitHub Secrets. **Я эти значения не ввожу и не вижу.**
- Прод-redirect URI в Google OAuth Console.

### A5. Первый запуск и приёмка на живом ☐
- `--migrate` вручную → `--seed-records` → выдать себе роль Admin.
- Health-эндпоинт (`/healthz`: процесс + БД) и настройка health-check App Service.
- Прокликать: главная, `/results` (paged на прод-данных), `/competitions/{id}`, `/swimmers/{id}`,
  `/groups/{slug}`, чистые URL, light/dark, RTL; Google-логин end-to-end;
  register→verify→login→favorites→logout-all (критерий приёмки фазы 4); `/Admin/*` под админом
  и 403 под обычным юзером.
- **Приёмка:** все пункты зелёные, в логах нет предупреждения про Google.

### A6. Документация ☐
- Раздел «Хостинг» в ROADMAP → статус ✅ + фактические имена ресурсов.
- `docs/deploy.md`: как деплоить, как накатывать миграции, где секреты, как откатиться.
- **Приёмка:** по документу деплой повторяем без меня.

---

## 5. Что делаю я / что можешь только ты

**Я (код, конфиг, CI — всё проверяемо локально):** A0, A1, A2, A3, A6 + подготовка точных
команд и чек-листа для A4, разбор логов и фиксы по итогам A5.

**Только ты:** `az login` и создание ресурсов, **все секреты** (строки подключения, SMTP-пароль,
Google client secret, publish-profile), прогон `setup-roles.sql` на прод-БД, прод-redirect URI
в Google Console, удаление Static Web App, выдача себе роли Admin в проде.

---

## 6. Риски

**6.1. `net10.0` может не поддерживаться App Service** (§2.5). Если рантайма нет:
(а) self-contained publish (`--self-contained -r linux-x64`) — работает почти всегда, но толще
артефакт; (б) контейнер (Dockerfile + App Service for Containers) — гибче, но добавляет
registry и сборку образа. Выбор — по факту проверки в A0.

**6.2. Молчаливое отключение Google-логина** без `ClientSecret` (§2.4) — вместе с
`/admin`-редиректом на Google (§2.6) это = недостижимая админка. Закрывается A2 (warning) и A5.

**6.3. Коллизия `home.html`** (§2.2) — при неаккуратном копировании корень сайта меняется молча.
Закрывается решением §3.2 + приёмкой A1.

**6.4. Первый деплой затирает `wwwroot` App Service.** Публикация — полная замена содержимого;
ничего «нажитого» на сервере в `wwwroot` держать нельзя (снапшоты Discovery уже конфигурируются
отдельным `Discovery:SnapshotDir` — проверить, что он **не** внутри `wwwroot`).

**6.5. Cold start на B1** — при простое первый запрос медленный; кэши (`ICacheService`,
in-memory) после рестарта пустые, а `/api/athletes/career` на холодную по замерам ~5 с
(известный остаток фазы 3.5). Не блокер, но на приёмке не пугаться.

**6.6. Стоимость** ~$30+/мес (принято в роадмапе). B1 без слотов — деплой = кратковременный
рестарт, staging-слота нет.

---

## 7. Отдельной задачей (не сейчас)

- Перенос/наполнение прод-данных: дамп vs импорт с нуля, судьба синтетики (`SYNTH`),
  `Sys_*`-таблицы с личными данными (медиа, favorites, логины) переносить **не** следует.
- Кастомный домен + TLS, CDN для `public/images`.
- Staging-слот и smoke-canary после деплоя.
- Бэкапы/восстановление Flexible Server сверх дефолтных.
