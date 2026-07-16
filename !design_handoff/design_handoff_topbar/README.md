# SwimHub Topbar — глобальная полоса с навигацией и auth

Дизайн: `Auth Header Options.dc.html` (вариант 1b) + `Topbar Themes.dc.html` (все темы).
Добавляется НА ВСЕ страницы (results_main, groups, competitions, normatives, records, about)
над существующими шапками. На главной (home) заменяет текущую тёмно-синюю навигацию.

## Компоненты

```
AppTopbar                       // фиксированная полоса, height ~46px
├── TopbarLogo                  // иконка S + "SWIMHUB"
├── TopbarNav                   // Home / Competitions / Groups / Normatives / Records / About
│                               // активный пункт: цвет text + underline 2px accent
└── TopbarAuth                  // залогинен: аватар-кружок + имя + ▾ (dropdown: profile, logout)
                                // гость: кнопка Login (bg accent)
```

## Пропсы

```ts
interface AppTopbarProps {
  active: 'home' | 'competitions' | 'groups' | 'normatives' | 'records' | 'about';
  user?: { name: string; avatarUrl?: string } | null;  // null → кнопка Login
  onLogin(): void;
  onLogout(): void;
}
```

## Токены (добавить в index.css)

Цвета полосы ДЕРИВИРУЮТСЯ из темы — не рисовать вручную для каждой:

```css
/* в :root (light) — общая формула, работает для любой data-theme */
:root {
  --theme-topbar-bg: color-mix(in srgb, var(--theme-primary) 22%, #0b0e14);
  --theme-topbar-text: #ffffff;
  --theme-topbar-text-muted: rgba(255, 255, 255, 0.62);
  --theme-topbar-accent: var(--theme-accent);
  /* текст на accent (лого, кнопка Login, аватар): у большинства тем белый;
     у тем со светлым accent (NexaVerse #2dd4bf) — тёмный. Проще: */
  --theme-topbar-accent-text: var(--theme-text-active);
}

:root[data-mode="dark"] {
  --theme-topbar-bg: color-mix(in srgb, var(--theme-primary) 14%, #141922);
  --theme-topbar-text: #eef1f6;
  --theme-topbar-text-muted: rgba(238, 241, 246, 0.55);
}

/* точечные переопределения, если формула даёт спорный результат: */
:root[data-theme="training-nexaverse"] { --theme-topbar-accent-text: #0a2239; }
```

Проверено на всех 7 темах (см. Topbar Themes.dc.html): dashboard, nexaverse, ocean,
emerald, blue, warm, competition-dark + dark-mode.

## Правила

- Полоса position:sticky top:0, z-index выше шапек страниц.
- Активный пункт: `--theme-topbar-text` + `border-bottom: 2px solid var(--theme-topbar-accent)`;
  остальные: `--theme-topbar-text-muted`, hover → text.
- Кнопка Login: bg `--theme-topbar-accent`, текст `--theme-topbar-accent-text`, radius 8px.
- Аватар: 30px кружок, bg accent, инициал; при avatarUrl — картинка. Dropdown: Profile / Logout.
- Мобильный (<md): nav сворачивается в бургер, лого + auth остаются.
- RTL-страницы: полоса остаётся LTR (лого слева, auth справа) — как на главной.
