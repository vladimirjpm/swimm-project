# Задание Sonnet: раскатка AppTopbar по страницам (шаг 2 из 2)

Шаг 1 (компонент `AppTopbar` + токены тем) уже сделан и принят Владом —
см. `docs/tasks/topbar-design-fable.md` и готовый код в
`client/src/projects/components/app-topbar/`. Дизайн-хендофф-первоисточник:
`!design_handoff/design_handoff_topbar/README.md`.

Твоя часть — **механическая раскатка** готового компонента по трём страницам
и починка z-index под новую sticky-полосу. Вид компонента менять не надо: он принят.

**Не запускай других агентов — делай всё сам.**
**Начинай только если `client/src/projects/components/app-topbar/` существует.**
Если папки нет — остановись и скажи об этом, шаг 1 ещё не готов.

## Решения (зафиксированы, не пересматривать)

- **Топбар ставится на: `competitions`, `groups`, `results_main`.**
  На `about` его уже поставил шаг 1 — не трогай.
  **`home.html` НЕ трогаем вообще** — главная остаётся лендингом со своим `HomeHeader`.
  Хендофф говорит «на главной заменяет навигацию» — это **отменено** решением Влада.
- На `competitions` и `groups` топбар **ЗАМЕНЯЕТ** `HomeHeader` (не добавляется над ним —
  иначе будут две одинаковые навигации подряд).
- На `results_main` топбар **ДОБАВЛЯЕТСЯ** сверху; существующие `GroupHeader` /
  `DataSourceDDL` остаются на месте — они про другое (шапка группы/фильтр, не навигация).
- **`HomeHeader` НЕ удалять и не менять** — он остаётся жить на главной.
- **`active` на results_main не передаём** (ни один пункт nav не подсвечен). Причина:
  у страницы результатов нет своего пункта в навигации (Home / Competitions / Groups /
  Normatives / Records / About). Если проп `active` в компоненте обязательный — сделай
  его опциональным (`active?:`), это единственная правка компонента, которая тебе разрешена.
- Пункты Normatives / Records остаются некликабельными — страниц под них нет. Не создавать.

## Что уже готово (не переделывать)

- `client/src/projects/components/app-topbar/` — компонент, sticky, `z-50`, свой
  `useAuth()` + `LoginModal` внутри (провайдер `LoginModalProvider` для него НЕ нужен).
- Токены `--theme-topbar-*` в `client/src/index.css` — приняты на 7 темах × light/dark.
- `about.tsx` — топбар уже стоит, там же `<UI_ThemeDevTool />`. Образец подключения — оттуда.

Где сейчас стоит `HomeHeader` (заменить на первых двух, третью не трогать):

| Страница | Файл | Строка | Что сделать |
|---|---|---|---|
| competitions | `client/src/projects/competitions-project/competitions.tsx` | ~160 | `<HomeHeader active="competitions"/>` → `<AppTopbar active="competitions"/>` |
| groups | `client/src/projects/hub-groups-project/groups.tsx` | ~742 | `<HomeHeader active="groups"/>` → `<AppTopbar active="groups"/>` |
| home | `client/src/projects/home-project/home.tsx` | ~13 | **НЕ ТРОГАТЬ** |

`results_main`: корневой компонент `client/src/projects/results-main-project/results-main-project.tsx`.
Сверху там `<GroupHeader>` (если `?group=`) либо `<DataSourceDDL>` — обёрнуты в
`<div className="w-full z-40 max-lg:px-2 max-lg:pt-2">` (стр. ~247 и ~259).
`<UI_ThemeDevTool />` там уже подключён (стр. ~241).

## Шаги

1. `competitions.tsx` и `groups.tsx`: замени `HomeHeader` на `AppTopbar`, поправь импорты.
   Проверь, что после замены не остался неиспользуемый импорт `HomeHeader` (tsc это поймает).
2. `results-main-project.tsx`: добавь `<AppTopbar />` первым элементом (без `active`).
3. **Почини z-index под sticky-полосу** — это главное в задаче:
   - `client/src/projects/results-table/results-table.tsx:316` — шапка таблицы сейчас
     `sticky top-0 z-10`. Под полосой высотой ~46px она прилипнет ПОД неё и будет
     перекрыта. Смести: `top-[46px]` (точную высоту возьми из `app-topbar.tsx` — если
     там другая, используй её и напиши в отчёте). `z-10` не трогай — полоса `z-50` выше.
   - `results-main-project.tsx:~247/~259` — на wrapper'ах стоит `z-40` **без `position`**,
     то есть z-index не действует вообще (элемент static). Это существующий баг. Добавь
     `relative`, чтобы `z-40` заработал — он должен быть ниже полосы (`z-50`) и выше контента.
   - Проверь, что оверлеи results_main (фильтры `z-[100]`, оверлей `z-[90]`, попапы
     `z-[130]`, модал логина `z-[100]`, mode-toggle `z-[120]`) по-прежнему перекрывают
     полосу — она `z-50`, конфликта быть не должно, но **убедись глазами**.
4. Прокликай на каждой странице: активный пункт подсвечен верно (competitions → Competitions,
   groups → Groups, results_main → ничего), ссылки ведут куда надо, бургер на узкой ширине
   работает, гость видит Login.

## Проверка

```bash
cd client
npx tsc --noEmit          # обязательно, 0 ошибок
npm run dev               # :5173
```
API: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/Swimm.API --urls http://localhost:5078`.
Если :5078 занят Visual Studio — API на :5079 + клиент с `SWIMM_API_TARGET=http://localhost:5079`
(конфиг `client-5079` в `.claude/launch.json`).

Обойди вживую и приложи скриншоты:
- `http://localhost:5173/competitions.html` — топбар вместо старой шапки
- `http://localhost:5173/groups.html`
- `http://localhost:5173/results_main.html?category=all` — **прокрути таблицу вниз**:
  шапка таблицы должна прилипать ПОД полосой, не заезжая на неё и не прячась за ней
- `http://localhost:5173/results_main.html?group=<любой>` — топбар + GroupHeader под ним
- `http://localhost:5173/home.html` — **не изменилась** (старый HomeHeader на месте)
- Узкая ширина (~375px) на каждой — бургер

## Footguns

- Build-lock :5078 (осиротевший `dotnet run`):
  `Get-CimInstance Win32_Process -Filter "Name='Swimm.API.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`.
  Останови всё, что поднял, после проверки.
- `results_main` — multi-page Vite: страницы монтируются из `client/src/pages/*.tsx`,
  у каждой свой mount-div. Entry-points перечислены в `client/vite.config.js`.
  Новых страниц не добавляй — их и не нужно.
- `LoginModalProvider` есть только на `results-main-page.tsx` — топбару он не нужен
  (у него свой стейт модала), но и не мешает. Не убирай его: им пользуются
  гостевые сердечки в таблице.
- Клиентский Tailwind v4 идёт через Vite — `npm run css:build` (это про админку) НЕ запускать.
- Комментарии русские, идентификаторы английские.

## Вне скоупа (не делать)

- Не менять вид/вёрстку/токены `AppTopbar` — он принят. Единственное исключение:
  сделать `active` опциональным, если он обязательный.
- Не трогать `home.tsx`, `home-header.tsx`, `home.html`.
- Не удалять `HomeHeader` — он остаётся на главной.
- Не рефакторить `GroupHeader` / `DataSourceDDL` / таблицу результатов — только `top-[46px]`
  и `relative` из шага 3.
- Не создавать страницы normatives/records, не делать их пункты ссылками.
- Не коммитить — оставь в рабочем дереве, приёмка и коммит за мной.
