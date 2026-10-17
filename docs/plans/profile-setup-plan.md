# План — «Set up profile»: три первых шага и опрос «кто ты» (хендофф 1a)

**Статус:** ◐ S0–S6 сделаны 16.10.2026 (миграция `AddUserProfileSetup` применена локально, на прод —
нет; ручки `api/me/profile-setup` + тесты; хук `client/src/hooks/useProfileSetup.ts`; пункт и точка в
`app-topbar.tsx` и `home-header.tsx`; страница `client/src/projects/setup-profile-project/`). Осталось:
выкатка миграций `AddUserProfileSetup` + `AddUserProfileSetupSkips` на прод. 17.10 — отказ от шага (раздел ниже). Поставлено 16.10.2026, решения Влада — в тот же день. **Оценка:** ~2 дня.
**Дизайн:** `!design_handoff/set-up-profile/` (`README.md` + `New User Setup.dc.html`, рамка `id="1a"`;
1b и 1c НЕ делаем). Хендофф лежит вне git, поэтому размеры и токены перенесены сюда — разделы
«Пункт меню» и «Страница».

## Зачем

Новый пользователь входит и видит сайт, который о нём ничего не знает. Всё, что делает сайт «своим»
(Me, клуб, группа), уже есть, но разбросано по разным страницам, и новичок их не находит. Один пункт
меню с прогрессом ведёт по трём шагам: добавь себя, свой клуб, свою группу.

**Не цель:** проверка личности, права, «официальная» привязка. Всё, что ставится здесь, — та же
недоверенная пометка, что и сейчас (правило «primary favorite недоверенный», решение 10.07.2026;
клубом не управляет никто — решение 09.09.2026).

## Решения (Влад, 16.10.2026)

| # | Вопрос | Решение |
|---|---|---|
| Р1 | Что делает вопрос «Who are you?» | **Только опрос, ни на что не влияет.** Шаги и счётчик от ответа не зависят. Ответ сохраняется в БД — может пригодиться в будущем |
| Р1а | «Just watching» вместе с другими | **Исключает остальные:** выбрал его — остальные сбрасываются; выбрал другую роль — снимается он (обратное направление — следствие, принято 16.10.2026). Сервер такую комбинацию отклоняет (400 `watcher_exclusive`, `ProfileSetupRules.IsValidSurvey`) |
| Р2 | Где хранить ответ и «закрыть» | **Колонки в `AppUser`** (по колонке на роль + время ответа + время Dismiss), одна миграция. Не `localStorage` |
| Р3 | Отправленная заявка в группу (`pending`) | **Засчитывается**: со стороны пользователя шаг сделан |
| Р4 | Закрываемость («Don't ask again» из хендоффа) | Ссылка **Dismiss** внизу страницы; в меню отдельной кнопки нет |
| Р5 | Проверять ли «It's me» | Нет: пометка недоверенная, прав не даёт |
| Р6 | Пловец с несколькими клубами | Избранных клубов сколько угодно; шаг засчитан с первого |

Следствие Р1: шагов по ролям из хендоффа (My child, Groups I coach, Follow) **нет** — набор шагов
фиксированный, ровно три из заголовка «Add yourself, your club and group». Если позже роли начнут
влиять, шаги по ролям из хендоффа — готовая заготовка (README хендоффа, таблица «Steps by role»).

Принято по умолчанию (поправимо без переделки):
- **Опрос в счётчик не входит:** `total = 3`. Иначе неответивший видел бы «недоделанный» профиль
  из-за вопроса, который ни на что не влияет.
- **При 3/3 пункт не показывается — сознательно** (решение Влада 17.10.2026). Следствие: кто сделал
  все три шага до этой страницы (Me, клуб, группа уже были), до опроса через меню не дойдёт. Это
  приняли: таких единицы (прод с 15.10), опрос ни на что не влияет, страница открывается напрямую
  по `/setup-profile`. Обсуждали и отвергли «держать пункт, пока нет ответа на опрос» — опрос,
  который ни на что не влияет, не должен держать пункт в меню.
- **Отдельного «Skip» у шага нет.** Пловцу без результатов хватает Dismiss. Подсказку шага пишем без
  слова skip.

## Как сейчас (сверено с кодом 16.10.2026)

Всё, чем закрываются шаги, уже есть:

| Шаг | Чем закрывается | Ручки |
|---|---|---|
| It's me | `UserFavorite.IsPrimary` | `POST /api/me/favorites` → `POST /api/me/favorites/{id}/primary` |
| My club | избранное `TargetType = "club"` | `POST /api/me/favorites` `{targetType:"club", clubId}` |
| My group | `HubGroupUserMember`, статус `active` / `pending` (`JoinPolicy=approval`) | `POST /api/me/hub-groups/{id}/join`, `GET …/joined` |

Поиск для окон выбора тоже есть:
- пловцы — публичный `GET /api/swimmers/search?q=` (от 2 символов, `SwimmerSearchHitDto`);
- клубы — `GET /api/me/hub-groups/clubs` (весь справочник, фильтр на клиенте);
- группы — публичный `GET /api/hub-groups`.

Окно выбора — `UI_H2HPickerModal` (`components/mix/h2h/h2h-picker-modal.tsx`, портал + класс темы),
общее для H2H и `/records/compare`. С третьим пользователем его стоит поднять в
`components/picker-modal/` (переименование без изменения поведения).

⚠ `POST /api/me/favorites` на уже добавленного отвечает **409** без id. Клиент, получив 409, берёт id из
`GET /api/me/favorites` и продолжает (`/primary`) — ошибку не показывает. Общий лимит избранного
(422 `FavoritesRules.LimitErrorCode`) показывать текстом из ответа.

⚠ **Phosphor в клиенте не подключён** — классы `ph-*` из хендоффа работать не будут. Иконки чипов и
шагов рисуем встроенными SVG, как остальные иконки в `components/mix/*-icon`. У «I'm a coach» на
макете иконка и так не отрисовалась (`ph-whistle` в Phosphor нет) — нужна своя.

## Устройство

### 1. Данные — колонки в `AppUser`

| Колонка | Тип | Смысл |
|---|---|---|
| `ProfileRoleSwimmer` | `boolean not null default false` | ответ опроса «I swim» |
| `ProfileRoleParent` | `boolean not null default false` | «I'm a parent» |
| `ProfileRoleCoach` | `boolean not null default false` | «I'm a coach» |
| `ProfileRoleWatcher` | `boolean not null default false` | «Just watching» |
| `ProfileRolesAnsweredAt` | `timestamptz null` | когда последний раз менял ответ; `null` — ни разу не отвечал |
| `ProfileSetupDismissedAt` | `timestamptz null` | нажал Dismiss; `null` — пункт показывается |

- Колонка на роль, а не строка через запятую: «сколько родителей» в будущем — простой `count`.
- ⚠ `ProfileRoleCoach` — **ответ опроса, а не право.** Не путать с site-ролью `Coach` (`AppRole`) и
  бейджем `HubGroupMember.Role = "coach"`. Комментарий в сущности обязателен; ничего от этих колонок
  не зависит (Р1).
- Таблица пользователей — `Sys_`, гранты `swimm_ro` не нужны.
- Миграция `AddUserProfileSetup` через `/db-migrate`. На прод — как все миграции ([deploy.md](../deploy.md)).

### 2. Правило — один чистый класс в Application

`Swimm.Application/Mapping/ProfileSetupRules.cs` (рядом с `FavoritesRules`):

```
ProfileSetupStatus Evaluate(ProfileSetupFacts facts, bool dismissed)
```

`ProfileSetupFacts` — три флага, которые репозиторий считает `EXISTS`-запросами: `HasMe`, `HasClub`,
`InGroup` (active **или** pending, Р3).

- Шаги всегда одни и те же, в этом порядке: `me`, `club`, `group`.
- **Счётчик:** `total = 3`, `done` = число засчитанных шагов.
- **Пункт виден**, когда `!dismissed && done < 3`. Ответ на опрос здесь не участвует — см. «При 3/3
  пункт не показывается — сознательно» выше.

Юнит-тесты (`ProfileSetupRulesTests`): 0/3 → виден; 3/3 → скрыт; pending засчитан; dismissed прячет
при `done < 3`.

### 3. API — `api/me/profile-setup` (отдельный контроллер, `[Authorize]`)

| Метод | Что | Ответ |
|---|---|---|
| `GET` | ответ опроса, шаги с флагом `done` (и что выбрано: имя пловца/клуба/группы, статус заявки), `done`, `total`, `visible` | `ProfileSetupDto` |
| `PUT /roles` `{swimmer, parent, coach, watcher}` | сохранить ответ опроса (все четыре разом) + `ProfileRolesAnsweredAt = now` | 204; 400 `watcher_exclusive` — watcher вместе с другой ролью (Р1а) |
| `POST /dismiss` | `ProfileSetupDismissedAt = now` | 204 |
| `DELETE /dismiss` | вернуть пункт (на будущее, см. открытые решения) | 204 |

- Контроллер инжектит только интерфейс из Application (`IProfileSetupService`); реализация — в
  Infrastructure, `SwimmDbContext` (пишет `Sys_`).
- **Без кэша:** ответ личный и меняется от любого действия со страницы.
- В `/auth/me` **не встраиваем**: это горячая ручка с логикой «БД недоступна». Отдельный лёгкий
  запрос — только у вошедшего.
- Антифорджери — как у соседних `api/me/*` (сверить с `FavoritesController` при реализации).
- `*PgTests` на запись колонок — правило «InMemory не видит ограничения Postgres».

### 4. Пункт меню (`app-topbar.tsx`)

Хук `use-profile-setup.ts` (в `components/app-topbar/`) грузит `GET` только при `isAuthenticated` и
отдаёт `{ visible, done, total }` и `refresh()`.

- **Где:** первым пунктом, над «My media», в выпадающем меню и в бургере; только пока `visible`.
- **Блок:** padding 9px 12px, radius 8px, фон `color-mix(in srgb, var(--theme-topbar-accent) 16%,
  transparent)` (как hover пункта), вертикальный gap 6px.
  - Строка 1: «Set up profile» 13px/700 `--theme-topbar-text`; справа «{done}/{total}» 12px/700
    `--theme-topbar-accent`.
  - Строка 2: полоска 3px, дорожка `color-mix(in srgb, var(--theme-topbar-text) 14%, transparent)`,
    заливка `--theme-topbar-accent`, ширина `done/total`.
  - Строка 3: «Add yourself, your club and group», 11px, `--theme-topbar-text-muted`.
- **Точка на аватаре:** 9px, `--theme-topbar-text`, кольцо 2px `--theme-topbar-bg`, top/right −1px —
  пока `visible`.
- Ссылка ведёт на `routes.setupProfile()`.

### 5. Страница `/setup-profile`

Новый маршрут — **три зеркала** ([pre-push-rules.md](../pre-push-rules.md)): `routes.ts`
(`setupProfile: () => '/setup-profile'` + строка в шапке-контракте), `vite.config.js` (rewrite + вход
`setup-profile.html`), `Program.cs` (`"setup-profile" => "/setup-profile.html"`). Неавторизованному —
та же заглушка «войдите», что у `/my-favorites`.

Карточка: `--theme-mode-surface`, radius 12px, padding 24px, `--theme-mode-card-border` /
`--theme-mode-card-shadow`.

⚠ **Как сделано (S3, 16.10.2026):** страница на теме **deep**, как соседние личные `/my-favorites` и
`/my-media`, поэтому токены хендоффа `--theme-mode-*` переложены на роли `--t-*`: карточка `--t-card` /
`--t-border` / `--t-shadow`, текст `--t-text` / `--t-text-2` / `--t-text-3`, включённый чип — рамка
`--t-accent`, фон `--t-accent-soft`, текст `--t-accent`; кнопка шага `--t-accent` / `--t-accent-ink`.
Размеры — как ниже.

- Заголовок «Set up your profile» 20px/800; подзаголовок «Add yourself, your club and group» 13px
  `--theme-mode-text-secondary`.
- **Опрос.** «Who are you?» 14px/700 + «Pick all that apply» 12px `--theme-mode-text-muted`. Чипы
  (мультивыбор): 36px, radius 18px, 13px/600, `white-space: nowrap`, иконка 16px — I swim, I'm a parent,
  I'm a coach, Just watching.
  - выкл: рамка 1px `--theme-mode-border-input`, фон прозрачный, текст `--theme-mode-text`;
  - вкл: рамка `--theme-primary`, фон `--theme-primary-light` (тёмный режим —
    `color-mix(in srgb, var(--theme-primary) 22%, transparent)`), текст `--theme-mode-accent-on-surface`.
  - «Just watching» исключающий (Р1а): клик по нему гасит остальные чипы, клик по любому другому
    гасит его.
  - Клик по чипу сразу шлёт `PUT /roles` (без кнопки «сохранить»). **Шаги ниже от него не меняются**
    (Р1); строки «Pick a role to see the next steps» из хендоффа нет.
- **Строки шагов:** верхняя граница 1px `--theme-mode-border`, padding 12px 0; круг 36px с пунктирной
  рамкой `--theme-mode-border-input` и иконкой 18px; заголовок 14px/700; подсказка 12px secondary;
  кнопка 34px, radius 8px, `--theme-bg-active` / `--theme-text-active`. Засчитанный шаг — галочка
  вместо кнопки и что выбрано (имя пловца/клуба/группы, у заявки — чип «Pending»).
- Внизу — ссылка **Dismiss** (`POST /dismiss` → на главную).

| Шаг | Иконка | Подсказка | Кнопка | Что делает |
|---|---|---|---|---|
| It's me | золотая ★ | Find your swimmer record | Find me | окно `UI_H2HPickerModal` + `UI_H2HRivalPicker` (избранные пловцы полосой + поиск `/api/swimmers/search`) → `useFavorites.setMeBySwimmer` |
| My club | клуб | Club you or your child swim for | Choose club | то же окно, свой список `/api/me/hub-groups/clubs` с фильтром на месте → `useFavorites.toggleFavoriteClub` |
| My group | группа | Training group · coach approves | Join group | **ссылка на каталог `/groups`** — вступают на странице группы |

Отступления от исходного плана (S4, 16.10.2026):
- **Группа — ссылкой, не окном.** Публичный `/api/hub-groups` не отдаёт `id`, а `POST …/{id}/join` ждёт
  его; заводить ручку ради окна не стали — на странице группы и так есть Join с правилами доступа.
- **Окно не поднималось в `components/picker-modal/`** — взят `UI_H2HPickerModal` как есть (третий
  вызывающий). `UI_H2HRivalPicker` получил проп `placeholder` (дефолт прежний — про соперника).
- Переключатели `useFavorites` (`setMeBySwimmer`, `toggleFavoriteClub`) по уже отмеченному СНИМАЮТ
  отметку — страница зовёт их только для неотмеченного. Лимит избранного — текстом `fullHint` под шагами.
- Засчитанный шаг — ✓ Done и ссылка на выбранное; «Dismiss» прячется, когда сделаны все три.

После каждого действия — `refresh()` статуса (страница и пункт меню берут его из одного хука).
Золотая звезда Me — та же, что на `/my-favorites` (решение 24.09.2026), не новая.

## Итог «You're all set» (хендофф `!design_handoff/set-up-profile-ex1`, вариант 2a, 17.10.2026)

Когда все три шага сделаны или пропущены (`done == total`), карточка вместо чек-листа показывает итог:
- шапка: круг 44px с галочкой цвета `--t-good` (успех темы deep; в макете #1f8a5b), «You're all set»
  20/800, «SwimHub now shows your results, club and group first.», справа — плашки ответа опроса
  (на телефоне — отдельной строкой; опрос по-прежнему ни на что не влияет, только показан);
- три строки Me / Club / Group: иконка, подпись, что выбрано (у группы — чип PENDING) и ссылка —
  «My swims →» (страница пловца), «Open club →», «Open group →»; пропущенный шаг — «Not added» и
  та же кнопка шага (Find me / Choose club / Join group): сделанное делом потом сильнее отказа;
- низ: «Change any of this later in My favorites.» и кнопка «Go to my swims» → страница своего
  пловца; если «это я» пропущен — «Browse results» → `/results` (в макете у watcher «Browse swimmers»,
  но страницы-списка пловцов нет).

Отступления от макета, сознательно: строк всегда три (шаги по ролям не делаем — Р1); итог
показывается при каждом заходе на `/setup-profile`, а не один раз (страница отдельная и открывается
только по ссылке); пункт меню и точка при 3/3 пропадают — это уже было. Чек-лист вернётся сам, если
что-то сделанное снять (например, убрать «это я» в My favorites).

## Отказ от шага (решение Влада 17.10.2026)

Не каждому подходят все три шага: тренер отмечает только клуб, родитель обходится без «это я».
Поэтому у каждого шага рядом с подсказкой — кнопка отказа: **Not a swimmer** / **No club** /
**No group**. Отказ **засчитывает шаг** (счётчик растёт, при 3/3 пункт меню прячется), строка
показывает подпись отказа, справа «Skipped», рядом **Undo**. Сделанное делом сильнее отказа: вступил
в группу после «No group» — шаг сделан, а не пропущен. Все варианты ролей сознательно не продумывали.

- Колонки `AppUser.ProfileSkipMe` / `ProfileSkipClub` / `ProfileSkipGroup` (bool, default false) —
  отдельная миграция `AddUserProfileSetupSkips` (не в `AddUserProfileSetup`: та уже стояла в локальной
  базе под работающим API, откат на секунды ронял бы все запросы к пользователям). На проде нужны обе.
- Правило — `ProfileSetupRules.Evaluate(facts, skips, dismissed)`; у шага `Skipped` = отказ без дела.
- Ручки: `PUT /api/me/profile-setup/steps/{me|club|group}/skip` — отказаться, `DELETE …` — отменить
  (204; чужой ключ — 404). Клиент — `useProfileSetup.setStepSkipped`.
- Тесты: правило (отказ засчитан, дело сильнее отказа, три ключа) и PG (отказ, 3/3, отмена).
- Проверено в pane под `utest-newbie`: 0/3 → Not a swimmer + No group = 2/3 → No club = 3/3, точка
  пропала → Undo = 2/3, точка вернулась.

## Шаги

| # | Что | Готово, когда |
|---|---|---|
| S0 | миграция `AddUserProfileSetup` (6 колонок), `ProfileSetupRules` + тесты | тесты правила зелёные; `\d` таблицы показывает колонки |
| S1 | `IProfileSetupService`, репозиторий фактов, контроллер `api/me/profile-setup` + `*PgTests` | `/smoke-test-api`: PUT roles сохраняет 4 флага и время; GET даёт 0/3; Dismiss прячет |
| S2 | хук + пункт меню + точка на аватаре (меню и бургер) ✅ 16.10 | у `utest-newbie` пункт есть (0/3), после Dismiss — нет; бургер и тёмная тема проверены в pane |
| S3 | маршрут в трёх зеркалах + страница, чипы опроса ✅ 16.10 | `utest-newbie`: чипы пишут колонки, «Just watching» гасит остальные и гаснет сам, шаги не меняются; `robots.txt` + `noindex` |
| S4 | кнопки трёх шагов ✅ 16.10 | `utest-newbie`: Find me → 1/3, Choose club → 2/3, заявка в группу с одобрением → 3/3 и PENDING, точка пропала; телефон и тёмная тема deep проверены |
| S5 | проверка в pane: 9 тем × light/dark, ширина телефона ✅ 16.10 | замер контраста скриптом по всем 18 сочетаниям: заголовок ≥8.4, счётчик ≥8.4, подсказка ≥5.9 (после правки — см. ниже) |
| S6 | доки: ROADMAP 4.6 ✅, `/privacy` ✅ (пункт в «What we keep and why», en/ru/he) | `client/CLAUDE.md` не трогали: там список входов примерами, не перечнем |

**S5 — что нашлось и поправлено (16.10.2026).** В пункте меню `AppTopbar` счётчик «0/3» по хендоффу
был цветом `--theme-topbar-accent`: на подложке пункта это 2.4–2.5 в competition-blue и training* —
цифры теперь цветом текста полосы, акцент остался у заливки полоски. Подсказка `-text-muted` в тёмных
режимах давала 3.8–4.3 на 11px — взят текст полосы на 78%. То же для подсказки в шапке главной
(`--t-text-2` на подложке пункта в светлой теме — 2.74).
⚠ Не наше, но замечено: общая роль `--t-text-2` светлой темы deep (`#7b95a3`) на белой карточке —
около 3.1 (норма 4.5). Так выглядят все вторичные подписи deep-страниц (`/my-favorites`, эта
страница) — чинить роль надо целиком, отдельной задачей.

## Открытые решения (за Владом)

1. ~~**`/privacy`**~~ **Решено 16.10.2026 — да.** Пункт в «What we keep and why» (`client/src/content/help/privacy/{en,ru,he}.md`):
   ответ опроса — по желанию, ничего не меняет, виден только владельцу и администратору, снимается в любой момент.
2. ~~**Где смотреть ответы опроса.**~~ **Решено 17.10.2026 — там, где детали пользователя:** панель
   `/Admin/Users?userId=` (строка «Кто он (опрос)» + дата ответа + дата Dismiss), см.
   [admin-pages/users.md](../admin-pages/users.md). Только чтение.
3. ~~**Шапка главной.**~~ **Решено 16.10.2026 — да.** `/` рисует свой `HomeHeader` (тема deep), туда
   поставлен тот же пункт на ролях `--t-*` (меню аватара и бургер) и точка на аватаре; на телефоне
   аватара в шапке нет — точка на кнопке бургера. Данные — тот же `useProfileSetup`.
4. **Возвращение после Dismiss:** нужна ли где-то ссылка «Set up profile» (например, вверху
   `/my-favorites`), чтобы вернуть пункт? Ручка `DELETE /dismiss` заложена, UI — по решению.

## Можно ли отдать Sonnet

S2–S5 — да, по таск-документу (`/delegate`): разметка и токены перенесены сюда полностью. S0–S1 — Opus:
новые колонки пользователя и правило подсчёта, на котором держится весь UI.
