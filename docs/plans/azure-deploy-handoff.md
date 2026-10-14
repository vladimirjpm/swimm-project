# ПЕРЕДАЧА — деплой на Azure (состояние на 14.10.2026, вечер)

**Читать первым в новом чате по деплою.** Единственный источник решений и порядка —
[azure-deploy-plan.md](azure-deploy-plan.md) (ревизия 3 + решения 14.10). Здесь — где мы, что
делать дальше и грабли; детали — ссылками на разделы плана, не копией.

Ветка работы — `entity-hero-roles` (в master влиты PR #101–#104; коды деплоя 14.10 — `a84c2833`, `aaa6d77c`,
`05a757b4`, `b3e9bdd4`, `df1eb881`, `b70be2ad` — **только в ветке, не запушены**). **Прода всё ещё нет**:
в Azure не создано ни одного ресурса swimhub.

---

## 1. Где мы

**Код к деплою готов** (сверка 11.10, план §0-бис): `dotnet publish` проходит все проверки
`azure-deploy.yml`, smoke под `Production` — все страницы 200, 124 миграции идут на чистой БД
(`01-roles.sql` → `--migrate` → `02-grants.sql`), дамп собирается. Новых блокеров нет.

**Сделано 14.10 (вне кода):**
- **Домен `swimhub.co.il`** — куплен в MyNames до 14.10.2031 (владелец — Влад, частное лицо;
  блокировка переноса и автопродление вкл.). План Р1.
- **DNS — свой аккаунт Cloudflare (Free)**, серверы имён `maleah` / `nicolas.ns.cloudflare.com`,
  зона Active. Записей под сайт ещё нет — заводятся после создания App Service.
- **Почта — Cloudflare Email Routing:** `info@` и `privacy@swimhub.co.il` → Gmail Влада
  (только приём; проверено живыми письмами). MX ×3, SPF, DKIM `cf2024-1`.
- **Отправка — Resend** (В12): домен Verified, DKIM/SPF/DMARC `p=none`, ключ на месте, живое
  письмо — все PASS. Подробности — §3.2.
- **Azure вычищен:** удалены 5 старых групп и секрет SWA в GitHub; оставлена группа
  `VS-clt-…` (организация Azure DevOps с привязанной оплатой, $0, удалить можно только из
  DevOps). План §2.1.

**Решения Влада 14.10** (план §10, сводка §0-тер):

| | Решение |
|---|---|
| В10 | Регион **Israel Central** (≈ $35/мес, §9) |
| В9 | PostgreSQL **16**, public + firewall (адреса App Service + домашний IP), бэкапы 14 дней, без гео-копии |
| В12 | SMTP — **Resend**, `smtp.resend.com:587` STARTTLS, `noreply@swimhub.co.il` (переменные — §5) |
| В6 | Ключи Data Protection — **`/home`** (`DataProtection__KeysDir=/home/data/dp-keys`; общий для инстансов) |
| В7 | Настройки админки — **в БД** (код) |
| В8 | Миграции первый раз — **вручную с домашнего IP** |
| В5 | Отдельный **прод-клиент Google OAuth**, отдельный ключ serper.dev |
| В4 | Оператор данных — Влад; **аккаунт с 13 лет по самодекларации**; юрист до A10 |
| В16 | Сайт **открываем для поиска** этапом A10 (Р6 — временное закрытие) |
| **В17** | **Админки на проде НЕТ** — локальная админка пишет прямо в прод-БД (§0-тер-бис) |

Открыто: только мелочи В14 (Postgres в CI, Application Insights). В11 снят решением В17.

---

## 2. Форма прода после В17 — главное, что надо держать в голове

- На проде — только публичный сайт и API. `/Admin`, `/api/admin`, фоновые джобы — выключены
  флагами. loglig на проде не нужен.
- **Два режима у Влада** (два профиля запуска): **разработка** — локальная БД в Docker, как
  сейчас; **`ProdAdmin`** — локальная админка против прод-БД, только для работы с настоящими
  данными (импорт, промежуточные, склейки, модерация, проверка loglig-id).
- **После запуска главная база — прод.** Импорт в локальную БД на прод не попадает.
  Рискованное — сначала на свежей локальной копии, потом в `ProdAdmin`.
- Кнопка «предложить loglig-id» на проде остаётся: предложения копятся в БД, их проверит
  ночной джоб локальной админки.

---

## 3. Что делать дальше — по порядку

### 3.1 Код до первого деплоя `[агент]` — ✅ сделано 14.10 (план §0-тер, пп. 1–5)

Как устроено и где отступили от постановки — **план §0-четыре** (здесь не копирую). Коротко:

1. **Прод без админки** — `Hosting/HostingMode.cs`; флаги выключает закоммиченный
   `appsettings.Production.json`. Админ-пути → 404, джобов нет, кроме пинга БД и моста кэша.
2. **ProdAdmin** — `dotnet run --project server/Swimm.API --launch-profile ProdAdmin`;
   секреты — `appsettings.ProdAdmin.Local.json` (образец `*.example`, там же пароль SMTP Resend).
   Сторож `ProdAdminGuard`: база localhost или расхождение миграций → отказ стартовать.
   Красная полоса — `Pages/Admin/Shared/_ModeBanner.cshtml`.
3. **Мост кэша** — `Services/CacheBridge.cs`: обёртка над всем `ICacheService`, а не только
   перехватчик; блок «Между процессами» на `/Admin/Cache`.
4. **Настройки в БД** — `Sys_AdminSettings`; ⚠ теперь переживают рестарт (и `DebugDetails` тоже).
5. **Синхронизация** — версия на последовательности (без блокировок), роль `swimm_sync`
   (`01-roles.sql -v sync_password=…`), `server/db/sync-from-prod.sh` **сливает**, а не заливает
   заново (каскады снесли бы локальное), скилл `/db-sync-from-prod`.

Не проверено: красная полоса глазами (нужен вход настоящим Google-аккаунтом в ProdAdmin) и
синхронизация против настоящего Azure — только против второй базы в локальном Postgres.

### 3.1-бис В4 и A10 `[агент]` — ✅ код сделан 14.10 (план §0-пять)

- **В4** (`b3e9bdd4`): галочка 13+ в форме регистрации; новый пользователь Google проходит шаг
  `/auth/google/confirm-age`; отметка — `Sys_AppUsers.AgeConfirmedAt`.
- **A10 шаги 1–4** (`b3e9bdd4`, `df1eb881`): `/privacy` (EN/RU/HE, справкой), флаг «скрыть из
  поиска» на `/Admin/Swimmers`, серверные title/description/canonical пловца, клуба, соревнования,
  `/sitemap.xml`, `noindex` снят с 7 страниц (закрыты группы, my-*, h2h, админка).
- **`www` → корень 301** (`b70be2ad`) — redirect URI Google для `www` не нужен.

Тексты `/privacy` и `/terms` (EN/RU/HE) составлены агентом под §11 закона о защите частной жизни
(поправка 13) по просьбе Влада (14.10) и прошли внешнее ревью (ChatGPT, правки — план §0-пять,
«Второй раунд»): оператор назван — Владимир Баренцев; страницы пловцов младше 18 не индексируются;
галочка — «разрешение родителя». Юристом не
проверены — проверка по желанию. Сайт открыт для поиска с первой минуты на проде.

Не проверено: Google-шаг 13+ вживую (нужен настоящий вход).

Тесты — на каждый пункт (правило «предлагать юнит-тесты»); новые пути записи — `*PgTests`.

### 3.2 За Владом `[Влад]`
- ✅ (14.10) Аккаунт **Resend**, домен `swimhub.co.il` Verified, регион eu-west-1, Return-Path
  `send`, трекинг выключен. Записи в Cloudflare внесены автонастройкой Resend (все DNS only):
  CNAME `send` → `send.forge.rmta.net`, CNAME `rsend` → `rsend-euw1.forge.rmta.net`,
  TXT `resend._domainkey` (DKIM); руками — TXT `_dmarc` `v=DMARC1; p=none;`. Корневые
  MX/SPF Cloudflare Email Routing не тронуты. API-ключ `swimhub-prod`
  (Sending access, только этот домен) создан, лежит в менеджере паролей Google; на проде →
  `Email__Smtp__Password` (логин `resend`). Потерялся — удалить в Resend, выпустить новый.
  Живая проверка 14.10: локальный API с `Email__Smtp__*` → письмо регистрации на Gmail пришло
  во «Входящие» за 1 с, SPF/DKIM (`swimhub.co.il`)/DMARC — все PASS. Темы писем — «SwimHub —»
  (`aaa6d77c`). В админке — блок «Почта» (`/Admin/Settings#email` + пункт дашборда, `05a757b4`):
  куда уходят письма процесса и кнопка тестового письма себе; в ProdAdmin должен быть зелёным
  (SMTP — `appsettings.ProdAdmin.json`, ключ — `*.Local.json`). Приёмка почты — план A8.
- **Прод-клиент Google OAuth** (redirect: `https://swimhub.co.il/signin-google`,
  `https://swimhub.azurewebsites.net/signin-google`; для `www` не нужен — редирект в коде).
- Ключ **serper.dev** для прода — в `appsettings.ProdAdmin.Local.json` (на App Service не нужен).
- **Юрист** — по желанию: `/privacy` и `/terms` (`client/src/content/help/{privacy,terms}/`) составлены
  агентом 14.10, оператор назван по имени (§11 закона требует указать владельца базы).
- **Слить ветку в master** до A7: деплой и `--migrate` берут код из master.

### 3.3 Ресурсы Azure (A7) — команды готовы, запускает Влад
**Runbook — [azure-a7-commands.md](azure-a7-commands.md)** (PowerShell 5.1, §0–§9; имена `swimhub`
и `swimhub-pg` на 14.10 свободны; провайдер `Microsoft.DBforPostgreSQL` надо зарегистрировать —
§1). Отличия от плана, внесённые и в план 14.10: `ASPNETCORE_FORWARDEDHEADERS_ENABLED`,
`MigrationConnection` и `CandidateSearch__ApiKey` на App Service **не задаются**; для publish
profile включается basic auth SCM (FTP выключен). Кратко, что в нём:
Группа `swimm-rg` заново в `israelcentral`: App Service Plan B1 Linux + Web App
(`DOTNETCORE|10.0`, Always On, HTTPS Only, 1 инстанс), Flexible Server B1ms PG 16
(админ-логин **`swimm`**, public + firewall, retention 14, 32 ГБ + autogrow), БД `swimm`.
Порядок первого запуска — **строго §6 плана**. Переменные — §5 (не задавать
`Loglig__SeasonId`, `PublicSite__BaseUrl`, `DevAdminBypass*`). Деплой — `azure-deploy.yml`
по кнопке (нужны secret `AZURE_WEBAPP_PUBLISH_PROFILE` и variable `AZURE_WEBAPP_NAME`).
После — DNS в Cloudflare (A `@`, CNAME `www`, TXT `asuid`/`asuid.www`, **DNS only**),
`AllowedHosts`, приёмка A8 (изменена В17: на проде `/Admin` → 404, админ-сценарии — из
`ProdAdmin`).

---

## 4. Как проверять

```bash
# publish-артефакт и его проверки (как в azure-deploy.yml)
dotnet publish server/Swimm.API -c Release -o <scratch>/publish

# домен и почта
nslookup -type=NS swimhub.co.il 1.1.1.1
nslookup -type=MX swimhub.co.il 1.1.1.1
nslookup -type=TXT swimhub.co.il 1.1.1.1

# что есть в Azure (только чтение)
az group list -o table
az resource list -o table
```

Whois реестра `.il` — TCP 43 `whois.isoc.org.il` (в PowerShell через `TcpClient`, `whois`
в системе нет).

---

## 5. Грабли

- **SPF у домена — одна TXT-запись.** Провайдер почты сайта дописывает свой `include:` в неё;
  вторая `v=spf1` ломает SPF, письма уходят в спам. (У Resend SPF на поддомене `send.` —
  CNAME на их `forge.rmta.net`, корневую не трогает; старой схемы «MX + TXT на `send`
  с amazonses» у них больше нет.)
- **Записи Cloudflare под Azure — только DNS only (серое облако).** Прокси ломает продление
  App Service Managed Certificate и подменяет IP клиента в `X-Forwarded-For` → лимиты входа и
  история логинов по узлу Cloudflare. Совет Cloudflare «пускать только IP Cloudflare» — НЕ
  делать.
- **`System.Net.Mail` не умеет неявный TLS (465)** — только 587 STARTTLS.
- **Пояс на Linux — `TZ=Asia/Jerusalem`**, не `WEBSITE_TIME_ZONE` (Windows).
- **HTTPS Only в портале обязателен** (Б15): без него HTTP не уводится на https.
- **`ProdAdmin` — никогда не `Development`** и только из master: иначе персонажи/обход логина
  в прод-БД или код разойдётся со схемой.
- **Перед дампом для первого наполнения** — дропнуть `tmp_*` (§2.3); `HubGroups` и пловцы
  закрытых групп не едут (их вырезает `dump-seed.sh`).
- **Деплой берёт код с GitHub** — что не в master, на прод не попадёт.
- Группу `VS-clt-…` не трогать — удаление падает из-за привязки оплаты DevOps.
- Репо публичный: уязвимости и секреты в доках не расписывать.
