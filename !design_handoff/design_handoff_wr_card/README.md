# Handoff: UI_H2HEventCard · variant="record" (пловец vs masters WR)

## Overview
На странице пловца (`/swimmers/:id?tab=results&view=records`, секция `HeldRecordsSection` в `swimmer-project/components/swimmer-panels.tsx`) рядом с каждым официальным мастерским рекордом пловца показать мировой рекорд мастерс (WR) той же возрастной группы и бассейна, разрыв до него, держателя WR с флагом и датой. Рекорды группируются по возрастной ступени.

Реализуется НЕ новой карточкой, а параметром существующей `UI_H2HEventCard` (`components/mix/h2h/`): `variant="record"`. Вариант `"h2h"` (по умолчанию) остаётся без изменений.

## About the Design Files
Файлы в пакете — **дизайн-референсы в HTML**, не продакшен-код. Задача — воспроизвести их в клиентском React-коде проекта (`client/src/projects/components/mix/h2h/`), переиспользуя существующие компоненты и `h2h.css` / `deep-theme.css` / `record-badge.css`. Все цвета — только через токены `--deep-*`; хардкодов нет.

## Fidelity
**Hi-fi.** `wr-card-h2h.dc.html` собран на реальных `h2h.css`, `deep-theme.css`, `record-badge.css` из репозитория (копии в `assets/css/`). Всё, что добавлено сверх них, лежит отдельным файлом `h2h-record.additions.css` — переносится в `h2h.css` как есть.

## Screens / Views
Макет показывает две секции, каждая в трёх рамках: `.theme-deep` 640px, `.theme-deep` 343px (телефон — срабатывает `@container h2h (max-width:560px)`), `.theme-deep-light` 640px.

### 4a — `variant="h2h"` (текущее поведение, контроль регрессии)
Два пловца, cyan-плашка `.h2h-time__box` у победителя, бейдж `SB`, разрыв `.h2h-pool__delta--win`, имён нет, заголовков групп нет. Ничего не менять.

### 4b — `variant="record"`
Структура для каждой возрастной ступени (`ageKey`):

1. **Заголовок группы** `.h2h-group__head` — flex, gap 10px, padding 4px 4px 2px; текст «🏆 ISR · MASTERS 45-49» 13.5px/900, letter-spacing .04em, цвет `--deep-gold`; затем `<UI_RecordBadge kind="masters" />` (10.5px, padding 2px 6px); линия `.h2h-group__line` (flex:1, 1px, `linear-gradient(90deg, var(--deep-gold-border), transparent)`); справа «MASTERS WR» тем же кеглем, цвет `--deep-text-mute`.
2. **Карточки** `.h2h-event.h2h-event--record` — по одной на стиль+дистанцию. Фон `--deep-gold-soft`, рамка `--deep-gold-border`, hairline в шапке скрыт (`background: transparent`). Иконка — `UI_SwimmStyleIcon styleType="icon-len"` (белая подложка 132×90, как в h2h).
3. **Полосы бассейнов** `.h2h-pool` — по одной на `poolType` (25m, 50m). Сетка `minmax(0,1fr) 120px minmax(0,1fr)` из h2h.css. В record `align-items: start`; между полосами разделитель `.h2h-event--record .h2h-pool + .h2h-pool { border-top: 1px solid var(--deep-gold-border); padding-top: 8px }`.
   - **Слева (пловец)** — `UI_H2HTimeCell side="left" isWinner` (плашка всегда у пловца — это его рекорд): `.h2h-time__box` с фоном `--deep-gold-chip`, рамкой `--deep-gold-border`; время 19px/800 `--deep-gold`; под ним `.h2h-time__who` — имя пловца + флаг (`.h2h-time__flag` 16×12, radius 2px, `https://flagcdn.com/w40/{cc}.png`), 11px/700 `--deep-text-mute`, выравнивание вправо; дата `.h2h-time__date`; при `relayLeadOff` — `.h2h-time__extra > .h2h-badge.h2h-badge--relay` «Relay lead-off» (9.5px uppercase, рамка `--deep-card-border`, radius `--deep-radius-pill`, цвет `--deep-text-mute`, nowrap).
   - **Середина** — `UI_PoolIcon styleType="icon-text-center"` («--25m--» / «-----50m-----») и разрыв `.h2h-pool__delta.h2h-pool__delta--behind` со знаком «+» (пловец медленнее WR), цвет `--deep-live`. `padding-top: 8px`.
   - **Справа (WR)** — `UI_H2HTimeCell side="right"` без плашки: время 19px/800 `--deep-text`; `.h2h-time__who` флаг + имя держателя; дата WR. `padding-top: 6px` компенсирует рамку плашки слева — время и имена на одной высоте.

## Interactions & Behavior
- Время пловца — ссылка на протокол (`href` как в `HeldRecordsSection`); время WR — без ссылки, `title` = «Masters world record · {age} · {dist} {stroke} {SCM|LCM}».
- Узкий контейнер наследуется из `h2h.css`: ≤780 — компактная сетка; ≤560 — `.h2h-time__date` скрыта. Добавлять ничего не нужно.
- Hover — как у `.h2h-time__link` (`--deep-accent`).
- Нет WR для бассейна → правая ячейка `—` (`.h2h-time__empty`), разрыв не показывается.

## API changes
- `UI_H2HEventCard`: `variant?: 'h2h' | 'record'` (default `'h2h'`) → класс `h2h-event--record`.
- `UI_H2HPoolRow`: `deltaTone?: 'win' | 'behind'` → `.h2h-pool__delta--win` / `--behind`; в record всегда `'behind'`.
- `UI_H2HTimeCell` / `H2HPoolSide`: `who?: { name: string; countryCode: string }` → `.h2h-time__who`; `extras?: ReactNode` → `.h2h-time__extra`.
- Обёртка группы `.h2h-group` + `.h2h-group__head` (внутри `HeldRecordsSection`). Группировка: `ageKey` → `stroke+distance` → `poolType`.

## State Management / Data
Без клиентского состояния. `SwimmerHeldRecord` + новое поле на бассейн
`worldRecord?: { time: string; date: string; holder: string; countryCode: string }`. Разрыв на клиенте: `parseTimeToSeconds(time) − parseTimeToSeconds(wr.time)`, формат `+X.XX`. Имена/даты/времена WR в макете — заглушки.

## Design Tokens
Только `--deep-*` из `deep-theme.css`: `--deep-gold`, `--deep-gold-soft`, `--deep-gold-chip`, `--deep-gold-border`, `--deep-text`, `--deep-text-mute`, `--deep-text-faint`, `--deep-divider`, `--deep-card-border`, `--deep-radius-pill`, `--deep-live`. Красная дистанция на иконке — часть `UI_SwimmStyleIcon`, не токен.

## Assets
- Иконки стилей — `public/images/swimm-style-icon/*.png` (уже в репо).
- Флаги — flagcdn.com `w40/{cc}.png`; если внешние картинки нежелательны, взять существующий компонент флага проекта.

## Files
- `wr-card-h2h.dc.html` — макет (4a контроль, 4b целевой). Открывать из корня пакета (нужны `support.js`, `_ds/`, `assets/`).
- `h2h-record.additions.css` — все CSS-добавки к `h2h.css`.
- `assets/css/{h2h,deep-theme,record-badge}.css` — копии из репо (референс, прод не заменять).
- `assets/stroke/*.png` — иконки стилей для макета.
- `wr-gap-options.dc.html` — ранние варианты (1a–4a), только для истории.
