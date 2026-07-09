# Задание (Sonnet 5): фаза 2, этапы 2.4–2.5 — клиент на /api/records + CRUD в админке

Контекст: фаза 2 роадмапа ([docs/ROADMAP.md](../ROADMAP.md)) — «Рекорды и нормативы в БД».
Этапы 2.1–2.3 **уже сделаны** (Fable, 2026-07-09): таблицы, сидер, публичный API. Твоя
работа — клиентский хелпер с fallback и админ-CRUD. Схему таблиц и API **не менять**.

## Что уже готово (не переделывать)

- Таблицы `Records` (1719 строк) и `NormativeStandards` (6510) — засеяны из легаси JS,
  счётчики сверены. Сущности: `Swimm.Domain/Entities/Record.cs`, `NormativeStandard.cs` —
  там же наборы допустимых значений (`Record.RegionTypes/Categories`, `NormativeStandard.Kinds`).
- Публичный API (RO-путь, кэш 24ч по регионам, `RecordsController`):
  - `GET /api/records?region=<world|EU|ISR|…>&category=<open|age|junior|masters>` — region
    обязателен; category опциональна. Плоский массив snake_case DTO (`RecordDto`).
  - `GET /api/normative-standards?kind=<regular|masters>` — kind опционален
    (`NormativeStandardDto`).
- Сидер `--seed-records` (не трогать), гранты swimm_ro/rw применены.

## Модель (для маппинга на легаси-структуры)

Три оси: территория (`region_type`+`region_code`) → категория (`category`+`age_key`) →
дисциплина (`gender`+`pool_type`+`style`+`distance`). Соответствие легаси-файлам:

| Легаси window.* | Запрос к API | Фильтр ответа |
|---|---|---|
| `normative_record` (WR-ветка) | `region=world` | category=open |
| `normative_record` (ISR-ветка) | `region=ISR&category=open` | |
| `normative_age_record` | `region=ISR&category=age` | age_key = возраст "10"…"18" |
| `normative_masters_record` | `region=ISR&category=masters` | age_key = "25-29"… |
| `normative` (уровни) | `/api/normative-standards?kind=regular` | |
| `normative_masters` | `/api/normative-standards?kind=masters` | |

`pool_type` в API — `25m`/`50m`; легаси-ключи в window-структурах — `25m_pool`/`50m_pool`
(при пересборке структуры добавляй `_pool` обратно).

## Этап 2.4 — клиентский RecordsHelper (мягкий переход!)

1. Новый `client/src/utils/helpers/records-helper.ts` по образцу `CategoryHelper` /
   `ResultsLoadModeHelper` (кэш промиса + fallback). Методы, возвращающие структуры
   **в точности легаси-формы** (см. таблицу выше), чтобы потребители менялись минимально:
   - `getOpenRecords()` → форма `window.normative_record.normatives` (ISR+WR в одном дереве:
     region=world + region=ISR&category=open, листья `{ISR:{...}, WR:{...}}` c полями
     time/name/country(=holder_country)/record_date);
   - `getAgeRecords()` → форма `window.normative_age_record.normatives` (+club);
   - `getMastersRecords()` → форма `window.normative_masters_record.normatives`;
   - `getStandards()` / `getMastersStandards()` → формы `window.normative` /
     `window.normative_masters`.
2. **Fallback обязателен (мягкий переход):** при ошибке API каждый метод возвращает
   соответствующий `window.normative_*` (script-теги в `results_main.html` ПОКА ОСТАЮТСЯ,
   файлы normative*.js НЕ удалять — снос отдельным коммитом после проверки вживую).
3. Перевести потребителей на хелпер (сейчас читают `window.*` напрямую):
   - `client/src/utils/helpers/helper-normative.ts` — главный (см. строки с
     `(window as any).normative*`);
   - `client/src/projects/components/popup/popup-content-normative.tsx`;
   - остальное найдёшь: `grep -rn "window.*normative\|(window as any).normative" client/src`.
   Учти: потребители синхронные, а API — async. Стратегия: прогреть хелпер при старте
   приложения (fire-and-forget в index.tsx или в компоненте результатов), синхронные
   геттеры отдают загруженный кэш, а до загрузки — window-fallback. Так поведение
   не ломается ни в какой момент.
4. Проверка: попап нормативов (обычный и masters), карточки age/masters-рекордов, level
   gauge в строках результатов — работают с поднятым API и (fallback-сценарий) без него.

## Этап 2.5 — CRUD в админке

По образцу Categories (полный стек — интерфейс, репозиторий, endpoints, Razor-страница):

1. `IRecordAdminRepository` в Application (+ реализация в Infrastructure, через
   `SwimmDbContext`): CRUD для Records и NormativeStandards, листинг с фильтрами
   (region, category / kind, gender, pool, style) + пагинация (данных ~8k строк).
2. Валидация значений осей: `Record.RegionTypes`, `Record.Categories`,
   `NormativeStandard.Kinds`; gender male|female; pool_type 25m|50m; time непустой.
   Уникальность позиций гарантируют unique-индексы — конфликт превращать в понятную
   ошибку UI («такой рекорд уже есть»), не в 500.
3. Endpoints в `AdminController` (или отдельный admin-контроллер, как удобнее по размеру)
   — antiforgery уже на контроллере. **Каждая мутация → `ICacheService.InvalidateAllAsync()`**
   (иначе публичный кэш 24ч отдаёт старое).
4. Страница `Pages/Admin/Records.cshtml`: две вкладки (Records / Standards), фильтры,
   inline-редактирование времени/держателя/даты (по образцу существующих админ-таблиц),
   добавление/удаление. Ссылка в меню `_Layout.cshtml`.
5. Tailwind: `cd server/Swimm.API && npm run css:build` после правок классов, коммитить
   `admin.min.css` вместе с изменениями.

## Приёмка

- `dotnet build` + `dotnet test` зелёные; `npx tsc --noEmit` в client чистый.
- Смоук: поправить время рекорда в админке → увидеть новое значение в
  `GET /api/records?region=ISR&category=age` (кэш сброшен) и в попапе на клиенте.
- Fallback: остановить API → клиент показывает рекорды из window.* без ошибок в консоли.

## Правила репо

- RU-комментарии/EN-идентификаторы; не коммитить без просьбы; контроллеры — только
  интерфейсы Application; схему таблиц/API/сидер не менять — чего-то не хватает → спроси.
- normative*.js и script-теги НЕ удалять — это отдельный шаг после проверки вживую.
