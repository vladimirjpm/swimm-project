# Таб Trainings группы — редизайн по хендоффу (план, 01.10.2026)

Источник: `!design_handoff/design_handoff_trainings_tab/` — `README.md` (структура, формы,
токены, правила), `marks-spec.md` (метки внимания), `Trainings Tab.dc.html` (прототип —
открыть в браузере; `support.js` — только его движок). Fidelity — **high**: цвета, текст,
радиусы и отступы финальные, собираем на `deep-theme.css` и готовых `.deep-*`, стили
прототипа не копируем.

Статус: **Т0–Т6 сделаны 01.10.2026 (сервер, каркас, шторки, лента, Next участника и тренера); Т7 отдан Сонету, ждёт приёмки — новый чат начинает с §0 [trainings-tab-handoff.md](trainings-tab-handoff.md).** Исполнители — §5.

## 1. Для чайника — что меняется

Сейчас таб Trainings на телефоне у тренера — одна очень длинная страница: карточка «Next
training» с полным списком людей, под ней «Trainings & videos», под ней ссылка на отдельный
экран «Training log» с временами. Формы раскрываются прямо в списке и сдвигают его.

Станет:
- **телефон** — три коротких вида переключателем `Next · Trainings · Lanes`:
  - *Next* — ответ сразу на три ближайшие тренировки, своя дорожка, мини-бассейн; у тренера —
    перекличка, где у человека ОДНА кнопка статуса и меню ⋮;
  - *Trainings* — одна лента тренировок вместо двух мест: показатели, «You», видео, и
    раскрываемые на месте `Sets & times`;
  - *Lanes* — план дорожек (участнику — опубликованный, тренеру — редактор);
- **десктоп** — без переключателя, две колонки (слева Next, справа лента);
- **формы** — шторки снизу на телефоне и модалы по центру на десктопе, никогда не в списке;
- **метки внимания** — красная (нет ответа), золотая (черновик плана / тебя перенесли),
  акцентная (новые видео) — на сегменте, на табе группы, в шапке и в липкой панели.

## 2. Разведка кода (01.10.2026) — на что опираемся

Все файлы из таблицы хендоффа есть, кроме нового `use-training-marks.ts`.

| Кусок | Где | Что важно |
|---|---|---|
| Таб | `hub-groups-project/components/group-trainings-tab.tsx` (189; удалён в Т2 → `components/trainings/`) | переключатель `Sessions \| Lanes` в `?view=` (`readViewParam`), `NextSessionCard` внутри, ссылка `🔒 Open training log →` |
| Лента тренировок | `group-sessions.tsx` (421) + `use-training-sessions.ts` | «Trainings & videos» (Е3): формы ИНЛАЙН, `FIRST_PAGE = 6`, `TrainingMediaStrip`, удаление через `ConfirmDialog` |
| Журнал с временами | отдельный экран `/groups/{slug}/results?tab=trainings` ← `GET /api/hub-groups/{slug}/trainings` (`TrainingSourceDto`, форма `ResultWrap`) | строки `Sys_TrainingResults`: `SetNo`, `OrderNo`, `Distance`, `TimeMillisecond`, `Intensity` (`v1`–`v5`), `ExpectedTimeMs`, инвентарь |
| Ответы | `group-rsvp.tsx`, `use-training-rsvp`, `GET/PUT /api/hub-groups/{id}/rsvp/{session}` | ключ занятия `yyyy-MM-dd-HHmm`; окно `DaysAhead = 28`, тренеру ещё `ManagerDaysBack = 7` назад; `answer: null` УДАЛЯЕТ ответ — цикл `— → ✓ → ? → ✕ → —` сервер уже держит |
| Ближайшее занятие | `HubGroupPublicRepository` → `HubGroupDetailsDto.NextTraining` | считает сервер в поясе Израиля; ⚠ это ответ ПУБЛИЧНОЙ кэшируемой страницы группы (К4) |
| Перерывы | `use-group-breaks.ts`, `GET/PUT …/breaks`, `person-status.tsx` | `kind: break \| not_training` (Р74) |
| Дорожки | `group-pool.tsx` (`LanePool`, `RsvpSegment`, `RsvpNotes`, `MyBreakControl`), `group-lanes.tsx`, `group-lane-editor.tsx` (469, перетаскивание уже есть), `group-lane-board.tsx`, `lane-plans-api.ts` | `LanePlan.my_swimmers` — `me` / `family`, прав не даёт |
| Лимиты медиа | `TrainingSessionRules` | `MaxMediaPerSession = 50`, `MaxMediaPerMember = 10` (это «N of 10 yours») |
| Приватность | `TrainingResultsPrivacyTests` | новый файл, читающий `Sys_Training*`, — строка в `Allowed` с причиной |
| Общие компоненты | `ConfirmDialog` (портал + тема), `docs/ui-components.md` | готовой ШТОРКИ снизу в реестре нет — её заводим один раз (Т3) |

## 3. Данные — что добавить на сервере

Всё личное (только группа), мимо кэша, `no-store` — как уже сделано у `/sessions` и `/rsvp`.

1. **`TrainingSession.stats: {km, swimmers} | null`** — из `Sys_TrainingResults` сессии;
   `null`, если времён нет. Формула km — Р2; «Avg effort» убран (Р3).
2. **`TrainingSession.key_set_no: int | null`** + **`TrainingSession.my_key: {set_no, distance,
   style, time} | null`** — быстрейший повтор «моих» пловцов (тот же круг, что
   `LanePlan.my_swimmers`: сам + семья) в сете, помеченном тренером ★ (Р4). Пометка —
   колонка `Sys_TrainingSessions.KeySetNo` (**миграция**) и
   `PUT /api/hub-groups/{id}/sessions/{sid}/key-set` `{set_no: int|null}` (только управляющий;
   сет должен существовать в сессии).
3. **`GET /api/hub-groups/{id}/sessions/{sid}/sets`** (новая, ленивая) — для `Sets & times`:
   сеты → строки `set.order · пловец · время (UI_SwimTime) · V · уровень`, флаг «моя строка»,
   флаг ★ у ключевого сета.
   Права и проверка группы — как у `List`.
4. **`next_trainings: NextTraining[]` (3)** рядом с `next_training`. ⚠ `next_training` живёт в
   ответе публичной страницы группы под кэшем К4 — новое поле класть туда же и проверить по
   `docs/pre-push-rules.md` строку «кэш → `/Admin/Cache`» (время инвалидации у «ближайшего»
   уже решено для одного занятия — для трёх правило то же). Ответы на 2-е и 3-е занятие —
   существующий `GET …/rsvp/{session}` по ключу, новых ручек не надо.
5. **Метки** (`marks-spec.md`):
   - `TrainingSessionList.new_media_count` + `TrainingSessionMedia.is_new` — медиа ДРУГИХ,
     добавленное после `trainings_seen_at` зрителя;
   - `PUT /api/hub-groups/{id}/sessions/seen` — `trainings_seen_at = now`; хранение — своя
     Sys_-таблица на (группа, юзер) (Р5), **миграция**;
   - `LanePlan.my_swimmers[].moved_from_lane` — тренер перенёс моего пловца с тех пор, как я
     открывал вид Lanes. Сделано иначе, чем в хендоффе (01.10.2026): «увидел» — отдельный
     `PUT /lane-plans/seen`, а НЕ побочный эффект `GET /lane-plans/{date}` — план грузится и ради
     метки на табе группы, такой GET гасил бы метку раньше, чем человек её увидел. Перенос
     пишется на строку плана (`MovedFromLaneNo`, `MovedAt`) при сохранении ОПУБЛИКОВАННОГО плана,
     отметка зрителя — та же строка (группа, юзер), что у ленты (`LanesSeenAt`).
   - Отдельной ручки `/me/marks` нет (решение хендоффа): красные метки клиент выводит из
     `TrainingRsvp`, золотые — из плана, акцентная — из списка тренировок.

### Контракт Т1а — готов (01.10.2026)

- `GET /api/hub-groups/{id}/sessions` → у каждой тренировки добавлены
  `stats: {meters, swimmers} | null` (клиент пишет `2.4 km`), `key_set_no: int | null`,
  `my_key: {set_no, order_no, swimmer_id, kind: me|family, name, name_en, distance, style, time} | null`.
  `PUT …/{sid}` (правка) отдаёт те же поля.
- `GET /api/hub-groups/{id}/sessions/{sid}/sets` → `{session_id, key_set_no, can_manage,
  sets: [{set_no, is_key, rows: [{order_no, swimmer_id, name, name_en, distance, style,
  time | null, intensity | null, level: {id, rank, name, color} | null, mine}]}]}`; чужая / нет — 404.
- `PUT /api/hub-groups/{id}/sessions/{sid}/key-set` `{set_no: int | null}` — только управляющий
  (участнику 403); сета нет в тренировке (и у тренировки без времён) — 400; ответ — тренировка
  целиком, как у правки.
- Код: счёт — `Swimm.Application/Mapping/TrainingFeed.cs` (чистые функции), повторы —
  `IHubGroupTrainingRepository.GetRepeatsAsync` (И16), «мои пловцы» — общий
  `Infrastructure/Services/MySwimmersQuery.cs` (на нём же «Your lane» плана), миграция
  `AddTrainingKeySet` (применена к локальной БД). Тесты — `TrainingFeedTests.cs`.

### Контракт Т1б — готов (01.10.2026)

- Страница группы (`GET /api/hub-groups/{slug}`, кэш К4 с TTL страницы): рядом с `next_training`
  — `next_trainings: NextTraining[]` (до 3, первое = `next_training`; `id` — ключ ответов
  `GET/PUT …/rsvp/{id}`). Считает `GroupTrainingSchedule.NextOccurrences`; дубль слота — одно занятие.
- `GET /api/hub-groups/{id}/sessions` → `new_media_count` в списке и `is_new` у каждого медиа:
  добавлено НЕ зрителем после его `PUT /api/hub-groups/{id}/sessions/seen` (204), но не старше
  14 дней (не открывал ни разу — тоже окно 14 дней). Отметка своя у каждого, тренера тоже.
- `GET /api/hub-groups/{id}/lane-plans/{date}` → `my_swimmers[].moved_from_lane: int | null`.
  Гасит `PUT /api/hub-groups/{id}/lane-plans/seen` (204), сам GET — нет. Перенос — только в
  опубликованном плане: черновик не считается, из Unassigned на дорожку — «поставили», не
  «перенесли», вернули на исходную дорожку — переноса нет, с дорожки в Unassigned — перенос.
- Хранение: `Sys_HubGroupUserSeen (HubGroupId, UserId, TrainingsSeenAt, LanesSeenAt)` и колонки
  `Sys_LanePlanSwimmers.MovedFromLaneNo / MovedAt` — миграция `AddTrainingMarks` (применена к
  локальной БД; в прод-наполнение не едет — `server/db/seed-tables.txt`). Код:
  `Infrastructure/Services/HubGroupSeenStore.cs`; тесты — `TrainingFeedTests`, `LanePlanTests`
  (`MovedFromLane_*`), `GroupTrainingScheduleTests` (`NextOccurrences_*`).

## 3½. Модульные секции — правило каркаса (решение Влада, 01.10.2026)

Каждый блок вида — **отдельная секция**, которую можно заменить или переделать, не трогая
соседей и сам вид. Т2 закладывает это в каркас, Т3–Т9 обязаны его держать.

- **Вид = список секций, а не вёрстка.** `next-view.tsx`, `trainings-view.tsx`,
  `lanes-view.tsx` только раскладывают секции (телефон — столбиком, десктоп — по колонкам) и
  решают, какие показать по роли. Логики блоков в виде нет.
- **Секция = один файл** в `components/trainings/sections/` (`next-answers-section.tsx`,
  `your-lane-section.tsx`, `mini-pool-section.tsx`, `roll-call-section.tsx`,
  `training-feed-section.tsx`, `lane-editor-section.tsx`…). Наружу — один компонент с узкими
  пропсами: данные, которые он рисует, и колбэки действий. Секция не лезет в соседние, не
  знает, в каком виде и в какой колонке стоит.
- **Данные — один общий контекст таба** (`TrainingsTabContext`: группа, роль, `manages`,
  загруженные `sessions` / `rsvp` / план / метки и их `reload`). Секция берёт из него, а не
  грузит сама; новая ручка = новое поле контекста, а не `fetch` внутри секции. Так секцию
  можно вынуть и поставить другую, не теряя загрузку.
- **Обёртка секции общая** (`TrainingsSection`: заголовок, место под метку, состояния
  loading / error / empty) — внешний вид рамки меняется в одном месте.
- **Формы — не в секциях:** секция только вызывает `openSheet('add-video', {...})`, сами шторки
  / модалы живут в одном реестре (Т3). Переделка формы не трогает секцию, и наоборот.
- **Порядок и состав секций — данные**, массив в виде (`NEXT_SECTIONS = [...]` с условием роли).
  Убрать, переставить или подменить блок — правка одной строки.
- В задании Сонету на каждый этап — явный список секций этапа и запрет править чужие.

### Каркас Т2 — готов (01.10.2026)

Всё в `client/src/projects/hub-groups-project/components/trainings/`:

| Файл | Что | Кто дальше трогает |
|---|---|---|
| `trainings-tab.tsx` | корень таба: провайдер + раскладка (телефон — сегменты и секции вида; десктоп ≥960 — сетка `minmax(0,1.1fr) minmax(0,1fr)`, gap 14: `NEXT_SECTIONS` \| `TRAININGS_SECTIONS`; `view=lanes` на десктопе — во всю ширину с «← Back to Next») | Т10 |
| `trainings-context.tsx` | `TrainingsTabProvider` / `useTrainingsTab()`: `group`, `manages`, `isDesktop`, `view`/`setView` (`?view=next\|trainings\|lanes`, старое `sessions` → `next`), `rsvp`, `sessions` (`useTrainingSessions` + `setKeySet`, `loadSets`, `markSeen`), `breaks`, `marks`, `openSheet`/`closeSheet`/`sheet` | Т7 поднимет сюда план дорожек; новые данные — сюда |
| `trainings-sections.tsx` | реестр: `NEXT_SECTIONS`, `TRAININGS_SECTIONS`, `LANES_SECTIONS` (`{id, when?, Component}`), `SectionList` | каждый этап — свои строки |
| `trainings-section.tsx` | рамка `TrainingsSection` (заголовок, `sub`, `mark`, `actions`, `status: ready\|loading\|error\|empty`, `bare`, `data-section`) + `MarkDot` | — |
| `trainings-segments.tsx` | `Next · Trainings · Lanes`, 40px, участнику `--deep-seg-*`, тренеру `--deep-ow`; точка метки | Т8 (метки) |
| `trainings-sheets.tsx` | реестр форм `SHEETS` (пуст), типы `TrainingsSheet` (`new-training`, `edit-training`, `delete-training`, `add-video`, `break`, `person`, `lightbox`), `TrainingsSheetHost`, `hasSheet()` | Т3 |
| `use-training-marks.ts` | контракт меток (`TrainingMark {tone, count?, label}` на `next`/`trainings`/`lanes`), сейчас всё null | Т8 |
| `sections/next-training-section.tsx` | прежний `NextSessionCard` без редизайна + пустое состояние «нет расписания» | Т5, Т6 |
| `sections/training-feed-section.tsx` | прежний `GroupSessions` на данных контекста (заголовок теперь «Trainings»; ссылки на Training log нет — Р1) | Т4 |
| `sections/lane-plan-section.tsx` | прежний `GroupLanes` (`bare`) | Т7 |
| `sections/lanes-entry-section.tsx` | только десктоп: «✎ Edit lanes» / «Open lanes ›» → `setView('lanes')`; уйдёт, когда Т5/Т6 поставят бассейн | Т5/Т6 |

Плюс: `client/src/hooks/useMediaQuery.ts` (`useMediaQuery`, `DEEP_DESKTOP_QUERY`) — телефонный и
десктопный виды — разные деревья, скрывать CSS-ом нельзя (лишнее дерево сходило бы за данными);
типы Т1а/Т1б — в `hub-groups-project/types.ts`; `lanePlansApi.markSeen`. Проверено на стенде
01.10.2026: тренер 1280 (две колонки, COACH TOOLS) и 375 (фиолетовый сегмент), Lanes на десктопе
во всю ширину, участник 375 в светлой теме, гость — замок без монтирования секций; `tsc` чистый.

## 4. Этапы

Порядок — сверху вниз; Т2 (каркас) обязателен до любого клиентского этапа, Т1а/Т1б — до
этапов, которые читают новые поля. У каждого этапа — проверка глазами на стенде (§7).

| Этап | Что | Секции прототипа | Исполнитель |
|---|---|---|---|
| **Т0** ✅ 01.10 | Ответы Влада §6, правка плана под них | — | Opus |
| **Т1а** ✅ 01.10 | Сервер ленты: `stats`, `key_set_no` + `my_key`, `PUT …/key-set`, `GET …/sets`; миграция `KeySetNo`; тесты сервиса и приватности | — | Opus |
| **Т1б** ✅ 01.10 | Сервер Next и меток: `next_trainings`, `trainings_seen_at` + `PUT …/seen`, `new_media_count` / `is_new`, `moved_from_lane`; две миграции; тесты | — | Opus |
| **Т2** ✅ 01.10 | Каркас таба по §3½ (виды = списки секций, `TrainingsTabContext`, обёртка `TrainingsSection`, реестр форм-заглушка): сегменты `Next · Trainings · Lanes` (`?view=next\|trainings\|lanes`, старое `sessions` → `next`), десктоп-сетка `1.1fr / 1fr` без переключателя, `COACH TOOLS` у тренера, перенос СУЩЕСТВУЮЩИХ блоков в виды без редизайна; ссылку на Training log убрать (Р1); клиентские типы под §3 | 2a, 3a, 3b (раскладка) | Opus |
| **Т3** ✅ 01.10 | Общая оболочка «шторка снизу / модал 480 по центру» (портал + класс темы, `--t-scrim`, `--t-surface-strong`, край `--deep-card-border` + `--deep-card-shadow` в светлой) + формы New training, Add video (тип по ссылке, превью YouTube, «N of 10 yours»), Delete (фокус на Cancel), Break; лайтбокс (всегда `.theme-deep`, свайп, `1 / N`); строка в `docs/ui-components.md` | 1d, 5a, 6b (модалы) | Sonnet |
| **Т4** ✅ 01.10 | Вид Trainings: общая лента (одна вместо «Trainings & videos» + ссылки на журнал), фильтры `All · ⏱ Times · ▶ Videos`, карточка (колонка даты, показатели, «You», полоса 86×60), `Sets & times` на месте с `All / Me` и ★ ключевого сета у тренера (Р4), `Older trainings ↓`, права кнопок (Edit, Delete только без времён, ✕ на плитках) | 2a, 3a/3b (лента), 5a #4 | Sonnet |
| **Т5** ✅ 01.10 | Вид Next — участник: ответы на 3 занятия (40px ✓ ? ✕, повторное нажатие снимает), «Your lane», мини-бассейн (тап → Lanes), быстрые заметки, строка «Taking a break?» → шторка, итог `⏸ On break until … · I'm back` | 2a, 3a | Sonnet |
| **Т6** ✅ 01.10 | Вид Next — тренер: навигация по датам ‹ ›, полоса, мини-бассейн с «Draft · N unassigned → Lanes ›», фильтр-чипы с числами, строка переклички (кнопка статуса ≥100×40 по циклу + ⋮ 44px / долгое нажатие → шторка человека), тост `… · Undo` 5 с, нижняя панель тренера; десктоп — перекличка в две колонки | 2a, 1d #3, 3b | Sonnet |
| **Т7** | Вид Lanes: участнику — опубликованный план, своя дорожка первой и крупно, золотая заметка о переносе; тренеру — существующий редактор, доведённый по Р6 п.1–5 (без режима правки, тап → `Move <name> to …`, ⠿ + ⋮, липкая панель Save/Publish, тулбар, свёрнутый Not today, колонки ≤ 6). Опора — `group-lane-editor.tsx` / `group-lane-board.tsx`, не писать с нуля | 1e, 2a, 6b | Sonnet |
| **Т8** | Метки: `use-training-marks.ts` по `marks-spec.md` (приоритет красная › золотая › акцентная, зелёных нет), места: сегменты, таб группы «Trainings», полоса в шапке, баннер «Are you coming?», чип липкой панели; меню статуса в Following с описаниями; `PUT …/seen` при открытии Trainings | 6a | Sonnet |
| **Т9** | Пустые состояния (гость 🔒 + `Join group`, нет расписания — Lanes скрыт, нет тренировок, тренировка без видео) + `club-media.tsx:115` `--deep-scrim` → `--t-scrim` | 4a | Sonnet |
| **Т10** | Финальная сверка с прототипом (обе темы, 375 и 1280), доки (`docs/ui-components.md`, этот план, `docs/plans/README.md`, ROADMAP), полный прогон тестов | все | Opus |

## 5. Кто делает

**Смешанно: сервер, контракт и каркас — я (Opus); клиентские виды и формы — Сонет через
`/delegate`, я готовлю задание и проверяю.**

Почему так:
- Т0–Т2 решают то, что потом трудно переделать: формулы показателей, две миграции, приватность
  `Sys_Training*` (тест-сторож), кэш К4 у страницы группы, форма DTO, раскладка вида. Ошибка
  здесь размножится по всем клиентским этапам — это делаю сам.
- Т3–Т9 после каркаса — хорошо специфицируемая вёрстка по high-fidelity прототипу на готовых
  токенах: у каждого этапа есть точные размеры, тексты и состояния. Это сильная сторона
  Сонета, и этапы почти не пересекаются по файлам.
- Т10 — сверка глазами и доки: сам.

**Как делегирую каждый этап Сонету** (скилл `/delegate`):
1. Пишу самодостаточное задание `docs/tasks/trainings-tab-t<N>-sonnet.md`: что строим, файлы,
   выдержка из хендоффа (размеры, тексты, состояния, токены), контракт данных из Т1/Т2, что НЕ
   трогать, критерии готовности, команды проверки.
2. Запускаю агента `sonnet-implementer` на это задание.
3. **Проверяю сам, до того как показать тебе:**
   - дифф целиком: слои, переиспользование (`docs/ui-components.md`), `<bdi>`/`dir="auto"` у
     имён, ни одного хекса мимо ролей, никаких `scrollIntoView`, строки UI на английском;
   - `tsc`, тесты, при серверной правке — `dotnet test`;
   - вживую на стенде (§7) персонажами тренера, участника и гостя, в светлой и тёмной теме, на
     375 и 1280, рядом с прототипом — скриншоты в отчёт;
   - нашёл расхождение — возвращаю Сонету с конкретным списком или правлю сам, если мелочь.
4. Отчитываюсь коротко: что сделано, что проверено, скриншоты, что осталось. Коммит — по твоей
   команде.

## 6. Решения — решено Владом 01.10.2026

- **Р1. Экран «Training log» (`/groups/{slug}/results?tab=trainings`)** — из таба ссылку
  убрать (Т2), сам экран оставить по прямому адресу (там полная таблица с фильтрами).
  Удалять ли — отдельным решением после Т4.
- **Р2. «Volume (km)»** — объём ОДНОГО пловца: сумма дистанций уникальных повторов
  (`SetNo`+`OrderNo`) сессии. Только записанное временами — разминка без времён не попадает.
- **Р3. «Avg effort» — убрать.** В карточке два показателя: km и swimmers.
- **Р4. «You» — сет помечает тренер.** Тренер ставит ★ на ОДИН сет тренировки (в `Sets &
  times` или в Edit тренировки) → колонка `TrainingSession.KeySetNo int?` (миграция Т1а).
  «You» = быстрейший повтор «моих» пловцов (сам, затем семья; несколько — строка «me») в этом
  сете, формат «100 free · 1:12.40». Нет ★ или нет моих повторов в сете — строки «You» нет
  (никакой эвристики «лучшего»). Поля в источнике Дельфина нет — метка только на сайте.
- **Р5. «Видел ленту» — своя строка на (группа, юзер)**, Sys_-таблица без grant `swimm_ro`;
  метка «новые видео» есть и у тренера без членства-аккаунта.
- **Р6. Редактор дорожек — доводим существующий** (`group-lane-editor.tsx` /
  `group-lane-board.tsx` / `group-lanes.tsx`), не пишем новый. Из прототипа берём:
  1. перенос пальцем: тап по плашке → строка `Move <name> to [1][2][3][Unassigned]`; десктоп —
     ручка ⠿ и ⋮ «Move to», перетаскивание внутри дорожки меняет порядок (заменяет ↑);
  2. отдельного режима правки нет — тренер сразу в редакторе; липкая нижняя панель:
     «Unsaved changes · Save», после записи «Saved · draft · Publish». **Явный Save остаётся**
     (автосохранения нет): опубликованный план не дёргается у участников на каждом переносе;
  3. тулбар: дорожки −/+, Distribute, Auto lanes, Copy from …, Delete; заметка для всех — под
     тулбаром;
  4. «Not today» остаётся, но свёрнутой строкой (как «On break» в перекличке);
  5. десктоп: дорожки колонками при ≤ 6, больше — сетка, без горизонтальной прокрутки;
  6. Unassigned из ответов «иду / не уверен» — НЕ в Т7, отдельным решением позже (ответ
     привязан к занятию «дата + время», план — к дате).
  Сохраняются все нынешние функции: Auto lanes, Distribute с подтверждением, заметка,
  «Same workout as lane N−1», серые ушедшие, точка цвета уровня у пловца.

## 7. Как проверять

- Стенд `stack-5183-api5082-personas` (API :5082 + Vite :5183) — не трогает API Влада на :5078.
  Перед стартом — `dotnet build server/Swimm.API` (конфиг запускает с `--no-build`). Первая
  навигация после старта часто «denied or failed» — повторить.
- Персонажи: `utest-coach` (тренер, владелец обеих тестовых групп), `utest-member` (участник),
  `utest-guest` (гость) — `http://localhost:5183/api/dev/persona?as=<ник>`; группа
  `/groups/utest-open?tab=trainings`. Аккаунт Влада (`default`) — только смотреть.
- После проверки — удалить тестовые строки, остановить стенд (`preview_stop`), иначе
  Debug-сборка заблокирована.

## 8. Как вернуться к работе (новый чат)

1. Прочитать этот план целиком, затем `!design_handoff/design_handoff_trainings_tab/README.md`
   и `marks-spec.md`; прототип открыть в браузере.
2. Статус этапов — только здесь (§4) и в `docs/plans/README.md`, не в заданиях `docs/tasks/`.
3. Следующий этап — первый в §4 без пометки «сделано».
