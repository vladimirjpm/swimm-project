# Задание Sonnet — K1: маршрут `/clubs/{id}` (страница клуба, идентичность)

## 1. Контекст

Делаем публичную **страницу клуба** (Фаза 10 роадмапа). Модель и план:
- [docs/plans/club-page-model.md](../plans/club-page-model.md) — модель данных;
- [docs/plans/club-page-plan.md](../plans/club-page-plan.md) — план, этап **K1** (это задание).

Сегодня клуб в проекте — **не ресурс**: `club` живёт в query как фильтр, отдельной страницы нет.
Этап K1 заводит **идентичность**: чистый URL `/clubs/{id}`, точку входа и страницу-заглушку.
Данные, карточки и API — **следующие этапы, не этот**.

Ровно такая же работа уже проделана для страницы пловца (`/swimmers/{id}`) — **копируем её
паттерн один в один**.

## 2. Решения (зафиксированы, не пересматривать)

1. Путь ресурса — **`/clubs/{id}`**, id числовой. Никаких `/clubs/{slug}`.
2. В путь идёт **только идентичность**. Сезон, группа, табы — остаются в query
   (правило записано в шапке `client/src/utils/routes.ts`, не нарушать).
3. Страница на этом этапе — **заглушка**: показывает `clubId` и заголовок-плейсхолдер.
   Никаких запросов к API, никаких карточек, никакого дизайна из хендоффа.
4. Имя html-файла — **`club.html`** (по образцу `swimmer.html`), точка входа —
   `client/src/pages/club-page.tsx`, проект — `client/src/projects/club-project/club-project.tsx`.
5. `/clubs` без id (одиночный сегмент) — ведём на `/competitions.html`, **как сделано для
   `swimmers`** (там `"swimmers" => "/results_main.html"` с комментарием «пусть падает штатно»).
   Для `clubs` по той же логике: `"clubs" => "/results_main.html"`.

## 3. Что уже готово (не переделывать)

**Контракт чистых URL живёт в ТРЁХ зеркалах — правятся вместе, иначе dev и прод разъезжаются:**

| Зеркало | Файл | Что там |
|---|---|---|
| Клиент (разбор + генерация) | `client/src/utils/routes.ts` | `routes.*` и `parseRoute()` |
| Dev (Vite) | `client/vite.config.js` | плагин `cleanUrlRewrite`, функция `resolve` |
| Прод (ASP.NET) | `server/Swimm.API/Program.cs` | middleware + локальная `ResolveCleanUrl` (~строки 406–462) |

Плюс **четвёртое** место, про которое легко забыть: `rollupOptions.input` в
`client/vite.config.js` — без новой точки входа прод-сборка не соберёт `club.html`.

Образцы для копирования (читай их перед работой):
- `client/swimmer.html` — html-обёртка;
- `client/src/pages/swimmer-page.tsx` — точка входа (Provider/store/провайдеры);
- `client/src/projects/swimmer-project/swimmer-project.tsx` — как проект читает
  идентичность через `parseRoute()`.

## 4. Шаги

### 4.1 `client/src/utils/routes.ts`
- В шапочный комментарий-контракт добавить строку `/clubs/{id}  → club.html`.
- В `routes` добавить `club: (id: string | number) => '/clubs/' + enc(String(id))`.
- В `RouteIdentity` добавить `clubId: number | null` (с doc-комментарием в стиле соседей).
- В `parseRoute()` — ветку `seg[0] === 'clubs'`, разбор как у `swimmers`
  (число > 0, иначе `null`); поле инициализировать `null` в объекте `id`.

### 4.2 `client/vite.config.js`
- В `cleanUrlRewrite` → `resolve`: в ветку `seg.length === 1` добавить `clubs: '/results_main.html'`;
  в ветку `seg.length >= 2` добавить `if (seg[0] === 'clubs') return '/club.html';`.
- В `build.rollupOptions.input` добавить `club: resolve(__dirname, 'club.html')`.

### 4.3 `server/Swimm.API/Program.cs`
В локальной функции `ResolveCleanUrl`:
- в `switch` для `seg.Length == 1` добавить `"clubs" => "/results_main.html",` с комментарием
  в стиле соседней строки про `swimmers`;
- в `switch` для `>= 2` добавить `"clubs" => "/club.html",  // /clubs/{id}`.

### 4.4 `client/club.html`
Копия `client/swimmer.html`: `<title>Club</title>`, контейнер `<div id="club-page"></div>`,
скрипт `/src/pages/club-page.tsx`.

### 4.5 `client/src/pages/club-page.tsx`
Копия `swimmer-page.tsx` с заменой контейнера на `club-page` и проекта на `ClubProject`.
`RecordsHelper.warmUp()` **не нужен** — заглушка ничего не считает; не копируй его.

### 4.6 `client/src/projects/club-project/club-project.tsx`
Заглушка: через `parseRoute()` берёт `clubId` и рендерит
- если `clubId` есть — заголовок `Club #{id}` и строку `Club page — coming soon`;
- если нет — `Club not found`.

UI **строго на английском** (правило проекта). Стилизация — минимальная, Tailwind-классами
в духе соседних проектов; дизайн будет позже.

## 5. Тесты (обязательно)

В клиенте юнит-тестов нет и раннер не настроен — **тестовый фреймворк не заводи**.
Вместо этого проверка ручная (§6) + строгий `tsc`.

Серверную часть покрой юнит-тестом: в `server/Swimm.Tests/` есть образец
`CompetitionAdminRepositoryTests.cs` (стиль `[Theory]/[InlineData]`, xUnit).
Если `ResolveCleanUrl` недоступна из тестов (она локальная функция внутри `Program.cs`) —
**не рефактори `Program.cs` ради тестируемости**, просто пропусти серверный тест и укажи
это в отчёте. Не выноси middleware в отдельный класс — это отдельное решение, не твоё.

## 6. Проверка

```bash
cd client && npx tsc --noEmit
cd client && npm run build
```
Сборка должна выдать в `dist/` файл `club.html` (проверь `ls client/dist/club.html`).

```bash
dotnet build server/Swimm.sln --configuration Release
dotnet test server/Swimm.Tests/Swimm.Tests.csproj --configuration Release
```
Сюита сейчас **828/828 зелёная** — должна остаться такой.

**Живая проверка (обязательна):**
1. Подними dev-клиент и открой `/clubs/42` — должна отрисоваться заглушка с «Club #42».
2. Открой `/swimmers/1` — убедись, что страница пловца **не сломалась** (общий rewrite).
3. Открой `/results` и `/competitions/last` — тоже должны работать.

## 7. Footguns

- **Build-lock:** если `dotnet build` падает с `MSB3027/MSB3021 … Swimm.API.dll is locked` —
  запущен другой инстанс (Visual Studio или параллельная работа). Собирай
  `--configuration Release`, API поднимай на :5079 (`swimm-api-5079` в `.claude/launch.json`),
  клиент — конфигурация `client-5079`. **Не убивай чужие процессы** — в этой сессии
  параллельно идёт другая работа по серверу.
- **Три зеркала** — правка одного без остальных даёт «в dev работает, в проде 404» (или наоборот).
  Проверь, что все три + `rollupOptions.input` изменены.
- ~~**`base: './'` в прод-сборке** — не трогай~~ — **ОТМЕНЕНО 2026-08-27.** Прод-base теперь `'/'`:
  относительный ломал все двухсегментные чистые URL (белый экран на `/swimmers/{id}`), а
  обоснование «работает и на GH Pages» протухло вместе с удалением того деплоя в `d3b25ab`.
  Причина — `docs/plans/azure-deploy-plan.md` Б2.
- **Кириллица/иврит в UI недопустимы** — интерфейс сайта только английский.
- Не трогай `client/src/utils/routes.ts` **сверх** добавления клуба: правило «в путь только
  идентичность» и legacy-терпимость к `.html` должны остаться как есть.

## 8. Вне скоупа (НЕ делать)

- ❌ Любые обращения к API, `/api/clubs/*`, хуки, загрузка данных.
- ❌ Карточки страницы клуба, тема из хендоффа, `theme.css`.
- ❌ `club_id` в `ResultDto`, merge клубов, `Club.MergedIntoId` — это этап K2, его делает Fable.
- ❌ Любые изменения в расчёте очков, медалей, рангов.
- ❌ Ссылки на страницу клуба из существующих мест (standings, фильтры) — отдельный этап.
- ❌ Рефакторинг `Program.cs` (вынос middleware в класс).
- ❌ Миграции БД, изменения сущностей.

## 9. Отчёт

В конце: что сделано, какие файлы изменены, результат `tsc`/`build`/`test`, что проверено
вживую, и **раздел «Отклонения»** — всё, где пришлось отступить от задания, с причиной.
