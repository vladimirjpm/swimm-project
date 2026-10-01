# Задание Sonnet — Т3 таба Trainings: шторка/модал + формы и лайтбокс (01.10.2026)

## Контекст

Таб Trainings группы переделывается по high-fidelity хендоффу
`!design_handoff/design_handoff_trainings_tab/` (`README.md` — разделы **Forms**, **Tokens**,
**Light theme**, **Rules**; прототип `Trainings Tab.dc.html`, секции **1d** (шторки), **5a**
(перерыв, удаление, лайтбокс), **6b** (модалы десктопа)). План — `docs/plans/trainings-tab-redesign-plan.md`
(§4 этап Т3, §3½ «Модульные секции» и таблица «Каркас Т2»).

Правило хендоффа: **формы — шторки снизу на телефоне и модалы 480px по центру на десктопе,
никогда не в списке.** Сейчас формы тренировки и ссылки раскрываются прямо в ленте
(`group-sessions.tsx`), перерыв — прямо в карточке Next (`MyBreakControl`). Т3 заводит общую
оболочку и переносит в неё все формы таба, плюс новый лайтбокс.

Каркас таба (Т2) уже готов: `client/src/projects/hub-groups-project/components/trainings/`.
В нём реестр форм `trainings-sheets.tsx` — **пустой**; твоя задача — наполнить его.

## Решения (зафиксированы, не пересматривать)

1. **Оболочка — общий компонент `DeepSheet`** в `client/src/projects/components/deep/deep-sheet.tsx`
   (не внутри таба: пригодится другим экранам deep). Два варианта `variant`:
   - `'sheet'` (по умолчанию): **< 960px — шторка снизу**, **≥ 960px — модал 480 по центру**;
   - `'dialog'`: всегда диалог по центру (удаление — так в 5a и на телефоне).
   Это НЕ `MobileFiltersDrawer` (тот всегда в разметке ради состояния фильтров) и не
   `ConfirmDialog` (у того фиксированная раскладка «текст + 2 кнопки»). Причину впиши строкой в
   `docs/ui-components.md` (см. шаг 9).
2. **Устройство `DeepSheet`** (сверено с прототипом и `ConfirmDialog`):
   - портал в `document.body`; на корне портала — класс `useDeepThemeClass()`
     (`client/src/projects/components/deep/use-deep-theme-class.ts`), иначе роли `--t-*` пустые;
   - `z-[125]` — как у `ConfirmDialog` (выше переключателя темы, у него 120);
   - подложка — `var(--t-scrim)`, клик по ней и `Esc` закрывают (кроме `busy`); прокрутка
     страницы под слоем погашена (`document.body.style.overflow = 'hidden'` с возвратом, как в
     `ConfirmDialog`);
   - поверхность — `var(--t-surface-strong)`, рамка `1px var(--deep-card-border)`, тень
     `var(--deep-card-shadow)` (в светлой теме подложка светлая — без края и тени шторка
     сливается; в тёмной это не мешает);
   - **шторка** (телефон): `border-radius: 22px 22px 0 0`, прижата к низу, `padding: 8px 16px
     calc(20px + env(safe-area-inset-bottom))`, `gap: 14px`, сверху ручка `36×4`, радиус 2, цвет
     `var(--deep-card-border)`, по центру; верхний край — 1px цвета тона (`tone`, ниже);
     `max-height: 90dvh`, тело прокручивается, шапка и подвал на месте;
   - **модал** (десктоп и `'dialog'`): ширина 480 (`max-width: calc(100vw - 32px)`), радиус 18,
     `padding: 20px`, `gap: 14px`, по центру экрана;
   - **шапка**: заголовок `font-family: var(--deep-font-display)`, 18px; под ним `sub` —
     11.5px/700, `var(--t-text-3)`; справа кнопка ✕ 36×36 круглая, фон `var(--deep-seg-track)`,
     цвет `var(--t-text-3)`, `aria-label="Close"`. У `'dialog'` кнопки ✕ нет (выход — Cancel/Esc);
   - **подвал** (`footer`) — кнопки действия, ВСЕГДА снизу (зона большого пальца);
   - `role="dialog"`, `aria-modal="true"`, `aria-labelledby` на заголовок; при открытии фокус —
     на элемент `initialFocusRef`, иначе на первое поле ввода, иначе на ✕;
   - `tone: 'accent' | 'coach' | 'neutral'` — цвет верхнего края шторки и основной кнопки:
     accent → `--deep-accent-border` / `--deep-accent-chip` / `--deep-accent`; coach →
     `--deep-ow-border` / `--deep-ow-chip` / `--deep-ow`; neutral → `--deep-card-border`.
3. **Кнопки подвала** — экспортируй из `deep-sheet.tsx` `SheetButton`
   (`kind: 'cancel' | 'primary' | 'danger'`, `tone` для primary): высота 48, радиус 12,
   13px/800; cancel — рамка `--deep-card-border`, прозрачный фон, текст `--t-text-2`;
   primary — по `tone` (рамка border, фон chip, текст цвета тона); danger — рамка
   `--deep-danger`, фон `--deep-danger-soft`, текст `--deep-danger`. Раскладка подвала —
   `grid` `1fr 2fr` (Cancel + действие) или одна кнопка во всю ширину; у `'dialog'` — `1fr 1fr`.
4. **Поля форм** — экспортируй из `deep-sheet.tsx` `SheetField` (подпись сверху 11px/800
   `var(--t-text-3)`, `gap 5px`) и класс поля: высота 44, радиус 10, рамка `--deep-card-border`,
   фон `var(--t-input-bg)`, текст `var(--t-text)` 13px/700; дата и время — моноширинный
   (`hp-mono`) 13px/800; textarea — высота 64. Фокус — рамка `--deep-accent-border`
   (у формы тренера — `--deep-ow-border`).
5. **Состав реестра** (`trainings-sheets.tsx`, тип `TrainingsSheet` УЖЕ объявлен — не менять):
   `new-training`, `edit-training`, `add-video`, `delete-training`, `break`, `lightbox`.
   `person` — НЕ в Т3 (это Т6), оставь без записи.
6. **Форма тренировки** (`new-training` / `edit-training`, тон coach). Заголовок «New training» /
   «Edit training»; `sub` «Shows in the trainings list right away» (только у новой).
   Поля: Date + Start · optional (сеткой 1fr 1fr), Name, Note. Date по умолчанию
   `todayInIsrael()` (`components/lane-plans-api.ts`); у тренировки с временами
   (`session.has_results`) Date **заблокирован** и под ним строка «Date is locked — this training
   has recorded times.». Name обязателен, `maxLength 100`, placeholder «e.g. Underwater
   filming»; Note `maxLength 500`, placeholder «Optional». Подсказка (только у новой):
   «No times needed — add the training, then attach videos to it.» Кнопки: Cancel /
   «Create training» (у правки — «Save»). Запись — `sessions.create(input)` /
   `sessions.update(id, { ...input, pool_type: session.pool_type ?? null })` (как сейчас в
   `group-sessions.tsx`); ответ — строка ошибки (`string | null`) → показать над кнопками,
   шторка остаётся; `null` → закрыть.
7. **Форма ссылки** (`add-video`, тон accent). Заголовок «Add video», `sub` «Link to YouTube,
   Vimeo, Google Photos…». Поля по порядку:
   - **Training** — `select` по `sessions.data.sessions` (новые сверху), подпись опции
     `«Tue 29 Sep · <название>»` (название — `sessionLabel()` из `use-training-sessions.ts`);
     предвыбрана `sheet.sessionId`, иначе первая;
   - **Link** — `type="url"`, моноширинный 12px, `maxLength 1000`, рамка `--deep-accent-border`;
     справа внутри поля — чип источника (10px/800, фон `--deep-accent-chip`, текст
     `--deep-accent`): «YouTube» / «Vimeo» / «Google Photos» / «Photo» / «Link» — по адресу;
   - строка: **превью** 110×70, радиус 10 (только YouTube/Vimeo/картинка — через
     `HelperMedia.resolveThumbUrl`; нет превью — блока нет) + **тип** — сегмент из трёх равных
     кнопок `Video · Photo · Album` высотой 38 на дорожке `--deep-seg-track`, выбранный —
     `--deep-seg-active-bg` / `--deep-seg-active-fg`. Тип **угадывается по ссылке**
     (`guessKind` — перенеси из `group-sessions.tsx`), пока человек не выбрал сам; выбрал —
     больше не перезаписываем;
   - **Caption** — `maxLength 200`, placeholder «Optional»;
   - подсказка: «Only the group sees it. The site keeps just the link.» + у НЕуправляющего
     ` · N of 10 yours` (N = медиа этой тренировки с `mine === true`; 10 —
     `MaxMediaPerMember` сервера). Управляющему квоты нет — хвоста нет;
   - одна кнопка во всю ширину «Add video». Пока ссылка не начинается с `https://` — кнопка
     неактивна. Запись — `sessions.addMedia(sessionId, { media_type, source_type: sourceOf(kind,
     url), url, caption })` (`sourceOf` — перенеси из `group-sessions.tsx`).
8. **Удаление тренировки** (`delete-training`, `variant 'dialog'`). Заголовок «Delete training?»;
   сводка в рамке (`--deep-card-bg`, рамка `--deep-card-border`, радиус 12): название 13px/800 и
   моно-строка `«Tue 29 Sep · 19:30 · 3 links»` (время и ссылки — если есть). Текст 12.5px:
   есть ссылки — «The N video links are removed from the group. The videos themselves stay
   where they are (YouTube, Google Photos).»; нет — «Nothing else is removed — this training has
   no times and no links.» Кнопки `Cancel` (**фокус при открытии** — `initialFocusRef`) и
   `Delete` (danger). Запись — `sessions.remove(id)`.
9. **Перерыв** (`break`, тон accent). Заголовок «Taking a break», `sub` «You stay in the group;
   you just won't be asked». Блок «How long»: 4 чипа сеткой `repeat(4, 1fr)`, высота 44, радиус
   10 — `1 week · 2 weeks · 1 month · Pick date`; выбранный — `--deep-accent-chip` фон,
   `--deep-accent-border` рамка, `--deep-accent` текст; по умолчанию **2 weeks**. `Pick date` →
   под чипами поле даты. Строка «Back on» (карточка `--deep-card-bg`, рамка, радиус 12,
   `padding 12px 14px`): слева «Back on» 12px `--t-text-3`, справа дата моно 15px/800
   `«Wed 14 Oct»`. **Семантика сервера:** `until` — ПОСЛЕДНИЙ день перерыва (включительно), а
   «Back on» — день возвращения: **`until = backOn − 1 день`**. Сроки: 1 week → backOn = сегодня
   + 7; 2 weeks → + 14; 1 month → тот же день следующего месяца; Pick date — `min` = завтра,
   `max` = сегодня + 367 (сервер: `until` не дальше 366 дней). Сегодня — `todayInIsrael()`.
   Эффекты (11.5px/600 `--t-text-3`): «Skips N trainings · you won't count as "no answer".» (N —
   число занятий расписания `group.training_schedule.slots` (`day` ISO 1=Mon…7=Sun) с завтра
   по `until` включительно; расписания нет — первую фразу без «Skips N trainings ·») и
   «Answering "Going" before that ends the break.» Кнопка во всю ширину «Start break».
   Запись — `breaks.setBreak({ on_break: true, until })` (контекст таба); ошибка — над кнопкой.
10. **Лайтбокс** (`lightbox`, свой компонент, НЕ `DeepSheet`). Всегда тёмный: на корне портала
    класс **`theme-deep`** жёстко (не `useDeepThemeClass`), фон `#020a14` → используй роль
    `var(--deep-page-bg)` темы `theme-deep`; на весь экран, `z-[125]`. Список — медиа тренировки
    `sheet.sessionId` БЕЗ альбомов (альбом открывается наружу, как сейчас), старт с
    `sheet.index`. Шапка: ✕ 40×40 круг (`rgba(255,255,255,.1)` → роль `--deep-seg-track`), название
    тренировки 13px/800 и моно-строка «Tue 29 Sep · 1 / 3». Центр: плеер **16:9** — YouTube /
    Vimeo `iframe` (вынеси построение адреса встраивания из
    `projects/components/mix/swimmer-gallery/swimmer-gallery.tsx` (`getEmbedUrl`) в
    `HelperMedia.embedUrl(sourceType, url)` и используй в ОБОИХ местах), картинка —
    `<img>` `object-contain` через `HelperMedia.directImageUrl`, прочее видео — ссылка «Open ↗».
    Под плеером: подпись 15px/800 (если есть); строка 12px «Added by you · YouTube» — **автора
    сервер не отдаёт** (правило приватности ленты группы), поэтому «Added by you» только при
    `mine`, иначе только источник. Точки: текущая 18×6 `--deep-accent`, прочие 6×6
    `rgba(255,255,255,.3)` (→ `--deep-text-ghost`). Подвал (рамка сверху `--deep-card-border`):
    «Open on YouTube ↗» (`flex:1`, 48px, рамка `--deep-accent-border`, текст `--deep-accent`,
    `<a target="_blank" rel="noopener noreferrer">`) и «Delete» (если `can_delete`; рамка/текст
    `--deep-danger`) → подтверждение через существующий `ConfirmDialog`
    (`projects/components/confirm-dialog/confirm-dialog.tsx`), затем `sessions.removeMedia`;
    удалили последнюю — лайтбокс закрывается. Листание: свайп по горизонтали (порог 50px,
    `touchstart/touchend`), стрелки ← → на клавиатуре, `Esc` закрывает. Медиа перечитывается
    после удаления (это делает хук) — индекс держи в пределах списка.
11. **Где формы открываются** (старые инлайн-формы удалить):
    - `group-sessions.tsx`: «+ New training» → `openSheet({ kind: 'new-training' })`; Edit →
      `edit-training`; Delete тренировки → `delete-training`; «+ Add video» →
      `add-video` с `sessionId`; клик по плитке медиа → `lightbox` с индексом. Удалить
      `SessionForm`, `AddLinkForm`, состояния `creating` / правки / подтверждения удаления
      тренировки и перенесённые хелперы. ✕ на плитке (удалить ССЫЛКУ) — оставить как есть
      (`ConfirmDialog`). `openSheet` передай пропом из
      `trainings/sections/training-feed-section.tsx` (`GroupSessions({ state, mark, onOpenSheet })`)
      — `GroupSessions` не должен звать `useTrainingsTab()` сам.
    - `training-media-strip.tsx` (`projects/training-table/training-by-session/`): новый
      необязательный проп `onOpen?: (index: number) => void` — индекс в списке БЕЗ альбомов. Если
      задан — своего `UI_SwimmerGallery` полоса не рисует и по клику зовёт `onOpen`. Без него —
      поведение прежнее (журнал тренировок `training-by-session.tsx` не трогаем).
    - `group-pool.tsx` `MyBreakControl`: вместо раскрытия даты на месте — проп
      `onStart: () => void` (кнопка «Taking a break?» его зовёт). Состояние «⏸ On break until … ·
      I'm back» — без изменений. В `trainings/sections/next-training-section.tsx` передай
      `onStart={() => openSheet({ kind: 'break' })}`. Неиспользуемое (`maxBreakDate`, если
      осиротеет) — удалить или перенести в форму перерыва.
12. Тексты интерфейса — **английские** (как выше, дословно). Имена/названия на иврите — в
    `<bdi>`, у строк с ними `dir="auto"`. Никаких хексов в разметке, кроме перечисленного в п.10
    (и там — через роли). `scrollIntoView` не использовать.

## Что уже готово (не переделывать)

- Каркас таба: `components/trainings/` — `trainings-context.tsx` (`useTrainingsTab()`:
  `group`, `manages`, `sessions`, `breaks`, `openSheet`, `closeSheet`, `sheet`),
  `trainings-sheets.tsx` (тип `TrainingsSheet`, `TrainingsSheetProps<K>`, `SHEETS`,
  `TrainingsSheetHost` — хост уже стоит в корне таба), `trainings-section.tsx`,
  `trainings-sections.tsx`. Описание — таблица «Каркас Т2» в §3½ плана.
- Данные: `use-training-sessions.ts` (`create`, `update`, `remove`, `addMedia`, `removeMedia`,
  `busy`, `data` — после записи сам перечитывает список; `sessionLabel()`),
  `components/use-group-breaks.ts` (`setBreak` возвращает `string | null`), типы — `types.ts`
  (`TrainingSession`, `TrainingSessionMedia` c `mine`, `can_delete`; `HubGroupDetails.training_schedule`).
- `ConfirmDialog` — образец портала/темы/Esc/фокуса на Cancel; `useDeepThemeClass`;
  `HelperMedia` (`client/src/utils/helpers/helper-media.ts`).
- Роли темы: `client/src/projects/components/deep/deep-theme.css` (`--t-scrim`,
  `--t-surface-strong`, `--t-input-bg`, `--t-text*`, `--deep-*`). Правила ролей — `client/CLAUDE.md`
  «Роли `--t-*`».

## Шаги

1. `deep-sheet.tsx`: `DeepSheet`, `SheetButton`, `SheetField` (+ класс поля) по решениям 1–4.
   Хук ширины — `useMediaQuery(DEEP_DESKTOP_QUERY)` из `client/src/hooks/useMediaQuery.ts`.
2. `HelperMedia.embedUrl(sourceType, url)` + перевод `swimmer-gallery.tsx` на него.
3. Папка `components/trainings/sheets/`: `training-form-sheet.tsx`, `add-video-sheet.tsx`,
   `delete-training-sheet.tsx`, `break-sheet.tsx`, `training-lightbox.tsx`; общие хелперы ссылок
   (`guessKind`, `sourceOf`, подпись источника, формат даты «Tue 29 Sep») —
   `sheets/link-helpers.ts`. Каждая форма данные берёт из `useTrainingsTab()`.
4. Наполни `SHEETS` в `trainings-sheets.tsx` (обнови шапку-комментарий: реестр больше не пуст).
5. Переведи `group-sessions.tsx`, `training-media-strip.tsx`, `group-pool.tsx`,
   `training-feed-section.tsx`, `next-training-section.tsx` по решению 11.
6. Строка в `docs/ui-components.md` про `DeepSheet` (раздел крупных блоков, рядом с
   `MobileFiltersDrawer`): где лежит, два варианта, чем отличается от `MobileFiltersDrawer` и
   `ConfirmDialog`, z-125.
7. В `docs/plans/trainings-tab-redesign-plan.md` ничего не отмечай — статус ставит ревьюер.

## Тесты

Клиентского раннера тестов в проекте нет (Jest-зависимости — остаток CRA, не настроен) —
юнит-тестов не пиши. Проверка — `tsc` и сборка (ниже); живую проверку на стенде делает ревьюер.

## Проверка

```bash
cd client && npx tsc --noEmit -p .
cd client && npx vite build
```
Оба — без ошибок. `vite build` пишет в `client/dist` (в .gitignore) — это нормально.
Стенд НЕ запускай и `dotnet` не трогай: серверных правок в задании нет.

## Footguns

- Порталу нужен свой класс темы — без него `var(--t-*)` пустые и шторка прозрачная.
- Лайтбокс — класс `theme-deep` ЖЁСТКО (всегда тёмный), не `useDeepThemeClass()`.
- `z-index`: переключатель темы — 120; оверлеи — 125 (как `ConfirmDialog`).
- `until` перерыва — последний день включительно; «Back on» = `until + 1`. Даты — строки
  `yyyy-MM-dd` по Израилю, считай в UTC (`Date.UTC`), как `formatPlanDate` / `maxBreakDate`, —
  иначе часовой пояс машины сдвинет день.
- В рабочей копии много чужих незакоммиченных правок (Т1–Т2 и файлы Влада) — **ничего не
  коммить, `git add` не делай**, трогай только файлы из этого задания.
- Градиент в Tailwind-произвольном значении требует `bg-[image:…]` (см. `client/CLAUDE.md`).

## Вне скоупа (не делать)

- Шторка человека переклички (`person`) — Т6.
- Редизайн ленты (карточка, фильтры, «Sets & times», ★) — Т4; меток (`use-training-marks`) — Т8;
  пустые состояния и `club-media.tsx` `--deep-scrim` — Т9.
- Журнал тренировок (`training-by-session.tsx`) и `UI_SwimmerGallery` — кроме переезда
  `getEmbedUrl` в `HelperMedia`.
- Серверные изменения, новые ручки, новые токены в `deep-theme.css`.
