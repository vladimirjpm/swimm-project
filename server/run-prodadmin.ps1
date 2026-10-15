# Запуск ProdAdmin — локальная админка против ПРОД-базы (решение В17, docs/plans/azure-deploy-plan.md §0-тер-бис).
#
# Только из свежего master: иначе код может разойтись со схемой прода (сторож ProdAdminGuard откажет
# по миграциям или предупредит про ветку). Скрипт сам переключает ветку и подтягивает master, чтобы
# это не надо было помнить. Запускается кнопкой swimm-api-prodadmin (.claude/launch.json) или руками:
#   powershell -NoProfile -ExecutionPolicy Bypass -File server/run-prodadmin.ps1
# -DryRun — только подготовить ветку, ProdAdmin не запускать.
param([switch]$DryRun)

$ErrorActionPreference = 'Continue'
Set-Location (Split-Path $PSScriptRoot -Parent)

function Stop-WithMessage([string]$text) {
    Write-Host ""
    Write-Host "СТОП: $text" -ForegroundColor Red
    exit 1
}

git fetch --quiet origin
if ($LASTEXITCODE -ne 0) { Stop-WithMessage "git fetch не прошёл — нет сети или доступа к GitHub." }

$branch = (git rev-parse --abbrev-ref HEAD).Trim()
# Только отслеживаемые файлы: свои неотслеживаемые (docx и т.п.) переключению не мешают.
$dirty = git status --porcelain --untracked-files=no

if ($branch -ne 'master') {
    if ($dirty) {
        Stop-WithMessage ("ветка '$branch', и в ней есть незакоммиченные изменения. " +
            "Закоммить или отложи их (git stash), потом запусти снова — ProdAdmin работает только из master.")
    }
    Write-Host "Ветка '$branch' → переключаю на master"
    git switch --quiet master
    if ($LASTEXITCODE -ne 0) { Stop-WithMessage "не удалось переключиться на master." }
}

git pull --quiet --ff-only origin master
if ($LASTEXITCODE -ne 0) {
    Stop-WithMessage "master не перематывается на origin/master (есть свои коммиты в master?). Разберись руками."
}

Write-Host ("ProdAdmin из master {0} — красная полоса PRODUCTION DATABASE, все правки идут на сайт." -f (git rev-parse --short HEAD))
if ($DryRun) { Write-Host "DryRun: ProdAdmin не запускаю."; exit 0 }

dotnet run --project server/Swimm.API --launch-profile ProdAdmin
exit $LASTEXITCODE
