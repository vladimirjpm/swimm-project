# Handoff: Competition Header + Overview (results_main.html)

## Overview
Модульная шапка соревнования + таб Overview для `results_main.html` (показывается, когда выбрано соревнование, нет `?group=`). Итоговое направление — **вариант 1b «Афиша»** (десктоп) + **2a** (мобайл 390px). Вариант 1a «Панель» оставлен в файле для истории, НЕ реализовывать.

## About the Design Files
`Competition Overview.dc.html` — **дизайн-референс в HTML** (прототип вида и поведения), не production-код. Задача — воспроизвести дизайн в существующем стеке клиента: React 18 + TS + Tailwind v4, паттерны `client/src/projects/results-main-project/components/group-header/` (GroupHeaderTop / GroupTabs / HighlightCard) и правило парных токенов из `client/CLAUDE.md`. Открой файл в браузере: сверху блок 2a (мобайл), ниже 1a и 1b (десктоп). Tweak `loggedIn` показывает/скрывает всё персональное.

## Fidelity
**High-fidelity** по композиции, отступам и типографике. Цвета в файле — литералы темы competition-emerald; в коде ВСЕ цвета заменить на тем-токены (см. Design Tokens). Данные (имена, времена, числа) — фиктивные.

## Структура (вариант 1b)

```
CompetitionHeader (rounded 14px, overflow hidden, box-shadow: var(--theme-mode-card-shadow))
├── Hero top          — bg: --theme-primary (в макете градиент 120deg #10b981→#087a58 — допустимо однотонно), text: --theme-mode-accent-text, padding 22-24px
│   ├── иконка 64px (rounded 16, border/bg = color-mix accent-text 35%/20%)
│   ├── название 26px/900 + бейдж «N Days» (только многодневные; border accent-text 40%, bg 15%)
│   ├── мета 12.5px/600 opacity .9: 🇮🇱 · даты · 50m pool · Swimmers: X · Clubs: X · Results so far: X
│   └── справа: [＋ Add media (video / photo)] (bg white, text primary-dark; только isAuthenticated)
│              [Change ▾] (border accent-text 40%, bg 12%) — существующий селектор соревнования
├── Персональная полоса — ТОЛЬКО залогиненному, гостю НЕ рендерится. bg surface, 3 карточки:
│   ├── «⭐ {Имя}» — бейдж-имя + строка «712 pts · 🥇 1 🥉 1 · 🏅 record» (каждая часть — если есть);
│   │   «My swims today — N →» → results_main.html?competitionId=X&tab=swims&filter=my;
│   │   строки заплывов: дистанция … время + PB + медаль (если есть). БЕЗ live («next: …» нет!)
│   ├── «❤️ Favorites» — «My favorites here — N →» → …&tab=swims&filter=favorites; строки пловцов
│   └── «My media» (flex 1.4) — счётчик + [＋ Add video / photo] + кубики-превью (те же, что в Media)
├── CompetitionTabs   — bg --theme-primary + внутр. bg-black/10 (паттерн GroupTabs), 13px/700
│   Overview* | Swims 896 | Clubs 18 | Records 3 (только если есть) | Media 11
│   справа в той же строке: тогглер Combine All Results (условия видимости прежние)
└── Контент Overview (grid 12 col, gap 12):
    ├── Левая (span 8): Feature «Best swim of the competition» (время 38px/900, поинты primary),
    │   New records (строки: бейдж типа + событие+время + пловец·клуб + Day N + ›, «Open Records tab →»),
    │   Media (грид 4 кубика 16:9 + «Open Media tab →»)
    └── Правая (span 4): Summary (Results so far / Days / Swimmers / Clubs — строки label:value),
        Top clubs (шапка # Club Medals Rating; строки: № имя 🥇🥈🥉 рейтинг зелёным; сорт по рейтингу; «Clubs tab →»),
        Top clubs · Men (♂) / Top clubs · Women (♀) — две карточки топ-3 с рейтингом
```

### Мобайл (2a, <lg)
- Шапка: иконка 44px, название 17px + «3 Days», мета одной строкой; вторая строка: сводка + [＋ Add media]; Change компактный.
- Персональная полоса: горизонтальный скролл, карточки flex:none width 230px.
- Табы: горизонтальный скролл; Combine НЕ в табах (остаётся в фильтрах Swims).
- Контент одной колонкой: Best swim → Records (списком, 2 строки на запись) → Media (2 кубика) → Top clubs → Men/Women 2×1.

## Навигационный контракт (всё кликабельно)
- Клуб → таб Clubs с выбранным клубом: `?competitionId=X&tab=clubs&clubId=Y` (drill-down: пловцы и результаты клуба).
- Пловец → страница пловца (пока не сделана — линк-заглушка, зарезервировать URL).
- Заплыв/эстафета → `results_main.html?competitionId=X&tab=swims&…` с параметрами, открывающими нужный заплыв + предустановка фильтров (дистанция/стиль/heat) + скролл/подсветка строки.
- **Новый параметр `filter`** (единый, расширяемый): `filter=my` (мои заплывы), `filter=favorites` (заплывы избранных). Swims читает его из URL и предустанавливает фильтр.

## Interactions & Behavior
- Табы — локальный toggle (паттерн GroupTabs), `?tab=` в URL, шапка не перезагружается.
- Overview — дефолтный таб; результаты Swims грузятся в фоне ПОСЛЕ Overview (lazy).
- Live-результаты НЕ показываем: никаких «next: …», пульсов и автообновлений в персональном блоке.
- Add media → существующий `AddLinkModal` с `fixedCompetitionId`; гостю скрыт (или логин-модал).
- Change ▾ → существующая панель выбора соревнования (категории/поиск/сезон).
- Медиа-кубик → лайтбокс `UI_SwimmerGallery`; «Open Media tab →» / таб Media → грид больших кубиков.
- Records-таб рендерится только если рекорды есть; персональная полоса — только залогиненному (гостю ничего).
- Блоки 2–5 скрываются по-отдельности при отсутствии данных; суперлативы (Best swim, Top clubs) вычислимы из результатов — пустого дэшборда не бывает.

## State Management
- `activeTab: 'overview'|'swims'|'clubs'|'records'|'media'` (+ `?tab=`), `filter` из URL.
- Данные: `/api/competitions` (метаданные), новый `GET /api/competitions/{id}/overview` (рекорды, суперлативы, топ-клубы incl. по полу, сводка, дни), `/api/media/results?competitionId=`, избранное/мои — существующие API favorites.

## Design Tokens
Поверхности/текст — существующие mode-токены: page-bg `#f5f5f7`, surface `#fff`, surface-alt `#fafbfd`, border `#eef1f6`, border-input `#d6e0da`, text `#1a1a1a`, secondary `#5b6470`, muted `#aab0bd`, card-shadow `0 1px 2px rgba(0,0,0,.05)`. Акцент: `--theme-primary` (#10b981 в макете), текст на нём `--theme-mode-accent-text`, бейджи: bg `color-mix(primary 15%)` / text primary(-hover). Пол: male `rgba(29,78,216,.12)/#1d4ed8`, female `rgba(190,24,93,.12)/#be185d` (по образцу row-male/female).

**НОВЫЕ токены персональной полосы** — добавить во ВСЕ темы в `client/src/index.css` (light и dark, парные, контраст ≥ 4.5:1), пока с текущими значениями:
- `--theme-personal-bg: #fffdf6` (dark: тёплый тёмный, напр. `#241f16` — прецедент `--theme-mode-me-highlight`)
- `--theme-personal-border: rgba(212,175,55,.45)`
- `--theme-personal-accent: #8a6d1a` (бейджи, стрелки, PB)
- `--theme-personal-badge-bg: rgba(212,175,55,.18)`
Компоненты полосы красятся ТОЛЬКО этими токенами — цель: настройка per-theme без правки компонентов.

Радиусы: шапка 14, карточки 12, вложенные 10, превью 7-8, пилюли 999. Типографика: system-ui стек; название 26/900 (моб 17), заголовки секций 14/800, тело 12.5/700, мета 12/600, бейджи 10/800 uppercase ls .04-.05em, рейтинг 12.5/800.

## Модульность
- `HighlightCard` → общий `components/highlights/`, generic-контракт по `type` (новый вид = новый case; неизвестный тип → null).
- Карточки персональной полосы — тот же generic-паттерн.
- Медиа-кубик — один компонент для Media, My media и таба Media.

## Assets
Нет бинарных ассетов. Превью медиа — плейсхолдеры (striped), флаг `UI_FlagEmoji`, медали/иконки — эмодзи (🥇🥈🥉⭐❤️🏅▶), пол — символы ♂/♀ в цветных кружках.

## Files
- `Competition Overview.dc.html` — прототип: блок 2a (мобайл), 1a (отклонён), 1b (утверждён).
- `NAV-CONTRACT.md` — копия навигационного контракта и токенов.
