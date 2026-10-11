# Задание Sonnet — Т9 таба Trainings: пустые состояния (01.10.2026)

## Контекст

Таб Trainings группы переделывается по хендоффу `!design_handoff/design_handoff_trainings_tab/`
(`README.md` — раздел **Empty states (4a)**; прототип `Trainings Tab.dc.html`, секция
`id="4a"`, строки ~198–290: гость, участник без расписания, участник с пустой лентой, тренер без
расписания, тренер с первой тренировкой без видео). План — `docs/plans/trainings-tab-redesign-plan.md`
(§3½ — правило модульных секций; §4 строка Т9).

Сейчас пустые места — одна серая строка текста («No regular schedule yet — …», «No trainings
yet.»), а гостю — пунктирная плашка «🔒 Trainings and lane plans are visible to group members
only.» без действия. Т9 делает их одним приёмом прототипа: карточка в пунктирной рамке, круг с
глифом, заголовок, одна строка «откуда это появится»; у тренера — кнопки действия, у участника
— только объяснение. Плюс мелкий хвост: токен затемнения в `club-media.tsx`.

Пути ниже — от `client/src/projects/`, если не сказано иное.

## Решения (зафиксированы, не пересматривать)

1. **Общая карточка пустого состояния** — новый файл
   `hub-groups-project/components/trainings/trainings-empty.tsx`, компонент
   `TrainingsEmpty({ glyph, title, text, children? })` (`children` — строки деталей и кнопки под
   текстом). Вид (прототип 4a): `border-radius 16px`, `border: 1px dashed var(--deep-card-border)`,
   `padding 24px 18px`, колонка `align-items: flex-start`, `gap 10px`; круг 40×40 радиус 50%,
   фон `--deep-seg-track`, глиф 17px (`aria-hidden`); заголовок 15px/800 `--deep-text`; текст
   12.5px/600 `line-height 1.5` `--t-text-3`. Корень — `<section>` с `aria-label` = `title`.
   Кнопки действия (где есть) — высота 44, радиус 12, 13px/800, ряд с переносом, `gap 8`,
   `margin-top 4px`:
   - главная — рамка `--deep-ow-border`, фон `--deep-ow-chip`, текст `--deep-ow`;
   - вторая — рамка `--deep-card-border`, фон прозрачный, текст `--t-text-3`.
2. **Гость / не участник** (таб `Trainings` под замком; сейчас `lockNotice` в
   `hub-groups-project/group-page.tsx`): `TrainingsEmpty` с глифом «🔒», заголовком «Members only»,
   текстом «Trainings and lane plans are visible to group members only.», под ним две строки
   деталей 12px/700 `--t-text-3` (колонка, gap 6): «◷ <дни>» — `scheduleDaysLabel(group.training_schedule)`
   (тот же хелпер, что у `GroupStickyStatus`; нет расписания — строки нет) и «<место> · <бассейн>»
   (`training_schedule.place` в `<bdi>`, `pool_type`; нет обоих — строки нет). Под ними —
   **существующий** `GroupMembershipChip` (`hub-groups-project/components/group-membership.tsx`)
   с тем же `membership`, что получает `GroupHero` (`{ isAuthenticated, status: membershipStatus,
   join, leave }`): он уже умеет «+ Join group» / «+ Request to join», вход для анонима и
   «Request sent». Своей кнопки не писать.
3. **Нет расписания — вид Next** (нет ближайшего занятия: `nextSessions(group)` пуст):
   - участник (`sections/member-next-section.tsx`): глиф «◷», «No weekly schedule yet», текст
     «When the coach sets training days, the next training and “Are you coming?” will appear here.»;
   - тренер (`sections/coach-next-section.tsx`): глиф «◷», «No weekly schedule yet», текст «Set
     training days and members can answer “Going” for each one. Lanes and roll call work from the
     schedule.», кнопки: главная **«Set schedule»** — открывает таб Admin и прокручивает к карточке
     расписания (п. 5); вторая **«+ New training»** — `openSheet({ kind: 'new-training' })`.
     (В прототипе вторая — «+ One-off training», но тренировка из ленты не создаёт занятие в Next —
     подпись честная: «+ New training».)
   - Обе — `TrainingsSection` с `bare` и `TrainingsEmpty` внутри (на телефоне — обёрнуть в
     `deep-panel-row`, как лента в `training-feed-section.tsx`, чтобы держать боковую линию
     сегментов); статус `empty` рамки и старые строки текста убрать.
4. **Lanes без расписания** — участнику (`!manages`) сегмент `Lanes` скрыт, пока у группы нет
   расписания (`!group.training_schedule`) И нет ни одного опубликованного плана. Для этого
   контекст (`trainings/trainings-context.tsx`) грузит `lanePlans` ВСЕМ, кто видит таб (сейчас —
   только `manages`; сервер участнику отдаёт только опубликованные — так и нужно). Поле
   `lanePlans` комментарий обнови: «тренеру — все, участнику — опубликованные». Правило — функцией
   в контексте: `lanesHidden: boolean` (`!manages && !group.training_schedule && (lanePlans == null
   || lanePlans.length === 0)`) — пока список не пришёл, тоже скрыт (без мигания сегмента).
   `trainings-segments.tsx` при `lanesHidden` рисует два сегмента; если вид уже `lanes` (пришли
   по `?view=lanes`), а он скрыт — контекст переключает на `next` (через `applyView`, без
   подтверждения — правок у участника нет). Тренеру Lanes виден всегда (он может создать план).
   ⚠ Т6 читает `lanePlans` в `coach/coach-pool-block.tsx` только у тренера — участнику это поле
   там не используется, менять тот файл не надо.
5. **«Set schedule»** — новый необязательный проп `onOpenSchedule?: () => void` у
   `GroupTrainingsTab` (`trainings/trainings-tab.tsx`) → поле `openSchedule?: () => void` в
   контексте → кнопка в `coach-next-section.tsx` (нет пропа — кнопки нет). В `group-page.tsx` таб
   `trainings` получает `cards: (nav) => …` (как у `overview`) и передаёт
   `onOpenSchedule={() => openScheduleCard(nav)}`, где `openScheduleCard`: `nav.go('admin')`,
   затем `window.setTimeout(..., 0)` — найти `[data-card="training-schedule"]` и
   `window.scrollTo({ top: window.scrollY + el.getBoundingClientRect().top - 120, behavior: 'smooth' })`.
   Для этого в `components/deep/entity-page.tsx` (`EntityPanel`) обёртке карточки добавь
   атрибут `data-card={card.id}` — одна строка, общая для всех страниц-сущностей.
6. **Пустая лента** (`trainings/feed/training-feed.tsx`, ветка `all.length === 0`): вместо строки —
   `TrainingsEmpty` с глифом «▶», «No trainings yet», текст участнику «Trainings show up here after
   the coach adds one or imports times. Then you can attach videos and photos to them.», тренеру —
   «Add one — e.g. “Underwater filming” — and attach videos to it. Times aren’t needed.» Кнопку
   «+ New training» в карточку НЕ добавлять — она у тренера уже есть над лентой (телефон) и в
   шапке колонки (десктоп). Пусто после фильтра («No trainings with times yet.» / «…videos yet.»)
   — оставить строкой, как сейчас.
7. **Тренировка без видео** (`trainings/feed/training-card.tsx`): если `session.media.length === 0`
   и `canAddMedia` — вместо ссылки «+ Add video» в ряду кнопок рисуется плитка на месте полосы
   медиа: кнопка 86×60, радиус 10, `border: 1px dashed var(--deep-card-border)`, фон прозрачный,
   внутри «+» 16px и «Add video» 10.5px/800 `--deep-accent` столбиком; справа от плитки (ряд, gap
   10, `align-items: center`) строка 11px/700 `--t-text-3` «Only the group sees them.» Клик —
   `openSheet({ kind: 'add-video', sessionId: session.id })`, как у ссылки. Ссылка «Sets & times»
   остаётся в ряду. Есть медиа — всё как сейчас (полоса + ссылка «+ Add video»). Нельзя добавлять
   (`!canAddMedia`) — плитки нет.
8. **`club-media.tsx`** (`club-project/components/club-media.tsx`, строка ~115):
   `var(--deep-scrim, rgba(0,0,0,.55))` → `var(--t-scrim)` (роль из `deep-theme.css`; фоллбек
   больше не нужен — страница клуба в классе темы).
9. Тексты — английские, дословно как выше. Цвета — только роли. Имена/места на иврите — `<bdi>`.
   Глифы — текстом, `aria-hidden`.

## Что уже готово (не переделывать)

- `trainings/trainings-section.tsx`: `TrainingsSection` (`bare`, `status`), `MarkDot`.
- `trainings/trainings-context.tsx`: `lanePlans` / `reloadLanePlans`, `applyView` / `setView`,
  `openSheet`, `manages`, `isDesktop`, `group`.
- `trainings/trainings-segments.tsx` — сегменты и метки (Т8).
- `trainings/trainings-sheets.tsx` — формы `new-training`, `add-video` уже есть (Т3).
- `trainings/next/next-format.ts`: `nextSessions(group)`.
- `group-page.tsx`: `scheduleDaysLabel`, `membershipStatus`, `join`, `leave`, `isAuthenticated`,
  `GroupMembershipChip` (через `GroupHero`), карточка Admin `training-schedule`
  (`GroupScheduleEditor`).
- Т8: метки — не трогать `use-training-marks.ts`.

## Шаги

1. `trainings/trainings-empty.tsx` (решение 1).
2. `group-page.tsx`: `lockNotice` → `TrainingsEmpty` (решение 2); `cards: (nav) => …` у таба
   `trainings` + `onOpenSchedule` (решение 5).
3. `components/deep/entity-page.tsx`: `data-card` у обёртки карточки.
4. `trainings/trainings-tab.tsx` + `trainings-context.tsx`: проп и поле `openSchedule`; `lanePlans`
   всем; `lanesHidden` + откат вида (решение 4).
5. `trainings/trainings-segments.tsx`: скрыть `Lanes` при `lanesHidden`.
6. `sections/member-next-section.tsx`, `sections/coach-next-section.tsx` (решение 3).
7. `feed/training-feed.tsx` (решение 6), `feed/training-card.tsx` (решение 7).
8. `club-project/components/club-media.tsx` (решение 8).
9. `docs/ui-components.md`: строка про `TrainingsEmpty` в разделе блоков (рядом с `TrainingsSection`,
   если он там есть; нет — в подходящем разделе крупных блоков) и упоминание `data-card` у
   `EntityPage`, если страница-сущность там описана.
10. План и передачу не трогай — их обновит ревьюер.

## Тесты

Клиентского раннера тестов нет — юнит-тестов не пиши. Серверных правок нет. Проверка — `tsc`
и сборка; живую проверку делает ревьюер.

## Проверка

```bash
cd client && npx tsc --noEmit -p .
cd client && npx vite build
```
Оба без ошибок. Стенд не запускай, `dotnet` не трогай.

## Footguns

- В рабочей копии есть чужие файлы Влада (`!design_handoff/…`) — **ничего не коммитить,
  `git add` не делать**, трогать только файлы задания.
- `lockNotice` рисуется вне `TrainingsTabProvider` — `TrainingsEmpty` не должен зависеть от
  контекста таба (только пропсы).
- `GroupMembershipChip` уже стоит в шапке группы — второй экземпляр в замке должен работать от
  тех же `join` / `leave` (одно состояние членства страницы), не заводи свой `useHubGroupMembership`.
- Хуки в контексте — всегда в одном порядке; эффект отката вида — `useEffect` по `lanesHidden` и
  `view`.
- Глобальный `entity-page.tsx` используют страницы клуба, спортсмена, группы и `/records` —
  меняй только атрибут обёртки, ничего больше.
- `scrollIntoView` не использовать (прокрутка — `window.scrollTo` с отступом, как в `nav.go`).
- Роли `--t-*` есть только внутри класса темы — на страницах группы и клуба он стоит.

## Вне скоупа (не делать)

- Меню статуса в Following с описаниями (было в строке Т8 плана) — отдельно.
- Метки вне таба, сервер, новые эндпоинты, новые токены.
- «One-off training» как занятие в Next (нужна модель разовых занятий — не сейчас).
- Пустые состояния других табов группы (Overview, Results, Media).
