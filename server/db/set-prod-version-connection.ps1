# Напоминание «Локальная база отстаёт от прода» (решение В17, docs/plans/azure-deploy-plan.md
# §0-тер-бис): записывает ConnectionStrings:ProdVersionConnection (роль swimm_sync — только чтение)
# в server/Swimm.API/appsettings.Local.json. Файл в .gitignore и читается только в Development.
# Дальше локальный API сам сверяет версию раз в 15 минут → Warning в лог и жёлтая полоса в админке.
#
#   powershell -ExecutionPolicy Bypass -File server/db/set-prod-version-connection.ps1
#
# Адрес сервера берётся из appsettings.ProdAdmin.Local.json (только Host, пароли оттуда не читаются)
# или из -PgHost. Пароль swimm_sync спрашивается скрыто (вставка — ПРАВОЙ кнопкой мыши) и на экран
# не выводится. Повторный запуск перезаписывает только эту строку — остальное в Local.json цело.
param(
    [string]$PgHost,
    [SecureString]$Password,
    [string]$ApiDir = (Join-Path $PSScriptRoot '..\Swimm.API'),
    [switch]$SkipCheck
)
$ErrorActionPreference = 'Stop'

$localPath = Join-Path $ApiDir 'appsettings.Local.json'

if (-not $PgHost) {
    $prodAdmin = Join-Path $ApiDir 'appsettings.ProdAdmin.Local.json'
    if (Test-Path $prodAdmin) {
        $cs = (Get-Content $prodAdmin -Raw | ConvertFrom-Json).ConnectionStrings.DefaultConnection
        if ($cs -match '(?i)(?:^|;)\s*(?:Host|Server)\s*=\s*([^;]+)') { $PgHost = $Matches[1].Trim() }
    }
}
if (-not $PgHost) { $PgHost = Read-Host 'Прод-сервер (<имя>.postgres.database.azure.com)' }
Write-Host "Прод-сервер: $PgHost"

if (-not $Password) { $Password = Read-Host -AsSecureString 'Пароль роли swimm_sync' }
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
try { $pw = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
if (-not $pw) { throw 'Пустой пароль — ничего не записано.' }

# Строка через DbConnectionStringBuilder: он сам заключит в кавычки пароль с ; или кавычками.
$b = New-Object System.Data.Common.DbConnectionStringBuilder
$b['Host'] = $PgHost; $b['Port'] = '5432'; $b['Database'] = 'swimm'
$b['Username'] = 'swimm_sync'; $b['Password'] = $pw; $b['SSL Mode'] = 'Require'; $b['Timeout'] = '10'

# Проверка до записи: пускает ли прод (файрвол Flexible Server, пароль). psql — из контейнера
# swimm-postgres, как у sync-from-prod.sh.
if (-not $SkipCheck) {
    $env:PGPASSWORD = $pw; $env:PGSSLMODE = 'require'
    try {
        $ver = docker exec -e PGPASSWORD -e PGSSLMODE swimm-postgres psql -h $PgHost -U swimm_sync -d swimm -tA `
            -c 'SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM "Sys_DataVersionSeq"'
        $ok = $LASTEXITCODE -eq 0
    }
    finally { Remove-Item Env:PGPASSWORD, Env:PGSSLMODE -ErrorAction SilentlyContinue }
    if (-not $ok) {
        throw 'Прод не пустил (неверный пароль, IP не в файрволе Flexible Server или Docker выключен) — ничего не записано. Без проверки: -SkipCheck.'
    }
    Write-Host "Прод отвечает: версия данных v$("$ver".Trim())"
}

$json = if (Test-Path $localPath) { Get-Content $localPath -Raw | ConvertFrom-Json } else { New-Object PSObject }
if (-not $json.PSObject.Properties['ConnectionStrings']) {
    $json | Add-Member -NotePropertyName ConnectionStrings -NotePropertyValue (New-Object PSObject)
}
$json.ConnectionStrings | Add-Member -NotePropertyName ProdVersionConnection -NotePropertyValue $b.ConnectionString -Force
[IO.File]::WriteAllText($localPath, ($json | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))

Write-Host "Записано: ConnectionStrings:ProdVersionConnection → $localPath"
Write-Host 'Перезапусти локальный API: через ~5 с после старта сверка, при отставании — Warning и жёлтая полоса в /Admin.'
