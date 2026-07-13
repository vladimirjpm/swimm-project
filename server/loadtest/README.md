# Нагрузочный k6-смоук paged API (Фаза 3.5)

Профиль «список результатов + 3 фильтра + карточка спортсмена» против `/api/results`,
`/api/results/filter-hints`, `/api/athletes/career`. Бюджет: **p95 < 300 мс на кэш-промахе**
для каждой из трёх групп (`list`, `filtered`, `athlete`).

## Установка k6

```powershell
winget install k6 --accept-source-agreements --accept-package-agreements
# ставится в C:\Program Files\k6\k6.exe, добавь в PATH сессии при необходимости:
$env:PATH += ";C:\Program Files\k6"
```

Альтернатива (без winget/choco) — скачать портативный бинарь Windows-релиза k6 в
`server/loadtest/.bin/` (папка в `.gitignore`, бинарь в git не коммитить).

## Запуск

1. Собери и подними API в Release на :5079 (Debug-bin на :5078 часто занят Visual Studio):

   ```bash
   dotnet build server/Swimm.API/Swimm.API.csproj -c Release
   ASPNETCORE_ENVIRONMENT=Development server/Swimm.API/bin/Release/net10.0/Swimm.API.exe --urls http://localhost:5079
   ```

   Дождись `Now listening on: http://localhost:5079` и проверь `curl http://localhost:5079/auth/me` → 200.

2. Прогони скрипт (BASE_URL по умолчанию `http://localhost:5079`):

   ```bash
   k6 run server/loadtest/paged-smoke.js
   # или против другого хоста:
   BASE_URL=http://localhost:5080 k6 run server/loadtest/paged-smoke.js
   ```

3. Останови API, чтобы не лочить следующую сборку:

   ```powershell
   Get-CimInstance Win32_Process -Filter "Name='Swimm.API.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
   ```

## Что меряет скрипт

Каждая VU-итерация выполняет три именованные группы (`group()` в k6), каждая со своим
`Trend`-метриком, чтобы пороги применялись независимо:

- **list** — `GET /api/results?competitionId=last&page=1&pageSize=100` (голый список).
- **filtered** — тот же список с 3 фильтрами (`styleName`, `distance`, `gender`); комбинация
  фильтров ротируется по `__VU`/`__ITER`, так что почти каждая итерация бьёт по свежему
  кэш-ключу (промах, не hit). Наборы значений `styleName`/`distance` тянутся в `setup()` из
  `GET /api/results/filter-hints?field=style|distance`, `gender` — фиксированный `['male','female']`.
- **athlete** — `GET /api/athletes/career?name=<имя>`; имя берётся в `setup()` из первой
  реальной строки `GET /api/results?competitionId=last&page=1&pageSize=5` (не выдумывается).

Нагрузка — умеренный смоук: `stages` 30s→10 VU, 30s→20 VU, hold 30s@20, down 15s (~1m45s
суммарно), не стресс-тест.

## Пороги (thresholds)

- `http_req_failed: rate<0.01` — почти нет ошибок.
- `list_duration`, `filtered_duration`, `athlete_duration`: `p(95)<300` мс каждая.

При нарушении любого порога k6 завершается ненулевым кодом — это и есть критерий приёмки.
Механика порогов проверена вручную: временная замена `list_duration` на `p(95)<1` даёт
`level=error msg="thresholds on metrics 'list_duration' have been crossed"` и код выхода `99`;
после проверки порог возвращён к `300`.

## Последний зафиксированный бюджет

Прогон: 2026-07-13, API из Release (`-c Release`) на :5079, локальная БД с синтетикой
~3.01 млн строк в `Results` (маркировка `SYNTH`). `competitionId=last` резолвится в
Maccabiah 2026 (915 результатов) — как и предписано решениями задания; запрос всё равно
идёт по индексу на полной таблице ~3 млн строк с фильтром `CompetitionId`.

| Группа   | p(95)  | Порог | Статус |
|----------|--------|-------|--------|
| list     | 7.65 ms | <300 ms | ✓ |
| filtered | 6.6 ms  | <300 ms | ✓ |
| athlete  | 6.41 ms | <300 ms | ✓ |

`http_req_failed`: 0.00% (0 из 469 455 запросов). Итог: 156 484 итерации, ~1m45s, до 20 VU.
Все пороги прошли с большим запасом — узких мест на этом профиле сейчас нет (кэш+индексы
из 3.1–3.4 отрабатывают штатно даже на «холодных» комбинациях фильтров).
