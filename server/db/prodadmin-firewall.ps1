# Файрвол прод-базы под текущий IP (docs/deploy.md, «ProdAdmin или prodpsql не подключаются»).
#
# Домашний/рабочий IP меняется (у Влада на работе — каждый день), а прод-база пускает только адреса
# из правил файрвола Azure. Скрипт узнаёт внешний IP; если ни одно правило его не покрывает —
# заводит или ОБНОВЛЯЕТ одно правило auto-<имя машины>: правила не копятся, у машины всегда одно.
# Вызывается из run-prodadmin.ps1 (кнопка swimm-api-prodadmin) перед запуском; можно и руками:
#   powershell -NoProfile -ExecutionPolicy Bypass -File server/db/prodadmin-firewall.ps1
# Нужен az CLI с входом (az login) в подписку с ресурсной группой swimm-rg.
param(
    [string]$ResourceGroup = 'swimm-rg',
    [string]$Server = 'swimhub-pg'
)

$ErrorActionPreference = 'Stop'

function ConvertTo-IpNumber([string]$ip) {
    $bytes = [System.Net.IPAddress]::Parse($ip).GetAddressBytes()
    [Array]::Reverse($bytes)
    [BitConverter]::ToUInt32($bytes, 0)
}

try {
    $ip = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 15).ToString().Trim()
} catch {
    Write-Host "Файрвол: не узнал внешний IP ($($_.Exception.Message)) — проверь сеть." -ForegroundColor Red
    exit 1
}

$json = az postgres flexible-server firewall-rule list -g $ResourceGroup -s $Server `
    --query "[].{name:name,start:startIpAddress,end:endIpAddress}" -o json 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Файрвол: az не прочитал правила сервера $Server. Если просрочен вход — az login." -ForegroundColor Red
    Write-Host ($json | Out-String)
    exit 1
}
$rules = $json | Out-String | ConvertFrom-Json

$n = ConvertTo-IpNumber $ip
$covering = $rules | Where-Object { (ConvertTo-IpNumber $_.start) -le $n -and $n -le (ConvertTo-IpNumber $_.end) } |
    Select-Object -First 1
if ($covering) {
    Write-Host "Файрвол: IP $ip уже разрешён (правило $($covering.name))."
    exit 0
}

# Имя правила: буквы, цифры, '-' и '_'.
$name = ('auto-' + $env:COMPUTERNAME.ToLowerInvariant()) -replace '[^a-z0-9_-]', '-'
$verb = if ($rules | Where-Object { $_.name -eq $name }) { 'update' } else { 'create' }
Write-Host "Файрвол: IP $ip не разрешён — $verb правила $name (полминуты)..."
$out = az postgres flexible-server firewall-rule $verb -g $ResourceGroup -s $Server -n $name `
    --start-ip-address $ip --end-ip-address $ip -o none 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Файрвол: не удалось записать правило $name." -ForegroundColor Red
    Write-Host ($out | Out-String)
    exit 1
}
Write-Host "Файрвол: правило $name → $ip."
exit 0
