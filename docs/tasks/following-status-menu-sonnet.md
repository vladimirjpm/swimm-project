# Задание Sonnet — меню статуса в Following с описаниями (02.10.2026)

## Контекст

У тренера в табе группы **Following** (карточка состава `GroupMembersCard`) у каждого пловца есть
статус в группе (решение Влада Р74, 30.09.2026): **Active / On break / Doesn't train with us /
Inactive — left the group**. Сейчас это обычный `<select>` с короткими подписями —
`PersonStatusSelect` в `client/src/projects/hub-groups-project/components/person-status.tsx`.

Хендофф `!design_handoff/design_handoff_trainings_tab/` (README, раздел **Related places (6a)** →
«Following tab»; прототип `Trainings Tab.dc.html`, блок «Following tab · coach · status menu»,
строки ~110–116, данные `statusOpts` ~строка 1011) рисует вместо него **меню**: у каждого пункта
вторая строка — что этот статус значит, у текущего — ✓. Это было в строке Т8 плана редизайна и
вынесено; Влад попросил сделать (02.10.2026).

Пути — от `client/src/projects/hub-groups-project/`, если не сказано иное.

## Решения (зафиксированы, не пересматривать)

1. **Переделать `PersonStatusSelect` на месте**, а не заводить второй компонент: те же экспорт,
   имя и пропсы (`value`, `onChange`, `withInactive`, `compact`, `disabled`, `name`) — вызывающий
   (`components/group-cards.tsx`) не меняется. Логика записи остаётся у вызывающего, как сейчас
   (меню само ничего не пишет).
2. **Кнопка (триггер)**: высота 34, радиус 8, рамка и цвет — из существующего `TONE[value]`,
   фон `var(--t-input-bg)`, 12px/800, подпись текущего статуса (`LABEL`, при `compact` — `SHORT`)
   и справа «▾» (`--deep-text-mute`), `justify-content: space-between`, `max-width` как сейчас
   (150 / 96 при `compact`), подпись с многоточием. `aria-haspopup="listbox"`, `aria-expanded`,
   `aria-label` = «Status of <name>», `title` — как сейчас. `disabled` — как сейчас.
3. **Меню** (прототип 6a): `position: absolute`, под кнопкой с отступом 6px, **прижато к правому
   краю кнопки** (`right: 0` — контрол стоит в конце строки, у левого края на телефоне вылезло бы
   за экран), ширина 250px (но не шире `calc(100vw - 32px)`), радиус 12, фон
   `var(--t-surface-strong)`, рамка `var(--deep-ow-border)`, тень `var(--deep-card-shadow)`,
   `padding 4`, `gap 2`, `z-index 30`. `role="listbox"`, у пунктов `role="option"` +
   `aria-selected`.
4. **Пункт**: `padding 8px 10px`, радиус 8, ряд: колонка 12px под «✓» (`--deep-ow`, 12px/900, только у
   текущего), затем две строки — подпись 12.5px/800 (текущий — `--deep-ow`, остальные
   `--deep-text`; у `inactive` — `--deep-danger`) и описание 10.5px/600 `--deep-text-mute`. Текущий
   пункт — фон `var(--deep-ow-soft)`; наведение / фокус — фон `var(--deep-ow-chip)`. Высота пункта
   ≥ 44 (тап-таргет). Подписи пунктов — полные (`LABEL`) даже при `compact`.
5. **Описания — дословно** (по Р74, правдиво для нашего поведения; «until a date» из прототипа
   НЕ брать — перерыв, поставленный тренером, у нас бессрочный):
   - Active — «Trains with the group»
   - ⏸ On break — «Out of answers and lanes; answering Going ends it»
   - Doesn't train with us — «In records, never in the pool or lanes»
   - Inactive — left the group — «Hidden from the list; restore in My groups»
   Список пунктов — как сейчас: `inactive` только при `withInactive`.
6. **Поведение**: клик по кнопке — открыть/закрыть; выбор пункта — `onChange(next)` (если отличается
   от текущего) и закрыть; клик мимо (`mousedown` вне корня) и `Escape` — закрыть, фокус вернуть
   на кнопку; `Tab` — закрыть. Клавиатура: при открытии фокус на текущем пункте; `ArrowDown` /
   `ArrowUp` — по пунктам (по кругу), `Home` / `End`, `Enter` / `Space` — выбрать. Открыто одно меню
   — у каждого экземпляра свой `open`, закрытие по клику мимо это и обеспечивает. Образец закрытия
   по клику мимо / Escape — `DeepMenuChip` в `client/src/projects/components/deep/hero-identity.tsx`
   (его самого не трогать и не переиспользовать — у него свой вид чипа шапки).
7. Шапочный комментарий `person-status.tsx` обновить: «выпадашка» → меню с описаниями (6a).
8. Цвета — только роли (`--deep-*`, `--t-*`), без хексов. Тексты — английские, дословно.
   `scrollIntoView` не использовать.

## Что уже готово (не переделывать)

- `person-status.tsx`: `RosterStatus`, `LABEL`, `SHORT`, `TONE`, `statusNote`, `statusOf` —
  остаются, `statusNote` / `statusOf` используются в других местах.
- `group-cards.tsx`: `setStatus`, подтверждение Inactive (`pendingInactive`, `ConfirmDialog`),
  `breaks.setBreak` — логика записи не меняется.
- Тема: роли `--t-*` есть внутри класса темы страницы группы — меню не портал, класс темы ему не
  нужен.

## Шаги

1. `components/person-status.tsx`: триггер + меню по решениям 2–6; описания — новый
   `Record<RosterStatus, string>` рядом с `LABEL`.
2. Проверь `components/group-cards.tsx`: у строк состава / контейнера списка нет `overflow: hidden`
   или `overflow: auto`, которые обрежут меню (у `.deep-card` их нет); если есть у какого-то
   обёрточного элемента строки — скажи в отчёте, не переделывай вёрстку карточки сам.
3. `docs/ui-components.md`: если `PersonStatusSelect` там описан — обнови строку (меню с
   описаниями, клавиатура); если нет — добавь строку в раздел блоков группы.

## Тесты

Клиентского раннера тестов нет — юнит-тестов не пиши. Серверных правок нет. Проверка — `tsc` и
сборка; живую проверку делает ревьюер.

## Проверка

```bash
cd client && npx tsc --noEmit -p .
cd client && npx vite build
```
Оба без ошибок. Стенд и браузер не запускай, `dotnet` не трогай.

## Footguns

- В рабочей копии есть чужие файлы Влада (`!design_handoff/…`) — **ничего не коммитить, `git add`
  не делать**, трогать только файлы задания.
- Строка состава на телефоне переносит статус и уровень на вторую строку (`flex-wrap`, `ml-auto`) —
  меню прижато вправо, чтобы не вылезти за экран; проверь, что ширина меню ограничена
  `calc(100vw - 32px)`.
- `mousedown` вне корня закрывает меню раньше `click` по другому триггеру — открытие другого
  статуса одним кликом должно работать (так же, как у `DeepMenuChip`).
- Имена пловцов в `aria-label` — как пришли (иврит), без `<bdi>` (это атрибут).

## Вне скоупа (не делать)

- Статус в шторке человека (`trainings/sheets/person-sheet.tsx`) и в перекличке тренера — там свои
  контролы, не трогать.
- Дата окончания перерыва в меню, перерыв «до даты» тренером.
- Сервер, новые статусы, новые токены.
