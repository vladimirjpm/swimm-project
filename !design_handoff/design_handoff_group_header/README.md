# Group Header — модульная спецификация (вариант 2b)

Дизайн: `Group Header Final.dc.html` (демо-данные: `group-header-data.json`).
Целевая страница: `results_main.html?group=<slug>` — заменяет текущую шапку группы
в `client/src/projects/results-main-project/results-main-project.tsx`.

## Дерево компонентов

```
GroupHeader                    // контейнер, rounded-14, card-shadow
├── GroupHeaderTop             // фон var(--theme-primary), текст var(--theme-mode-accent-text)
│   ├── GroupIdentity          // левая часть: иконка + имя (dir="auto") + official-бейдж + мета
│   └── GroupLinks             // правая часть: чипы links[] (WhatsApp / Instagram / Site)
├── GroupHighlights            // лента: grid repeat(4, 1fr), фон surface, рендерит highlights[]
│   └── HighlightCard          // ОДИН компонент, switch по type
│       ├── type="record"      // бейдж, имя+дистанция, результат, ссылка
│       ├── type="medals"      // место в зачёте, 🥇🥈🥉, ссылка
│       ├── type="video"       // превью + длительность + подпись
│       └── type="photo"       // превью + подпись + «вся галерея +N»
└── GroupTabs                  // нижний модуль: Overview/Members/Records ↗ | Competitions / 🔒Trainings
```

Правила модульности:
- Каждый модуль — отдельный файл-компонент со своими пропсами; GroupHeader только компонует.
- HighlightCard — единый компонент с дискриминированным union-пропом; новые типы
  (например «следующий старт», «юбилей участника») добавляются новым вариантом type,
  лента и шапка не меняются.
- GroupHighlights рендерит массив как есть: состав и порядок задаёт сервер.
- GroupTabs — уже существующая логика (ссылки на groups.html + локальный toggle
  competitions/trainings), вынести из results-main-project.tsx как есть.

## Пропсы (TypeScript)

```ts
interface GroupHeaderProps {
  group: HubGroupDetails;            // существующий DTO
  highlights?: Highlight[];          // НОВОЕ: лента скрыта, если пусто/undefined
  activeTab: 'competitions' | 'trainings';
  onTabChange(tab: 'competitions' | 'trainings'): void;
}

type Highlight =
  | { type: 'record'; badge: string; title: string; detail: string; url: string }
  | { type: 'medals'; badge: string; place: string; placeLabel: string;
      gold: number; silver: number; bronze: number; url: string }
  | { type: 'video'; label: string; duration: string; thumbUrl: string; url: string }
  | { type: 'photo'; label: string; extra?: string; thumbUrl: string; url: string };
```

## Расширение DTO / API

`GET /api/hub-groups/:slug` → добавить поле `highlights: Highlight[]`
(или отдельный `GET /api/hub-groups/:slug/highlights`, если данные тяжёлые).
`coach?: string` — опционально в мета-строку GroupIdentity.

## Темизация (обязательно)

НЕ хардкодить цвета — в приложении несколько тем + light/dark:
- Фон GroupHeaderTop: `var(--theme-primary)`; текст: `var(--theme-mode-accent-text)`.
- Полупрозрачные рамки/фоны бейджей и чипов: `color-mix(in srgb, var(--theme-mode-accent-text) N%, transparent)` — как в текущей шапке.
- GroupTabs: как сейчас — `bg-black/10` поверх primary (в демо — затемнение primary).
- Карточки HighlightCard: `var(--theme-mode-surface)`, рамка `var(--theme-mode-border-input)`,
  текст `var(--theme-mode-text)` / `var(--theme-mode-text-muted)`.

## Поведение

- Лента highlights: на <lg — горизонтальный скролл (flex + overflow-x) вместо grid.
- video/photo карточки кликабельны целиком (переход в галерею/плеер).
- RTL: имя группы и club_name — `dir="auto"` (как в текущем коде).
- Если highlights пуст — модуль не рендерится, шапка схлопывается до Top + Tabs
  (текущий вид, обратная совместимость).
