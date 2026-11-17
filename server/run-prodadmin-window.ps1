# ProdAdmin в отдельном окне Windows, которое не зависит от приложения Claude (кнопка
# swimm-api-prodadmin-window в .claude/launch.json). Нужен для длинных работ: прогон рекордов по
# странам (~1,5 ч), пакетный импорт.
#
# Почему не обычная кнопка swimm-api-prodadmin. Её ProdAdmin — дочерний процесс приложения, и
# приложение гасит его вместе с остальными preview-серверами сессии. 17.11.2026 так оборвался прогон
# по странам на 201 из 213; в логе ошибки нет, только «was stopped by the app» в preview_list.
#
# Как устроено. Окно создаётся через WMI (Win32_Process.Create): тогда его родитель — служба WMI, а
# не приложение, и погасить его вместе с сессией приложению нечем. Сам этот скрипт остаётся
# «сторожем»: ждёт, пока окно закроется, чтобы в приложении кнопка показывала, работает ли ProdAdmin.
# Остановят сторожа — окно ProdAdmin живёт дальше. Закрыть ProdAdmin — закрыть его окно.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$port = 5078

$busy = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
if ($busy) {
    Write-Host ("СТОП: порт $port уже занят (процесс {0}). Уже работает ProdAdmin или локальный API — " -f
        ($busy[0].OwningProcess)) -ForegroundColor Red
    Write-Host "закрой его окно (или останови кнопку) и запусти снова."
    exit 1
}

$script = Join-Path $PSScriptRoot 'run-prodadmin.ps1'
$command = "powershell.exe -NoExit -NoProfile -ExecutionPolicy Bypass -File `"$script`""
$created = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine      = $command
    CurrentDirectory = $repo
}
if ($created.ReturnValue -ne 0) {
    Write-Host "СТОП: окно ProdAdmin не открылось (Win32_Process.Create вернул $($created.ReturnValue))." -ForegroundColor Red
    exit 1
}

$windowPid = $created.ProcessId
Write-Host "ProdAdmin открыт в отдельном окне (PID $windowPid). Это окно не закрывай до конца работы."
Write-Host "Остановка этой кнопки ProdAdmin НЕ гасит — закрыть его можно только окном."

# Сторож: живём, пока живо окно.
while (Get-Process -Id $windowPid -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 5 }
Write-Host "Окно ProdAdmin закрыто."
