# ПЕРЕДАЧА — деплой на Azure (состояние на 14.10.2026)

**Читать первым в новом чате по деплою.** Единственный источник решений и порядка —
[azure-deploy-plan.md](azure-deploy-plan.md) (ревизия 3 + решения 14.10). Здесь — где мы, что
делать дальше и грабли; детали — ссылками на разделы плана, не копией.

Ветка работы — `entity-hero-roles` (всё влито в master: PR #101–#104). **Прода всё ещё нет**:
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
  (только приём; проверено живыми письмами). MX ×3, SPF, DKIM `cf2024-1`. DMARC — нет.
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

### 3.1 Код до первого деплоя `[агент]` (план §0-тер, пп. 1–5)

1. **Флаги `Admin:Enabled` и `BackgroundJobs:Enabled`** (на проде `false`).
   - Admin: `/Admin/*`, `/api/admin/*`, `admin-home.html` → 404. Точки: middleware в
     [Program.cs](../../server/Swimm.API/Program.cs) (блок «Блокируем /admin/», ~стр. 325),
     `MapRazorPages` (~429), `DefaultFileNames` (admin-home в списке — §3 Б3 плана).
   - Jobs: `AddHostedService` (~стр. 100, 103, 244–247) — оставить только
     `DbPingBackgroundService`.
   - Стартовая диагностика (~стр. 543–577): проверку Google оставить (вход нужен
     пользователям), но текст «/admin недостижима» при `Admin:Enabled=false` поправить.
2. **Профиль `ProdAdmin`**: окружение `ProdAdmin` (НЕ `Development` — без `DevAdminBypass`,
   персонажей, `--seed-personas`); строки подключения к Azure — gitignored-файл;
   `launchSettings.json` + конфиг в `.claude/launch.json`. Вход — настоящим аккаунтом Влада
   (ему `--grant-admin`). **Красная полоса «PRODUCTION DATABASE»** на всех страницах админки.
3. **Мост сброса кэша через `pg_notify`**: [CacheInvalidationInterceptor](../../server/Swimm.Infrastructure/Data/CacheInvalidationInterceptor.cs)
   после сохранения шлёт метки в канал; фоновый `LISTEN`-сервис в каждом процессе сайта
   сбрасывает их у себя (`ICacheService.InvalidateTagsAsync`). ⚠ Этот сервис должен работать
   и при `BackgroundJobs:Enabled=false`. Пейлоад `NOTIFY` ≤ 8000 байт — длинный список меток
   резать или слать «сбросить всё». Правило 1 `docs/pre-push-rules.md` (кэш → `/Admin/Cache`).
4. **В7 — настройки в БД**: [AdminSettingsService](../../server/Swimm.Infrastructure/Services/AdminSettingsService.cs)
   сейчас в памяти; образец таблицы — `DebugOption` / миграция `20260822141149_DebugOptions`.
   Плюс `LogligVerifyEnabled` в реестр настроек (§8.2). Новая таблица `Sys_*` →
   `seed-tables.txt` (исключить), `02-grants.sql` не трогать.
5. **Синхронизация «прод → локально»** (§0-тер-бис): `Sys_DataVersion` + statement-level
   триггер на таблицах из `seed-tables.txt`; `Sys_SyncState` локально; скрипт
   `server/db/sync-from-prod.sh` (⚠ `restore-seed.sh` рассчитан на пустую БД — сначала
   вычистить локальные `Sys_*`-ссылки на исчезнувшее, потом заливка, потом
   `--seed-personas --reset`); предупреждение при старте локального API + жёлтая полоса;
   скилл `/db-sync-from-prod` + строка в CLAUDE.md.

После — **В4** (галочка 13+ при регистрации и первом входе через Google) и **A10** (открытие
для поиска: `/privacy` → серверные title/description → `sitemap.xml` → снять `noindex`).

Тесты — на каждый пункт (правило «предлагать юнит-тесты»); новые пути записи — `*PgTests`.

### 3.2 За Владом `[Влад]`
- Аккаунт **Resend**, домен `swimhub.co.il` в нём, его DNS-записи — в Cloudflare
  (DKIM `resend._domainkey`, SPF/MX на поддомене `send.`), плюс `_dmarc` `p=none`.
- **Прод-клиент Google OAuth** (redirect: `https://swimhub.co.il/signin-google`,
  `https://<app>.azurewebsites.net/signin-google`).
- Ключ **serper.dev** для прода.
- **Юрист** — `/privacy` и условия до A10.

### 3.3 Ресурсы Azure (A7) — агент готовит команды, запускает Влад
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
  корневую не трогает.)
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
