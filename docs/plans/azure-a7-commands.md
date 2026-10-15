# A7 — команды создания ресурсов Azure (runbook для PowerShell 5.1)

**Запускает Влад, по шагам, в одном окне PowerShell** (переменные живут в сессии). Подготовлено
14.10.2026 по [azure-deploy-plan.md](azure-deploy-plan.md) (§5 переменные, §6 порядок первого
запуска, §10 решения) и [azure-deploy-handoff.md](azure-deploy-handoff.md) §3.3, §5. Решения не
пересматриваются здесь — только исполняются. Порядок §1 → §7 не переставлять.

Проверено 14.10 только чтением (`az … show/list`, `checkNameAvailability`): подписка
`Pay-As-You-Go` активна; в `israelcentral` есть App Service B1 Linux, рантайм `DOTNETCORE|10.0`
(LTS), Flexible Server Burstable `Standard_B1ms`, PostgreSQL 11–18, storage autogrow, гео-копии
бэкапов нет (`geoBackupSupported: Disabled`). Имена **`swimhub`** (Web App) и **`swimhub-pg`**
(Flexible Server) на 14.10 свободны. Провайдер `Microsoft.DBforPostgreSQL` в подписке **не
зарегистрирован** — шаг §1.

Обозначения: ⚠ — параметр **несменяемый** (меняется только пересозданием сервера); «✔» — что
проверить после шага. Секретов в документе нет — только `<плейсхолдеры>`.

---

## 0. Подготовка сессии

### 0.1 Предусловия

- ✅ **Код деплоя в master** (с 14.10): PR #105 (`409e2e25` — В17: прод без админки, мост кэша,
  настройки в БД, синхронизация; почта; В4; A10; `www` → корень) и PR #106 (`16f5276b` — `/privacy`
  и `/terms` после ревью, страницы пловцов младше 18 не индексируются). CI на master зелёный.
  Миграции, которых ещё нет ни в одной базе Azure: `AdminSettingsInDb`, `DataVersionForSync`,
  `AppUserAgeConfirmedAt`, `SwimmerHideFromSearch` — `--migrate` в §4.3 накатит их вместе со всеми.
- **`--migrate` (§4.3), дамп (§4.5) и потом ProdAdmin — из ветки `master`, а не из рабочей**: в master
  лежат merge-коммиты, поэтому SHA рабочей ветки с ним не совпадает, даже когда код тот же; на чужой
  ветке сторож ProdAdmin ещё и предупреждает. Перед §4:
  ```powershell
  git fetch origin
  git switch master
  git pull --ff-only
  git rev-parse HEAD origin/master      # ✔ две одинаковые строки
  git status --short server/            # ✔ пусто (свои неотслеживаемые файлы вне server/ не мешают)
  ```
  Иначе в прод уедет миграция, которой нет в выкаченном коде, и `ProdAdmin` потом откажется
  стартовать (сторож сверки миграций). Вернуться к работе — `git switch entity-hero-roles`.
- Docker Desktop запущен, контейнер `swimm-postgres` работает (через него идут `psql`/`pg_dump`:
  на Windows их нет). API локально **не запущен** (build-lock).
- Готов прод-клиент Google OAuth (ClientId + ClientSecret), ключ Resend `swimhub-prod` — в
  менеджере паролей. Ключ serper.dev для прода — по желанию (см. §5).
- `az login` выполнен, `gh auth status` — залогинен.

### 0.2 Переменные

```powershell
Set-Location C:\_Proj\swimm-project
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8   # иначе кириллица в логах az — «?»
$env:PYTHONIOENCODING = 'utf-8'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

az account set --subscription 'Pay-As-You-Go'

$rg      = 'swimm-rg'
$loc     = 'israelcentral'
$plan    = 'swimhub-plan'
$app     = 'swimhub'                                  # → https://swimhub.azurewebsites.net
$pg      = 'swimhub-pg'
$pgHost  = "$pg.postgres.database.azure.com"
$repo    = 'vladimirjpm/swimm-project'
$gitBash = 'C:\Program Files\Git\bin\bash.exe'       # НЕ просто bash: в PowerShell это WSL
$myIp    = (Invoke-RestMethod https://api.ipify.org).Trim()
$myIp
```

✔ `az account show --query "{name:name, state:state}" -o jsonc` → `Pay-As-You-Go`, `Enabled`;
`$myIp` — IPv4 домашнего роутера (VPN выключен: правило firewall будет на этот адрес).

### 0.3 Пароли и ключи

Пароли генерируются локально, **сразу кладутся в менеджер паролей**, в файлы не пишутся.
Алфавит — только латиница и цифры (без `; ' " @ / \ % & ^ |` и пробела): такие пароли проходят
без экранирования через psql, строки подключения Npgsql, `az.cmd` и PowerShell. Требование
Azure к паролю админа (3 категории из 4) закрывают заглавные + строчные + цифры.

```powershell
function New-StrongPassword {
  param([int]$Length = 32)
  $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
  $limit = 256 - (256 % $alphabet.Length)       # без смещения по модулю
  $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
  $buf = New-Object byte[] 1
  $sb  = New-Object System.Text.StringBuilder
  while ($sb.Length -lt $Length) {
    $rng.GetBytes($buf)
    if ($buf[0] -lt $limit) { [void]$sb.Append($alphabet[$buf[0] % $alphabet.Length]) }
  }
  $rng.Dispose()
  $s = $sb.ToString()
  if ($s -cmatch '[A-Z]' -and $s -cmatch '[a-z]' -and $s -match '[0-9]') { return $s }
  return New-StrongPassword -Length $Length
}

function Read-Secret([string]$Prompt) {          # ввод без эха и без следа в истории
  $s = Read-Host -AsSecureString $Prompt
  [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
}

$pwAdmin = New-StrongPassword    # админ-логин swimm (owner, миграции)
$pwRw    = New-StrongPassword    # swimm_rw  (рантайм-запись)
$pwRo    = New-StrongPassword    # swimm_ro  (публичное чтение)
$pwSync  = New-StrongPassword    # swimm_sync (синхронизация прод → локально)

# В менеджер паролей — по одному, через буфер обмена:
$pwAdmin | Set-Clipboard   # вставить как «swimhub-pg / swimm»
$pwRw    | Set-Clipboard   # «swimhub-pg / swimm_rw»
$pwRo    | Set-Clipboard   # «swimhub-pg / swimm_ro»
$pwSync  | Set-Clipboard   # «swimhub-pg / swimm_sync»
Set-Clipboard -Value ' '

# Внешние секреты — из менеджера паролей:
$googleClientId     = Read-Host 'Google ClientId ПРОД-клиента'
$googleSecret       = Read-Secret 'Google ClientSecret ПРОД-клиента'
$googleDevSecret    = Read-Secret 'Google ClientSecret DEV-клиента (для ProdAdmin)'
$resendKey          = Read-Secret 'Ключ Resend swimhub-prod (re_...)'
```

Окно закрылось — переменные пропали: `$pwAdmin = Read-Secret 'swimm'` и т.д. из менеджера, §0.2
заново.

### 0.4 Грабли PowerShell 5.1 (учтены ниже)

- `az` — это `az.cmd`: символ `|` внутри аргумента ломается даже в кавычках (`cmd` режет строку).
  Поэтому рантайм задаём как `DOTNETCORE:10.0` (двоеточие — штатная форма `az webapp create`).
  Если `|` всё же нужен — `--%` перед аргументами или `'"DOTNETCORE|10.0"'`.
- Двойные кавычки **внутри** аргумента нативной программы PS 5.1 теряет: в SQL для psql пишем
  `\"Results\"` (проверено). В PowerShell 7.3+ — без обратных слешей.
- `curl` — алиас `Invoke-WebRequest`; ниже везде `curl.exe`.
- `;` в значениях (строки подключения, `AllowedHosts`) — только внутри кавычек.

### 0.5 psql до прод-базы — через локальный контейнер

```powershell
# Пароль — через переменную окружения, в командную строку не попадает.
$env:PGPASSWORD = $pwAdmin
function prodpsql {
  docker exec -i -e PGPASSWORD swimm-postgres psql "host=$pgHost port=5432 dbname=swimm user=swimm sslmode=require" -v ON_ERROR_STOP=1 -X @args
}
```

Контейнер ходит в интернет через домашний роутер — правило firewall на `$myIp` его покрывает.

---

## 1. Провайдер и группа ресурсов

```powershell
az provider register --namespace Microsoft.DBforPostgreSQL --wait
az group create --name $rg --location $loc -o table
```

✔ `az provider show -n Microsoft.DBforPostgreSQL --query registrationState -o tsv` → `Registered`;
группа `swimm-rg` в `israelcentral`. (Группу `VS-clt-…` не трогать — handoff §5.)

---

## 2. PostgreSQL Flexible Server и база `swimm`

### 2.1 Сервер (5–10 минут)

```powershell
$pgCreate = @(
  'postgres', 'flexible-server', 'create',
  '--resource-group', $rg, '--name', $pg,
  '--location', $loc,                         # ⚠ регион (В10)
  '--version', '16',                          # мажор обновляется на месте, но не вниз
  '--tier', 'Burstable', '--sku-name', 'Standard_B1ms',
  '--storage-size', '32', '--storage-auto-grow', 'Enabled',
  '--backup-retention', '14',
  '--geo-redundant-backup', 'Disabled',       # ⚠ (в israelcentral и не поддерживается)
  '--zonal-resiliency', 'Disabled',           # HA выкл. (в az < 2.70 флаг назывался --high-availability)
  '--public-access', $myIp,                   # ⚠ режим сети: public + firewall; создаёт правило на домашний IP
  '--admin-user', 'swimm',                    # ⚠ имя админа (owner схемы, см. 01-roles.sql)
  '--admin-password', $pwAdmin,
  '--yes'
)
az @pgCreate
```

Не передавать `--public-access 0.0.0.0`/`All` и не включать в портале «Allow public access from
any Azure service» (В9). Если `--storage-auto-grow` откажет для Burstable — создать без него и
включить позже: `az postgres flexible-server update -g $rg -n $pg --storage-auto-grow Enabled`
(autogrow сменяемый).

✔ Параметры:
```powershell
az postgres flexible-server show -g $rg -n $pg --query "{state:state, ver:version, tier:sku.tier, sku:sku.name, gb:storage.storageSizeGb, autogrow:storage.autoGrow, retention:backup.backupRetentionDays, geo:backup.geoRedundantBackup, net:network.publicNetworkAccess, ha:highAvailability.mode, admin:administratorLogin}" -o jsonc
az postgres flexible-server firewall-rule list -g $rg -s $pg -o table
```
→ `Ready`, `16`, `Burstable`, `Standard_B1ms`, 32, `Enabled`, 14, `Disabled`, `Enabled`,
`Disabled`, `swimm`; в firewall **одно** правило — `$myIp`, правила `0.0.0.0` нет.

### 2.2 База `swimm` с локалью как у локальной

Через `psql` от имени `swimm`, а не `az postgres flexible-server db create`: так владелец базы —
`swimm` (как локально; на этом держатся `REVOKE … ON SCHEMA public` и гранты в `01-roles.sql`),
а локаль задаётся явно из `template0`.

```powershell
docker exec -e PGPASSWORD swimm-postgres psql "host=$pgHost port=5432 dbname=postgres user=swimm sslmode=require" -v ON_ERROR_STOP=1 -c "CREATE DATABASE swimm WITH TEMPLATE template0 ENCODING 'UTF8' LC_COLLATE 'en_US.utf8' LC_CTYPE 'en_US.utf8'"
```

✔ Локаль (⚠ несменяема для базы) и лимиты:
```powershell
prodpsql -At -c "SELECT datname, pg_encoding_to_char(encoding), datcollate, datctype, pg_get_userbyid(datdba) FROM pg_database WHERE datname = current_database()" -c "SHOW server_version" -c "SHOW max_connections"
```
→ `swimm|UTF8|en_US.utf8|en_US.utf8|swimm` — ровно как локальная; `16.x`; `max_connections`
(у B1ms по умолчанию 50). `SHOW lc_collate` на PG 16 не работает — параметр убран из сервера,
локаль смотрится только в `pg_database`.

Серверных параметров менять не нужно: ни миграции, ни скрипты расширений не используют
(`CREATE EXTENSION` в миграциях нет; `fuzzystrmatch` нужен только ручному
`dedup-report.sql`), `azure.extensions` не трогаем.

---

## 3. App Service: план, Web App, настройки, firewall базы

### 3.1 План и приложение

```powershell
az appservice plan create -g $rg -n $plan -l $loc --sku B1 --is-linux --number-of-workers 1 -o table
az webapp create -g $rg -p $plan -n $app --runtime "DOTNETCORE:10.0" --https-only true --basic-auth Disabled -o none
```

✔ `az webapp config show -g $rg -n $app --query linuxFxVersion -o tsv` → `DOTNETCORE|10.0`;
`az webapp show -g $rg -n $app --query "{state:state, httpsOnly:httpsOnly, host:defaultHostName}" -o jsonc`
→ `Running`, `true`, `swimhub.azurewebsites.net`.

### 3.2 Конфигурация сайта

```powershell
az webapp config set -g $rg -n $app --always-on true --ftps-state Disabled --http20-enabled true --min-tls-version 1.2 --number-of-workers 1 --startup-file "dotnet Swimm.API.dll" -o none
az webapp config set -g $rg -n $app --generic-configurations healthCheckPath=/healthz -o none
```

`--startup-file` — явная команда старта (сборка framework-dependent, точка входа
`Swimm.API.dll`); без неё App Service ищет `*.runtimeconfig.json` сам — сейчас он один в корне,
но так надёжнее. `/healthz` выведен из-под https-редиректа и режима обслуживания (план A2).

✔
```powershell
az webapp config show -g $rg -n $app --query "{alwaysOn:alwaysOn, ftps:ftpsState, http2:http20Enabled, tls:minTlsVersion, workers:numberOfWorkers, startup:appCommandLine, health:healthCheckPath}" -o jsonc
```
→ `true`, `Disabled`, `true`, `1.2`, `1`, `dotnet Swimm.API.dll`, `/healthz`.

### 3.3 Исходящие адреса App Service → firewall базы

```powershell
$outIps = (az webapp show -g $rg -n $app --query possibleOutboundIpAddresses -o tsv).Split(',') |
  ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } | Sort-Object -Unique
"Адресов: $($outIps.Count)"
$i = 0
foreach ($ip in $outIps) {
  $i++
  az postgres flexible-server firewall-rule create -g $rg -s $pg -n ('appsvc-{0:D2}' -f $i) --start-ip-address $ip --end-ip-address $ip -o none
}
```

`possibleOutboundIpAddresses` — надмножество `outboundIpAddresses` (все адреса, которыми план
может ходить наружу). Список меняется при смене тарифа плана — тогда цикл повторить.

✔ `az postgres flexible-server firewall-rule list -g $rg -s $pg -o table` — правила `appsvc-NN`
на каждый адрес + домашний; `0.0.0.0` нет.

Сменился домашний IP — новое правило:
`az postgres flexible-server firewall-rule create -g $rg -s $pg -n home-<ггггммдд> --start-ip-address <ip> --end-ip-address <ip>`
(старое удалить: `… firewall-rule delete -g $rg -s $pg -n <имя> --yes`).

---

## 4. База: роли → миграции → гранты → данные (план §6, шаги 2–5)

Порядок жёсткий (CLAUDE.md, «Least-privilege roles»). Скрипты гоняем внутри контейнера
`swimm-postgres`: там есть `psql`, `pg_dump` и `bash`.

### 4.1 Скрипты в контейнер

```powershell
docker exec swimm-postgres mkdir -p /tmp/swimm-db
foreach ($f in '01-roles.sql', '02-grants.sql', 'restore-seed.sh', 'seed-tables.txt', 'fix-sequences.sql') {
  docker cp "server/db/$f" "swimm-postgres:/tmp/swimm-db/$f"
}
```

✔ `docker exec swimm-postgres ls /tmp/swimm-db` — пять файлов.

### 4.2 Роли (`01-roles.sql`)

```powershell
prodpsql -v "rw_password=$pwRw" -v "ro_password=$pwRo" -v "sync_password=$pwSync" -f /tmp/swimm-db/01-roles.sql
```

(`owner` по умолчанию `swimm` — совпадает с админ-логином сервера.)

✔ В конце `OK: роли swimm_rw и swimm_ro готовы.`;
`prodpsql -At -c "SELECT rolname FROM pg_roles WHERE rolname LIKE 'swimm%' ORDER BY 1"` →
`swimm`, `swimm_ro`, `swimm_rw`, `swimm_sync`.

### 4.3 Миграции (`--migrate` из профиля ProdAdmin)

Сначала — `appsettings.ProdAdmin.Local.json` (gitignored, образец `*.example`): строки к прод-базе
понадобятся и здесь, и потом для всей работы с данными (В17). Если файл уже есть — не
перезаписывать, сверить руками.

```powershell
$csBase      = "Host=$pgHost;Port=5432;Database=swimm;SslMode=Require"
$csDefault   = "$csBase;Username=swimm_rw;Password=$pwRw;Maximum Pool Size=5"
$csMigration = "$csBase;Username=swimm;Password=$pwAdmin;Maximum Pool Size=2"
$csAdmin     = "$csBase;Username=swimm_rw;Password=$pwRw;Maximum Pool Size=10"
$csRead      = "$csBase;Username=swimm_ro;Password=$pwRo;Maximum Pool Size=20"
$csReadLocal = "$csBase;Username=swimm_ro;Password=$pwRo;Maximum Pool Size=10"   # для ProdAdmin

$paFile = Join-Path (Get-Location) 'server\Swimm.API\appsettings.ProdAdmin.Local.json'
if (Test-Path $paFile) { "Уже есть: $paFile — сверь строки руками" } else {
  $pa = [ordered]@{
    ConnectionStrings = [ordered]@{
      DefaultConnection   = $csDefault
      MigrationConnection = $csMigration
      AdminConnection     = $csAdmin
      ReadConnection      = $csReadLocal
    }
    Authentication = @{ Google = @{ ClientSecret = $googleDevSecret } }
    Email          = @{ Smtp   = @{ Password     = $resendKey } }
  }
  [IO.File]::WriteAllText($paFile, ($pa | ConvertTo-Json -Depth 5))
  "Записан: $paFile"
}
git check-ignore -q $paFile; if ($LASTEXITCODE -eq 0) { 'gitignored — OK' } else { '!! НЕ в .gitignore — остановись' }

dotnet run --project server/Swimm.API --launch-profile ProdAdmin -- --migrate
```

Сторож `ProdAdmin` (отказ на расхождении миграций) срабатывает позже CLI — `--migrate` он не
блокирует (план §0-четыре п. 2).

✔ Число миграций на проде = числу в коде:
```powershell
prodpsql -At -c 'SELECT count(*), max(\"MigrationId\") FROM \"__EFMigrationsHistory\"'
(Get-ChildItem server\Swimm.Infrastructure\Migrations -Filter '2*.cs' | Where-Object Name -notlike '*.Designer.cs').Count
```

### 4.4 Гранты (`02-grants.sql`)

```powershell
prodpsql -f /tmp/swimm-db/02-grants.sql
```

✔ В конце `ro_tables` = **31**; уведомления «swimm_sync нет» быть не должно (роль заведена в
§4.2).

### 4.5 Данные: дамп локальной → заливка в прод

```powershell
# (а) Локальная база — на той же схеме, что прод (тот же коммит, §0.1):
dotnet run --project server/Swimm.API -- --migrate

# (б) По желанию: дропнуть хвосты ручных операций (в дамп они и так не попадают — include-список):
docker exec swimm-postgres psql -U swimm -d swimm -c "DROP TABLE IF EXISTS tmp_relay_age_backup_20260731, tmp_results_backup_1528_20260823"

# (в) Дамп бизнес-таблиц (seed-tables.txt; пловцы закрытых групп вырезаются, сторож ПДн):
& $gitBash server/db/dump-seed.sh

# (г) В контейнер и заливка в прод одной транзакцией:
docker cp server/db/seed-data.sql swimm-postgres:/tmp/swimm-db/seed-data.sql
$env:PGHOST = $pgHost; $env:PGUSER = 'swimm'; $env:PGDATABASE = 'swimm'; $env:PGSSLMODE = 'require'
docker exec -i -e PGHOST -e PGUSER -e PGDATABASE -e PGSSLMODE -e PGPASSWORD swimm-postgres bash /tmp/swimm-db/restore-seed.sh --direct -f /tmp/swimm-db/seed-data.sql

# (д) Статистика планировщика после массовой заливки:
prodpsql -c "ANALYZE"
```

`--direct` внутри контейнера = «`psql` по PG*-переменным», то есть в прод. Если `PGHOST` не
проброшен, скрипт уйдёт в **локальную** базу и откажется (там уже есть `Results`) — это и есть
защита, `--force` не добавлять.

✔ dump-seed печатает «пловцов групп вырезано (Р71): 18» (или сколько их сейчас);
restore-seed — таблицу счётчиков. Сверка с локальной:
```powershell
$countsSql = 'SELECT (SELECT count(*) FROM \"Results\") AS results, (SELECT count(*) FROM \"Swimmers\" WHERE \"PrivateHubGroupId\" IS NULL) AS swimmers, (SELECT count(*) FROM \"Competitions\") AS comps, (SELECT count(*) FROM \"Clubs\") AS clubs, (SELECT count(*) FROM \"Records\") AS records'
docker exec swimm-postgres psql -U swimm -d swimm -At -c $countsSql   # локальная
prodpsql -At -c $countsSql                                              # прод
```
→ строки совпадают (на 11.10: 97 727 | 5 923 | 203 | 241 | 17 419).

### 4.6 Уборка

```powershell
docker exec swimm-postgres rm -rf /tmp/swimm-db
Remove-Item server\db\seed-data.sql
Remove-Item Env:PGHOST, Env:PGUSER, Env:PGDATABASE, Env:PGSSLMODE
# Env:PGPASSWORD оставить до конца §7 (prodpsql), потом: Remove-Item Env:PGPASSWORD
```

---

## 5. Переменные App Service (план §5)

Задавать **только** перечисленное. **Не задавать:** `Admin__Enabled`, `BackgroundJobs__Enabled`
(приезжают с кодом в `appsettings.Production.json`), `Loglig__SeasonId`, `PublicSite__BaseUrl`,
`DevAdminBypass*`, `CandidateSearch__Provider`, `WEBSITE_TIME_ZONE` (это Windows; на Linux —
`TZ`), **`ASPNETCORE_FORWARDEDHEADERS_ENABLED`** (заголовки прокси уже разбирает
`UseForwardedHeaders()` в коде; переменная включила бы второй такой же проход, и IP посетителя
определялся бы неверно — план §5 поправлен 14.10), **`ConnectionStrings__MigrationConnection`**
(сайт её не использует — миграции идут из ProdAdmin; пароль владельца базы на веб-сервер не
кладём, с `b70be2ad` старт на проде её и не проверяет), `CandidateSearch__ApiKey` (только
админка — ниже), `Seo__BaseUrl` (дефолт в коде — `https://swimhub.co.il`).

### 5.1 Не секреты

```powershell
az webapp config appsettings set -g $rg -n $app -o none --settings `
  ASPNETCORE_ENVIRONMENT=Production `
  ASPNETCORE_HTTPS_PORT=443 `
  WEBSITE_RUN_FROM_PACKAGE=1 `
  TZ=Asia/Jerusalem `
  DataProtection__KeysDir=/home/data/dp-keys `
  "AllowedHosts=$app.azurewebsites.net" `
  "Authentication__Google__ClientId=$googleClientId" `
  Email__Smtp__Host=smtp.resend.com `
  Email__Smtp__Port=587 `
  Email__Smtp__EnableSsl=true `
  Email__Smtp__User=resend `
  Email__Smtp__From=noreply@swimhub.co.il `
  Email__Smtp__FromName=SwimHub
```

(Обратный апостроф — перенос строки; после него не должно быть пробелов.)
`AllowedHosts` после привязки домена расширяется — §8.

### 5.2 Секреты

```powershell
az webapp config appsettings set -g $rg -n $app -o none --settings `
  "ConnectionStrings__DefaultConnection=$csDefault" `
  "ConnectionStrings__AdminConnection=$csAdmin" `
  "ConnectionStrings__ReadConnection=$csRead" `
  "Authentication__Google__ClientSecret=$googleSecret" `
  "Email__Smtp__Password=$resendKey"
```

Пулы — по плану A3: чтение 20, запись 10 (+ `DefaultConnection` 5 — сайт её почти не
использует, она фоллбек). Строки — как app settings `ConnectionStrings__*`, не через
`az webapp config connection-string`: код читает `GetConnectionString`, обе формы ему видны, но
план и стартовая диагностика говорят именно о `ConnectionStrings__X`. `MigrationConnection` здесь
нет сознательно — см. «Не задавать» выше; она живёт только в `appsettings.ProdAdmin.Local.json`.

`CandidateSearch__ApiKey` — по коду читается только админкой и фоновым пакетным забором, а их на
проде нет (В17): на App Service **не задаём**, ключ serper.dev нужен в
`appsettings.ProdAdmin.Local.json` (`"CandidateSearch": { "ApiKey": "…" }`).

✔ Имена без значений:
```powershell
az webapp config appsettings list -g $rg -n $app --query "[].name" -o tsv | Sort-Object
```
→ 18 наших имён (13 из §5.1 + 5 из §5.2; служебные `WEBSITE_*`, если App Service добавил
свои, не трогать). Приложение перезапустится само.

---

## 6. Publish profile для GitHub и первый деплой (план A6)

У новых приложений basic-auth публикации **выключен** (`--basic-auth Disabled` в §3.1), а
publish profile — это и есть basic-auth к SCM. Включаем **только SCM** (FTP остаётся выключен):

```powershell
az resource update -g $rg --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies --parent "sites/$app" -n scm --set properties.allow=true -o none

az webapp deployment list-publishing-profiles -g $rg -n $app --xml | gh secret set AZURE_WEBAPP_PUBLISH_PROFILE -R $repo
gh variable set AZURE_WEBAPP_NAME --body $app -R $repo
```

Профиль идёт в `gh` через stdin (в аргумент нельзя: PS 5.1 портит кавычки внутри XML).

✔ `az resource show -g $rg --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies --parent "sites/$app" -n scm --query properties.allow -o tsv` → `true`
(а с `-n ftp` → `false`); `gh secret list -R $repo` и `gh variable list -R $repo` показывают оба
имени.

**Деплой:** GitHub → Actions → «Деплой на Azure App Service» → Run workflow (ветка `master`). Или:
`gh workflow run azure-deploy.yml -R $repo --ref master`, следить — `gh run watch -R $repo`.

✔ Прогон зелёный: проверка артефакта прошла, `/healthz` 200, в Summary SHA совпадает,
`/readyz` с `"database":"up"`.

Перевыпуск профиля (утёк/сбросили учётку): `az webapp deployment list-publishing-profiles … --xml |
gh secret set …` ещё раз.

---

## 7. Проверки после деплоя (приёмка A8 — частично)

```powershell
az webapp log config -g $rg -n $app --docker-container-logging filesystem -o none
az webapp log tail -g $rg -n $app        # Ctrl+C — выход; заодно сделать рестарт: az webapp restart -g $rg -n $app
```

✔ В логе старта:
- `Окружение Production: админка выкл, фоновые джобы выкл.`
- **нет** Critical `SMTP НЕ НАСТРОЕН`, `Google OAuth ВЫКЛЮЧЕН`, `… указывает на localhost …`;
- нет Warning про `SslMode`, `AllowedHosts = "*"`, `DataProtection:KeysDir`.

```powershell
$base = "https://$app.azurewebsites.net"
curl.exe -sS "$base/healthz"; ''
curl.exe -sS "$base/readyz"; ''
curl.exe -sS "$base/version"; ''
curl.exe -s -o NUL -w "/Admin -> %{http_code}`n" "$base/Admin"
curl.exe -s -o NUL -w "/api/admin/settings -> %{http_code}`n" "$base/api/admin/settings"
curl.exe -s -o NUL -w "/admin-home.html -> %{http_code}`n" "$base/admin-home.html"
curl.exe -sI "http://$app.azurewebsites.net/results" | Select-String -Pattern '^(HTTP|Location)'
curl.exe -s -o NUL -w "/results -> %{http_code}`n" "$base/results"
```

✔ `/healthz` 200; `/readyz` — `"database":"up"`; `/version` — SHA деплоя; три админ-пути →
**404** (В17); `http://` → 301 на `https://` (HTTPS Only, Б15); `/results` → 200.

Открытие для поиска (A10) — голый HTML без скриптов:
```powershell
$sid = prodpsql -At -c 'SELECT \"SwimmerId\" FROM \"Results\" GROUP BY 1 ORDER BY count(*) DESC LIMIT 1'
curl.exe -s "$base/swimmers/$sid" | Select-String -Pattern '<title>|name="description"|rel="canonical"|name="robots"'
curl.exe -s "$base/sitemap.xml" | Select-String -Pattern '<url>' | Measure-Object | Select-Object -ExpandProperty Count
curl.exe -s "$base/robots.txt"
curl.exe -s -o NUL -w "/privacy -> %{http_code}`n" "$base/privacy"
```
✔ у пловца — ивритское имя в `<title>`, описание, canonical на `https://swimhub.co.il/swimmers/…`,
**без** robots; в sitemap — тысячи адресов (на 14.10 локально 6 222); `robots.txt` со строкой
`Sitemap:`; `/privacy` 200. До привязки домена canonical уже смотрит на `swimhub.co.il` — так и
задумано.

```powershell
# Ключи Data Protection легли в /home:
az webapp ssh -g $rg -n $app      # в консоли: ls -la /home/data/dp-keys ; date  (время — израильское, TZ)
# Соединения с базой:
prodpsql -At -c "SELECT usename, application_name, count(*) FROM pg_stat_activity WHERE datname = 'swimm' GROUP BY 1, 2 ORDER BY 1"
```

✔ В `/home/data/dp-keys` — `key-*.xml`; `date` показывает IDT/IST; в `pg_stat_activity` —
`swimm_ro` (в т.ч. `swimm-cache-bridge` — слушатель моста кэша) и, после первых запросов,
`swimm_rw`.

Дальше по плану §6, шаги 8–11:
1. Google Console, прод-клиент → Authorized redirect URIs:
   `https://swimhub.azurewebsites.net/signin-google` (+ `https://swimhub.co.il/signin-google`
   после §8).
2. Войти на сайте через Google → роль админа из ProdAdmin:
   `dotnet run --project server/Swimm.API --launch-profile ProdAdmin -- --grant-admin <email>`.
3. Приёмка A8 целиком (план §4 A8: публичные страницы, register→verify→login, ProdAdmin: красная
   полоса, мост кэша, `/Admin/Health`).
4. Локальная копия — `/db-sync-from-prod` (роль `swimm_sync`, пароль `$pwSync`); для
   напоминания об отставании — `ConnectionStrings:ProdVersionConnection` =
   `Host=<pgHost>;Port=5432;Database=swimm;Username=swimm_sync;Password=<pwSync>;SslMode=Require`
   в `appsettings.Local.json`.

---

## 8. Свой домен `swimhub.co.il` (после приёмки на `*.azurewebsites.net`)

```powershell
$verId = az webapp show -g $rg -n $app --query customDomainVerificationId -o tsv
$appIp = az webapp config hostname get-external-ip -g $rg --webapp-name $app --query ip -o tsv
"asuid: $verId"; "A: $appIp"
```

Записи в Cloudflare — **все DNS only (серое облако)**, handoff §5:

| Тип | Имя | Значение |
|---|---|---|
| A | `@` | `$appIp` |
| CNAME | `www` | `swimhub.azurewebsites.net` |
| TXT | `asuid` | `$verId` |
| TXT | `asuid.www` | `$verId` |

Существующие MX/SPF/DKIM/DMARC и записи Resend не трогать. CAA-записей в зоне нет — так и
оставить (Managed Certificate выпускает DigiCert).

✔ `nslookup -type=TXT asuid.swimhub.co.il 1.1.1.1` и `nslookup swimhub.co.il 1.1.1.1` отдают
новые значения.

```powershell
az webapp config hostname add -g $rg --webapp-name $app --hostname swimhub.co.il -o none
az webapp config hostname add -g $rg --webapp-name $app --hostname www.swimhub.co.il -o none

$thApex = az webapp config ssl create -g $rg -n $app --hostname swimhub.co.il --query thumbprint -o tsv
az webapp config ssl bind -g $rg -n $app --certificate-thumbprint $thApex --ssl-type SNI --hostname swimhub.co.il -o none
$thWww = az webapp config ssl create -g $rg -n $app --hostname www.swimhub.co.il --query thumbprint -o tsv
az webapp config ssl bind -g $rg -n $app --certificate-thumbprint $thWww --ssl-type SNI --hostname www.swimhub.co.il -o none

az webapp config appsettings set -g $rg -n $app -o none --settings "AllowedHosts=swimhub.co.il;www.swimhub.co.il;$app.azurewebsites.net"
```

`*.azurewebsites.net` в `AllowedHosts` оставить: на него ходят health check, Always On и дымовая
проверка деплоя.

✔ `az webapp config hostname list -g $rg --webapp-name $app -o table` — оба хоста с
`SslState: SniEnabled`; `curl.exe -sI https://swimhub.co.il/` → 200;
`curl.exe -sI https://www.swimhub.co.il/clubs/1` → **301** на `https://swimhub.co.il/clubs/1`
(редирект `www` в коде, `b70be2ad` — поэтому сертификат на `www` нужен, а redirect URI Google
для `www` — нет); письмо register→verify ведёт на `https://swimhub.co.il/…` (план A8, «Почта»).

После домена — Google Search Console (A10 шаг 5, Влад): подтвердить `swimhub.co.il` TXT-записью
в Cloudflare, отправить `https://swimhub.co.il/sitemap.xml`.

---

## 9. Откат, удаление, стоимость

- **Откат кода** — перезапуск workflow деплоя на предыдущем коммите/теге
  (`WEBSITE_RUN_FROM_PACKAGE=1` — подмена пакета целиком). Миграции так не откатываются: две
  необратимы, откат схемы — восстановлением базы на момент времени (PITR в новый сервер,
  бэкапы 14 дней) и переключением `ConnectionStrings__*` (план Б13).
- **Снести всё**, что создано по этому документу:
  ```powershell
  az group delete --name swimm-rg     # спросит подтверждение
  ```
  ⚠ Уходят сервер базы вместе с бэкапами, сайт, сертификаты — считать необратимым. Группу
  `VS-clt-…` не трогать. Секрет и переменную в GitHub удалить отдельно:
  `gh secret delete AZURE_WEBAPP_PUBLISH_PROFILE -R $repo`, `gh variable delete AZURE_WEBAPP_NAME -R $repo`.
- **Стоимость** — план §9: ≈ $35/мес (B1 ≈ 14.5 + B1ms ≈ 16.1 + диск 32 ГБ ≈ 4–5). Бюджетный
  алерт (бесплатно) — портал → Cost Management → Budgets, например $50/мес с письмом на 80 %.
- **Соединения:** у B1ms `max_connections` по умолчанию 50, часть служебная. Пулы сайта
  (20 + 10 + 5) и ProdAdmin (10 + 10) в сумме близки к лимиту, но пулы ленивые — реально занято
  намного меньше. Смотреть `pg_stat_activity` (§7); упрёмся — ужать пулы, а не поднимать тариф.
