# Задание Sonnet — Фаза 3.5: нагрузочный k6-смоук paged API

## 1. Контекст

Фаза 3 «Масштаб и кэш» (`docs/ROADMAP.md`). Пункты 3.1–3.4 закрыты: публичные GET-эндпоинты
за кэшем (CachedJson + ETag), результаты соревнований отдаются постранично через paged API,
агрегаты (карьера, сводка по клубам) считаются на сервере. Остался **3.5** — зафиксировать
нагрузочный бюджет, чтобы регрессии производительности ловились до прода.

Задача: написать **k6-скрипт** (нагрузочный смоук) по профилю «список результатов + 3 фильтра
+ карточка спортсмена», прогнать его против локального API с синтетикой 3 млн строк и
зафиксировать бюджет **p95 < 300 мс на кэш-промахе**.

## 2. Решения (зафиксированы, не пересматривать)

- **Инструмент — k6** (не bombardier). Скрипт на JS, кладём в `server/loadtest/`.
- **Профиль-сценарий** (один k6 `default` с несколькими шагами на итерацию VU, каждый шаг —
  отдельная именованная группа + свой Trend-метрик):
  1. **list** — `GET /api/results?competitionId=last&page=1&pageSize=100` (голый список источника).
  2. **filtered** — тот же список **с 3 фильтрами**: `styleName`, `distance`, `gender`
     (значения брать из filter-hints, см. ниже) — `GET /api/results?competitionId=last&styleName=freestyle&distance=100&gender=male&page=1&pageSize=100`.
  3. **athlete** — карточка спортсмена: `GET /api/athletes/career?name=<имя>` (имя взять из
     ответа шага 1 — поле `data[].first_name`+`data[].last_name`, или захардкодить одно реальное
     имя, полученное заранее curl'ом; см. Footguns про имена в синтетике).
- **Кэш-промах — обязательное условие бюджета.** Кэш TTL у результатов 2 мин, у payload-обёртки
  5 мин. Чтобы мерить **промах**, а не hit, добавляй в каждый URL **cache-buster**-параметр,
  который меняет кэш-ключ, но не результат: у paged API кэш-ключ строится из фильтра, поэтому
  просто менять page/pageSize нельзя (меняет данные). Вместо этого — **прогрев отдельным
  прогоном не делаем**, а меряем «холодный» бюджет так: перед основным прогоном один раз
  дёрнуть `POST /api/admin/cache/invalidate` НЕЛЬЗЯ (админ, antiforgery). Поэтому берём
  **консервативный подход**: p95 меряем на первом обращении каждого VU к уникальному фильтру —
  варьируй `styleName`/`distance`/`gender` по набору из filter-hints так, чтобы комбинации по VU
  не повторялись в пределах прогона (каждая комбинация = свежий кэш-ключ = промах). Набор
  значений возьми из `GET /api/results/filter-hints?field=style` и `?field=distance` на этапе
  `setup()`.
- **Пороги (k6 `thresholds`)** — прогон падает (exit≠0), если нарушены:
  - `http_req_failed: rate<0.01` (почти нет ошибок);
  - на каждую группу свой Trend: `p(95)<300` мс для list/filtered/athlete.
- **Нагрузка** — умеренный смоук, не стресс: `stages` до ~20 VU, суммарно ~1–2 мин
  (напр. ramp 30s→10 VU, 30s→20 VU, hold 30s, down 15s). Цель — зафиксировать бюджет,
  не убить машину.
- **Куда писать бюджет** — в `docs/ROADMAP.md` пункт 3.5 отметить `✅` с кратким итогом
  (фактические p95 по группам, дата) и в `server/loadtest/README.md` — как запускать + последние
  зафиксированные числа.

## 3. Что уже готово (не переделывать)

- **Эндпоинты** (все за кэшем, camelCase/snake_case по существующим DTO):
  - `GET /api/results?competitionId=last|<id>|&eventId=&styleName=&distance=&gender=&poolType=&country=&name=&club=&birthYearFrom=&birthYearTo=&ageGroup=&position=all|top|podium&eventDate=&page=&pageSize=`
    → `{ page, pageSize, hasMore, total, data: [...] }`. `data[]` содержит `first_name`,
    `last_name` (snake_case). Глубина ограничена: `page*pageSize ≤ 10000` иначе 400.
  - `GET /api/results/filter-hints?field=style|distance|club|competition|name&q=&limit=` → `string[]`.
  - `GET /api/athletes/career?name=<полное имя>` → всегда 200 (нулевой DTO если не найден).
  - `GET /api/competitions` → список источников; `competitionId=last` в /api/results сам находит
    последнее соревнование, отдельный вызов не нужен.
- **Синтетика 3 млн строк уже в локальной БД** (маркировка `SYNTH`). Скрипты:
  `server/db/synthetic-results.sql` (сид), `server/db/synthetic-results-cleanup.sql` (откат).
  НЕ пересоздавать, НЕ чистить — она нужна для прогона.
- Профиль медленных запросов (эталон, чего касались индексами) — `server/db/explain-top-queries.sql`.

## 4. Шаги

1. Создай папку `server/loadtest/`.
2. `server/loadtest/paged-smoke.js` — k6-скрипт по разделу 2:
   - `export const options` с `stages` и `thresholds` (см. решения);
   - `export function setup()` — тянет наборы значений из filter-hints (style, distance),
     фиксированный `gender` набор `['male','female']`, и достаёт одно-два реальных имени из
     первого `GET /api/results?competitionId=last&pageSize=5` (`data[].first_name+' '+last_name`);
     возвращает их в `default`;
   - `export default function(data)` — на каждой итерации: группа `list`, группа `filtered`
     (комбинация фильтров, ротируемая по `__VU`/`__ITER`, чтобы бить свежие кэш-ключи), группа
     `athlete`; каждый ответ — `check` на статус 200 и непустое тело; фиксировать длительность
     в отдельные `Trend` по группам (или использовать встроенную `http_req_duration{group:...}`
     через `thresholds` с тегами групп).
   - База URL — из env: `const BASE = __ENV.BASE_URL || 'http://localhost:5079';`.
3. `server/loadtest/README.md` — как поставить k6, как запустить, что означают пороги, и
   таблица «последний зафиксированный бюджет» (заполнишь после прогона).
4. Прогон:
   - k6 нет на PATH — поставь: `winget install k6 --accept-source-agreements --accept-package-agreements`
     (или `choco install k6`); если менеджеры недоступны — скачай портативный бинарь k6 для
     Windows в `server/loadtest/.bin/` и вызывай оттуда. Бинарь в git НЕ коммить (добавь
     `.bin/` в `server/loadtest/.gitignore`).
   - Подними API из **Release** (см. Footguns) на :5079, дождись готовности (`/auth/me` → 200).
   - `k6 run server/loadtest/paged-smoke.js` (при :5079 по умолчанию; иначе `BASE_URL=... k6 run ...`).
   - Занеси реальные p95 по группам в README и в ROADMAP 3.5.
   - Останови API (чтобы не лочить сборку).

## 5. Тесты

Юнит-тестов на k6-скрипт не пишем (это внешний инструмент). Вместо этого:
- k6-скрипт сам себе тест: `thresholds` дают ненулевой exit-код при нарушении бюджета —
  это и есть критерий приёмки. Убедись, что при заведомо жёстком пороге (напр. временно
  `p(95)<1`) прогон падает — так проверишь, что пороги реально применяются (в отчёте укажи,
  что проверил механику порогов, потом верни 300).
- Существующие C#-тесты (`dotnet test server/Swimm.Tests`, сейчас 181) должны остаться
  зелёными — ты серверный код не трогаешь, но прогони для страховки.

## 6. Проверка (команды)

```bash
# API из Release на :5079 (см. Footguns)
dotnet build server/Swimm.API/Swimm.API.csproj -c Release
# запуск API — см. Footguns; дождись /auth/me → 200
k6 run server/loadtest/paged-smoke.js
dotnet test server/Swimm.Tests/Swimm.Tests.csproj -c Release --nologo   # 181 зелёных
```

Ожидаемо: прогон завершается `✓` по всем thresholds, p95 каждой группы < 300 мс.
Если какая-то группа не проходит — НЕ подгоняй порог под факт: зафиксируй фактические числа,
опиши в отчёте, какой запрос узкое место (сверься с `explain-top-queries.sql`), и оставь порог
300 (пусть прогон падает — это честный сигнал, разберёмся на приёмке).

## 7. Footguns

- **Build-lock от Visual Studio:** VS часто держит Debug-bin на :5078 (kill → access denied).
  Поэтому: сборка `--configuration Release`, API запускать из
  `server/Swimm.API/bin/Release/net10.0/Swimm.API.exe --urls http://localhost:5079`
  (env `ASPNETCORE_ENVIRONMENT=Development`), в конце гасить:
  `Get-CimInstance Win32_Process -Filter "Name='Swimm.API.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`.
- **Два DbContext** — тебя не касается (только читаешь через HTTP), но не трогай миграции.
- **Кэш**: не полагайся на первый прогон как «холодный» глобально — грей свежие кэш-ключи
  вариацией фильтров (см. решения). Повторные одинаковые URL внутри прогона будут hit'ами и
  занизят p95 — это ок для list/athlete, но для честного «промаха» именно filtered-группа
  должна бить уникальные комбинации.
- **Имена в синтетике**: у SYNTH-строк имена могут быть шаблонными — бери реальное имя из
  фактического ответа /api/results, не выдумывай. `/api/athletes/career` на несуществующем
  имени вернёт 200 с нулями (не ошибка), так что `check` на статус мало — проверяй, что
  для «настоящего» имени `races > 0` хотя бы в одном обращении (иначе мимо данных).

## 8. Вне скоупа (не делать)

- НЕ менять серверный код, эндпоинты, индексы, миграции. Только `server/loadtest/*` + отметки
  в `docs/ROADMAP.md`.
- НЕ трогать синтетику (не пересоздавать, не чистить).
- НЕ добавлять CI-интеграцию прогона (отдельная задача, если понадобится).
- НЕ коммитить бинарь k6 и результаты прогона (json summary) в git.
