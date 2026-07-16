# Задание Sonnet: B2 — массовое удаление сирот + карточки «Внимание» на дашборде

**Не запускай других агентов — делай всё сам. Не коммить: изменения оставь в рабочем дереве.**

## Контекст

На `/Admin/Swimmers` внизу есть список «сирот» — карточек `Swimmer`, на которые не
ссылается **ничего** (ни Results, ни тренировки, ни группы, ни избранное, ни медиа,
ни аккаунт). Сейчас их 191, все `Origin='isr'` — артефакты импорта протоколов
(участники эстафет/иностранцы с половиной имени). Удалять их можно только руками по
одной — нужна массовая кнопка (B2 из ROADMAP, Влад дал явное «да»).

Плюс на дашборде `/Admin` админ не видит, что есть данные, требующие ручной обработки.
Нужны карточки: «сироты» и «спорные дубли» со ссылками на `/Admin/Swimmers`.

## Решения (зафиксированы, не пересматривать)

1. **Критерий сироты не менять** — ровно тот, что в `SwimmerDedupService.FindCandidatesAsync`
   (блок Orphans, 8 NOT EXISTS + отсев `SYNTH-`). Тренировочные фантомы (`Origin='local'`,
   есть `Sys_TrainingResults`/членство в группе) под критерий НЕ попадают — Влад с ними
   плавает, их не трогать. Это гарантируется самим критерием, менять его нельзя.
2. **Сервер пересчитывает сирот сам в момент удаления.** Endpoint НЕ принимает список
   id от клиента как истину: он заново вычисляет актуальных сирот тем же критерием и
   удаляет только их. (Опциональный параметр `ids` — если передан, удаляется
   пересечение `ids ∩ актуальные сироты`; передали id не-сироты — он молча пропускается
   и попадает в отчёт как skipped.)
3. Новый метод в существующем сервисе: `ISwimmerDedupService.DeleteOrphansAsync(
   IReadOnlyCollection<int>? ids, CancellationToken ct)` → DTO
   `SwimmerOrphanCleanupReport(int Deleted, List<int> DeletedIds, List<int> SkippedIds)`.
   Реализация в `SwimmerDedupService` (переиспользуй существующий orphan-запрос,
   вынеси его в приватный метод, чтобы критерий жил в одном месте).
4. Endpoint: `POST api/admin/swimmers/orphans/delete` в существующем
   `SwimmersAdminController` (`server/Swimm.API/Controllers/SwimmersAdminController.cs`).
   Тело: `{ ids?: number[] }`. После успешного удаления — `_logger.LogWarning` с
   количеством и id (по образцу merge) и `await _cache.InvalidateAllAsync()`
   (сироты в публичных выдачах не фигурируют, но однородность дешевле рассуждений).
5. Лёгкая сводка для дашборда: `GET api/admin/swimmers/attention-summary` →
   `{ orphans: number, sureCandidates: number, unsureCandidates: number }`.
   Реализация: вызвать `FindCandidatesAsync` и посчитать (тысячи пловцов — дёшево,
   отдельный оптимизированный запрос не нужен).
6. UI `/Admin/Swimmers` (`server/Swimm.API/Pages/Admin/Swimmers/Index.cshtml`):
   над списком сирот — красная кнопка «Удалить всех сирот (N)». Клик → нативный
   `confirm()` с текстом «Удалить N карточек без единой связи? Пловцы с результатами,
   тренировками и членствами не могут быть удалены. Действие необратимо.» → POST →
   тост с количеством → перезагрузка данных страницы. Никакого dry-run у сирот не
   нужно (в отличие от merge): операция по определению не трогает ничего связанного.
7. UI `/Admin` Dashboard (`server/Swimm.API/Pages/Admin/Index.cshtml`): после
   существующей сетки `#stats-grid` — блок «Требует внимания» (рендерится ТОЛЬКО если
   сводка ненулевая) с карточками-ссылками на `/Admin/Swimmers`:
   - «Сироты: N» — карточки пловцов без единой связи, можно массово удалить;
   - «Спорные дубли: N» — кандидаты merge с `sure=false`, разбирать глазами
     (плюс отдельной строкой «уверенных: M», если M > 0).
   Стиль карточек — как у существующих stats-карточек (`bg-white/[.07] rounded-[10px]`),
   но с заметным акцентом (например, число оранжевым `#ffa726`), т.к. это call-to-action.
   Загрузка — из `attention-summary` в том же init-скрипте страницы.

## Что уже готово (не переделывать)

- `SwimmerDedupService` (`server/Swimm.Infrastructure/Services/SwimmerDedupService.cs`) —
  orphan-запрос уже написан (строки ~79–93), кандидаты и `Normalize` не трогать.
- `SwimmersAdminController` — паттерн merge-endpoint'а: Authorize(Admin), antiforgery
  авто, BadRequest на ArgumentException, LogWarning + InvalidateAllAsync при apply.
- `/Admin/Swimmers` UI уже грузит `api/admin/swimmers/dedup-candidates` и рендерит
  `#orphans`; там же есть паттерн POST с X-XSRF-TOKEN (посмотри, как страница шлёт merge).
- DTO живут в `server/Swimm.Application/Dtos/SwimmerDedupDtos.cs`.
- Дашборд `/Admin` — `#stats-grid` наполняется в init-скрипте из `api/admin/stats`.

## Шаги

1. DTO `SwimmerOrphanCleanupReport` в `SwimmerDedupDtos.cs`; метод в
   `ISwimmerDedupService` (`server/Swimm.Application/Abstractions/`) + реализация
   в `SwimmerDedupService` (orphan-критерий — общий приватный метод/выражение).
2. `SwimmersAdminController`: `POST orphans/delete` + `GET attention-summary`
   (решения 4–5).
3. UI Swimmers: кнопка + confirm + тост + refresh (решение 6).
4. UI Dashboard: блок «Требует внимания» (решение 7).
5. Тесты в `server/Swimm.Tests` (образец — `SwimmerDedupServiceTests.cs`):
   - чистая сирота удаляется;
   - пловец с Results НЕ удаляется; пловец с Sys_TrainingResults НЕ удаляется;
     пловец с членством в HubGroupMembers НЕ удаляется (это защита тренировочных
     фантомов Влада — тест обязателен);
   - `SwimmerOrgId LIKE 'SYNTH-%'` не удаляется;
   - `ids` с не-сиротой → она в SkippedIds, сирота из списка удалена;
   - `ids = null` → удаляются все актуальные сироты.

## Проверка

```bash
dotnet build server/Swimm.sln --configuration Release
dotnet test server/Swimm.Tests --configuration Release
```
Существующие тесты зелёные (сейчас 286/286). Вживую страницы можно посмотреть на
уже запущенном API :5078 (DevAdminBypass в Development пускает в админку без логина),
но **кнопку «Удалить всех сирот» на живой БД НЕ нажимать** — применяет Влад/ревьюер.
Разметку правишь — Tailwind админки: `cd server/Swimm.API && npm run css:build`
(если добавляешь НОВЫЕ утилити-классы, которых ещё нет в admin.min.css) — и тогда
пересобранный `wwwroot/css/admin.min.css` тоже остаётся в рабочем дереве.

## Footguns

- Build-lock MSB3027 (VS держит Swimm.API.dll) → собирай с `--configuration Release`.
- Тесты не ходят в сеть и не трогают живую БД (InMemory; для транзакций —
  `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`,
  паттерн в `CountryFilterTests.cs` / `SwimmerImportMatchingTests.cs`).
- У `Results`/`Sys_TrainingResults` FK = RESTRICT — даже при ошибке критерия БД не даст
  удалить пловца с данными; но полагаться на это нельзя, критерий обязан фильтровать сам.
- Antiforgery: админские POST требуют X-XSRF-TOKEN — используй существующий хелпер
  страницы, не изобретай.

## Вне скоупа (не делать)

- Не менять критерий сироты, merge-логику, `Normalize`, публичные API.
- Не делать периодический пересчёт/фоновые джобы — сводка считается при открытии дашборда.
- Не удалять сирот на живой БД самому.
- Не трогать клиент (client/) вообще — всё в админке (Razor).
