# Задание Fable: AppTopbar — компонент + токены тем (шаг 1 из 2)

Дизайн-хендофф: `!design_handoff/design_handoff_topbar/README.md` (прочитай его первым —
он источник требований: компоненты, пропсы, токены, правила).

Твоя часть — **вид и токены**: собрать компонент и довести его до приёмки Владом на всех
7 темах × light/dark. Раскатку по страницам делает потом Sonnet отдельным таском —
**ты её не делаешь** (кроме about, см. ниже).

**Не запускай других агентов — делай всё сам.**

## Решения (зафиксированы Владом 2026-07-16, не пересматривать)

- **Топбар ставится везде, КРОМЕ главной.** `home.html` остаётся лендингом со своим
  `HomeHeader` — его не трогаем вообще. Хендофф говорит «на главной заменяет навигацию» —
  **это отменено**, Влад решил иначе.
- Пункты **Normatives и Records — некликабельные** (страниц под них не существует).
  Рендерить как сейчас в `HomeHeader`: `<span>` без href, приглушённым цветом.
  Бейджи «● LIVE» / «★ 3 NEW» из мобильного меню HomeHeader в топбар не тащим.
- Nav-пункты топбара: Home / Competitions / Groups / Normatives(span) / Records(span) / About.
- Проп `active` по хендоффу: `'home' | 'competitions' | 'groups' | 'normatives' | 'records' | 'about'`.
- Приёмка вида — на **about.html** (сейчас это заглушка, риск нулевой): ты ставишь топбар
  туда и подключаешь `UI_ThemeDevTool`. Влад открывает `about.html?themes` и щёлкает темы.
- `home-header.tsx` — **эталон поведения auth**, списывай с него, но не меняй его.

## Что уже готово (не переделывать, переиспользовать)

### Темы — `client/src/index.css`
Две независимые оси, обе на `<html>`: `data-mode` (light/dark) и `data-theme`.
Tailwind-вариант: `@custom-variant dark (&:where([data-mode="dark"], [data-mode="dark"] *));` (стр. 7).
`:root` = light (стр. 60), `:root[data-mode="dark"]` (стр. 93), блоки тем — со стр. 138.

7 тем (значения, которые пойдут в `color-mix`):

| `data-theme` | `--theme-primary` | `--theme-accent` |
|---|---|---|
| `training` / `training-dashboard` (= `:root`, дефолт) | `#5b5fc7` | `#5b5fc7` |
| `training-nexaverse` | `#1a5f7a` | `#2dd4bf` |
| `training-ocean` | `#0077b6` | `#00b4d8` |
| `competition` / `competition-emerald` | `#10b981` | `#10b981` |
| `competition-blue` | `#0466c8` | `#0466c8` |
| `competition-warm` | `#7fb685` | `#ef6f6c` |
| `competition-dark` | `#34d399` | `#34d399` |

`--theme-text-active` = `#ffffff` во всех темах. Контракт (комментарий на стр. 319):
`--theme-primary`, `--theme-bg-active`, `--theme-accent`, `--theme-text-active`
осью режима НЕ переопределяются.

**Внимание на контраст** — хендофф предупреждает про «спорный результат формулы»,
вот кандидаты: accent светлый у `training-nexaverse` (#2dd4bf), `training-ocean` (#00b4d8),
`competition-dark` (#34d399) — белый текст на них может не читаться. Хендофф уже даёт
точечный override для nexaverse; посмотри глазами, нужны ли ещё. Это ровно та часть,
ради которой задача идёт тебе, а не Сонету.

### Переключение тем
- `client/src/hooks/useMode.ts` — `data-mode` на `<html>`, persist в `localStorage['swimm-mode']`,
  экспорт `{ mode, toggleMode, setMode }`.
- `client/src/hooks/useTheme.ts` — `data-theme` на `<html>`, база = Redux
  `state.filterSelected.activity_type`, кастомизация через `data-training-theme` /
  `data-competition-theme` **на `<body>`**.
- `client/src/projects/components/mix/theme-dev-tool/theme-dev-tool.tsx` — `UI_ThemeDevTool`,
  рендерится только при `?themes` в URL, переключает mode и все 7 тем. Сейчас подключён
  только в `results-main-project.tsx:241`.

### Auth — `client/src/hooks/useAuth.ts`
Возвращает `{ ...AuthUser, loading, refresh }`, где `AuthUser` =
`{ isAuthenticated, userId, email, displayName, avatarUrl, roles, isAdmin, swimmerId }`.
Один `GET /auth/me` на страницу (модульный кэш + `useSyncExternalStore`), `refresh()`
перечитывает и оповещает всех. Любая ошибка → гость, не throw.

**Ключевой паттерн (соблюсти!):** пока `loading === true` — auth-блок не рендерит НИЧЕГО,
чтобы не мигало «Sign in» → аватар. См. `home-header.tsx:145-150`.

### Модал логина
`client/src/projects/components/login-modal/login-modal.tsx` — пропсы
`{ open, onClose, onLoggedIn }`, портал, `fixed inset-0 z-[100]`.
`login-modal-context.tsx` — `LoginModalProvider` + `useLoginModal(): { openLoginModal() }`;
провайдер подключён только на `results-main-page.tsx:24`.
`HomeHeader` использует **не контекст**, а свой локальный `useState(loginOpen)`.
Для топбара делай так же, как HomeHeader (свой стейт + `<LoginModal>` в хвосте) —
это работает на любой странице независимо от наличия провайдера.

### Эталон: `client/src/projects/home-project/components/home-header.tsx` (274 стр.)
Оттуда переиспользуй логику (копируй в топбар, HomeHeader не меняй):
- `SwimHubLogo()` (стр. 7–18) — **лежит внутри этого файла**, отдельного модуля нет.
  Для `TopbarLogo` сделай свой (по хендоффу: иконка S + «SWIMHUB», цвета из токенов полосы).
- `UserAvatar({name, avatarUrl, size})` (59–79) — `<img referrerPolicy="no-referrer">`
  либо градиентный кружок с инициалом. Хендофф просит 30px.
- `logoutHref` (101): `/auth/logout?returnUrl=${encodeURIComponent(window.location.href)}`.
- `signOutEverywhere` (103–111): `POST /auth/logout-all` c `credentials: 'include'`,
  в `finally` — `auth.refresh()`.
- Дропдаун: закрытие по клику вне (`useEffect` 92–99, `mousedown` на `document`).
- `userName = auth.displayName || auth.email || 'User'` (89).

## Шаги

1. **Токены** в `client/src/index.css`: добавь блок `--theme-topbar-*` по формуле из
   хендоффа (в `:root` и `:root[data-mode="dark"]`), плюс точечные override там, где
   формула даёт нечитаемый контраст. Держись существующей структуры файла и его
   комментариев. Формула — не догма: если по итогам глаз на 7 темах видно, что нужен
   другой процент или другая база — меняй и **напиши в отчёте, что и почему**.
2. **Компоненты** — новая папка `client/src/projects/components/app-topbar/`:
   `app-topbar.tsx` (+ `topbar-logo`/`topbar-nav`/`topbar-auth` — отдельными файлами или
   внутри одного, как сочтёшь; ориентир — структура из хендоффа).
   Пропсы строго по хендоффу:
   ```ts
   interface AppTopbarProps {
     active: 'home' | 'competitions' | 'groups' | 'normatives' | 'records' | 'about';
     user?: { name: string; avatarUrl?: string } | null;
     onLogin(): void;
     onLogout(): void;
   }
   ```
   **Но**: внутри компонента дёргай `useAuth()` сам (как HomeHeader), а пропсы `user`/
   `onLogin`/`onLogout` сделай необязательными override'ами. Обоснование: страниц 4,
   и прокидывать auth в каждую — лишняя работа; хендофф писался до того, как `useAuth`
   стал модульным синглтоном. **Если решишь иначе — напиши почему в отчёте.**
3. Высота ~46px, `position: sticky; top: 0`. **z-index: `z-50`** — обосновано:
   `HomeHeader` = `z-30`, шапка таблицы результатов = `z-10`, а диапазон 90–130
   плотно занят оверлеями results_main (фильтры, попапы, модал логина). Полоса должна
   быть выше шапок, но НИЖЕ оверлеев.
4. Мобильный (<md): nav сворачивается в бургер, лого и auth остаются. Панель бургера —
   по образцу мобильного меню HomeHeader (195–257), но без футера «Countries — coming 2026».
5. **Поставь топбар на `about.html`**: `client/src/projects/about-project/about.tsx`
   (сейчас там заглушка `ABOUT 11 : {testP2} / {testP1}` — контент не трогай, просто
   добавь `<AppTopbar active="about" />` сверху) и подключи `<UI_ThemeDevTool />`
   рядом, чтобы Влад мог щёлкать темы. Больше **никаких страниц не трогай** —
   competitions/groups/results_main делает Sonnet.
6. Проверь вживую сам, до отчёта: `about.html?themes`, все 7 тем × light/dark,
   десктоп и мобильная ширина, гость и залогиненный. Гость проверяется сразу;
   для залогиненного — если не выйдет войти, скажи в отчёте, не выдумывай.

## Проверка

```bash
cd client
npx tsc --noEmit          # обязательно
npm run dev               # :5173, прокси на API :5078
```
API поднимать: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/Swimm.API --urls http://localhost:5078`
(если :5078 занят Visual Studio — подними на :5079 и запусти клиент с
`SWIMM_API_TARGET=http://localhost:5079`; в `.claude/launch.json` есть конфиг `client-5079`).
Открывай `http://localhost:5173/about.html?themes`.

**Сделай скриншоты** ключевых тем (минимум: nexaverse, ocean, competition-dark,
warm — там где контраст спорный) в light и dark и приложи пути к отчёту — Влад
принимает вид по ним.

## Footguns

- Build-lock :5078 (осиротевший `dotnet run` от Visual Studio): гаси через
  `Get-CimInstance Win32_Process -Filter "Name='Swimm.API.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`,
  либо работай на :5079. После проверки останови всё, что поднял.
- `useTheme` перечитывает `data-theme` от `activity_type` — на about Redux-темы нет,
  дефолт `:root` = training-dashboard. ThemeDevTool ставит `data-theme` напрямую,
  этого достаточно.
- Комментарии в коде русские, идентификаторы английские — как в соседних файлах.
- Клиент — Tailwind v4 через Vite, **не** трогай `server/Swimm.API/Styles/admin.css`
  и `npm run css:build` (это про админку, не про клиент).

## Вне скоупа (не делать)

- Не трогать `home.html` / `home-header.tsx` / `home.tsx` — главная остаётся как есть.
- Не ставить топбар на competitions, groups, results_main — это таск Сонета
  (`docs/tasks/topbar-rollout-sonnet.md`).
- Не чинить z-index-баг в `results-main-project.tsx:247/259` — тоже Сонету.
- Не делать страницы normatives/records и не превращать их пункты в ссылки.
- Не коммитить: оставь изменения в рабочем дереве, я приму и закоммичу сам.
