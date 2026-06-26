---
name: smoke-test-api
description: Build, run, and smoke-test the Swimm.API locally, then stop it so the next build isn't locked. Use when the user wants to verify the API runs, hit an endpoint, or check a change end-to-end against the running server. Handles the build-lock footgun and polls /auth/me before curling.
---

# smoke-test-api — локальный прогон API (Swimm)

Запускает API в фоне, дожидается готовности, дёргает эндпоинты и **обязательно гасит процесс**,
чтобы следующий `dotnet build` не упал на блокировке `Swimm.API.dll`.

## Предусловия
- Postgres поднят: `docker compose -f server/docker-compose.yml up -d`.
- Нет висящего предыдущего `dotnet run` (см. ниже «build-lock»).

## Шаги

### 1. Собрать
```bash
dotnet build server/Swimm.sln
```
Если падает с `MSB3027/MSB3021 … Swimm.API.dll is locked by ".NET Host (<pid>)"` — жив прошлый
запуск. Прибей и пересобери:
```powershell
Get-CimInstance Win32_Process -Filter "Name='Swimm.API.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

### 2. Запустить в фоне (Development, порт 5078)
Запускай как background-команду:
```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/Swimm.API --urls http://localhost:5078
```

### 3. Дождаться готовности (poll, не sleep наугад)
```bash
until curl -fsS -o /dev/null http://localhost:5078/auth/me; do sleep 1; done
```
`/auth/me` отвечает 200 (или 401, тоже значит «поднялся») — сервер готов.

### 4. Дёрнуть нужные эндпоинты
```bash
curl -s http://localhost:5078/<endpoint> | head
```

### 5. ОБЯЗАТЕЛЬНО остановить процесс
```powershell
Get-CimInstance Win32_Process -Filter "Name='Swimm.API.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```
Пропуск этого шага = заблокированная сборка в следующий раз.

## Заметки
- Админ-мутации требуют antiforgery, auth-эндпоинты rate-limited — для них нужен полноценный
  сценарий с куками, простым curl не проверишь.
- dev-`IEmailSender` пишет ссылку в лог вместо отправки письма — ищи её в выводе процесса.
