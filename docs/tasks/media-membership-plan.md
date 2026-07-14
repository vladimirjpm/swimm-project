# План: media/членство — оставшиеся шаги (2026-07-14)

Хендофф-план после реализации 2B′ (members-медиа «тренерские разборы», коммит `1251a3e`).
Контекст и принятая модель — `docs/favorites-media-phase2-design.md` (§0 — финальная модель,
§10 — что уже реализовано). Распределение: security-чувствительное — Fable, механика по
чёткой спеке — Sonnet через `/delegate`.

## Фон (что уже есть)

- 2B′ реализован: `HubGroupMedia.Visibility(public|members)` + якоря `SwimmerId?/ResultId?`,
  `GET /api/hub-groups/{slug}/media/members`, редактор «🔒 Members reviews», секция Reviews
  на groups.html. Тесты 189/189.
- Членство юзеров: `Sys_HubGroupUserMembers` (`Status: active|pending`), самозапись
  `JoinAsync` (мгновенно `active`), добавление админом по email. Права мутаций — только
  CanEdit (владелец/админ группы/site-админ); членство = только просмотр.
- **Вскрытая проблема:** self-join мгновенно `active` → любой залогиненный видит
  members-разборы публичной группы, просто нажав «Вступить». «Дверь» уже заложена:
  статус `pending` существует, `IsActiveAccountMemberAsync` проверяет `active`.
- Нюанс dev: `DevAdminBypass` даёт синтетического юзера id=0 — POST медиа под ним падает FK
  (артефакт обхода, не бага).

## Шаг 1. JoinPolicy для групп (Вариант B) — **Fable** ⏳ следующий

Гейт доступа к members-контенту (security) — делать самому, не делегировать.

- `HubGroup.JoinPolicy` string (`open` | `approval`), дефолт `open`, миграция +
  check-constraint (образец: `CK_HubGroups_OfficialRequiresClub`).
- `HubGroupUserService.JoinAsync`: при `approval` вставлять `Status=pending`
  (сейчас всегда `active`; вставка — `InsertUserMemberAsync`). Добавление по email
  админом (`AddUserMemberAsync`) — как было, сразу `active`.
- Approve: `POST /api/me/hub-groups/{id}/user-members/{userId}/approve` (CanEdit,
  antiforgery) — `pending → active`. Отклонить = существующий
  `DELETE /api/me/hub-groups/{id}/user-members/{userId}`.
- Самообслуживание группы (MyGroupsPanel → редактор): селектор политики; в списке
  user-members у `pending` — кнопка Approve (статус уже отображается в
  `HubGroupUserMember` DTO). Страница группы: для pending показывать «Заявка отправлена»
  вместо «Вы участник» / «Выйти» (клиентский `useHubGroupMembership.joined[].status`
  уже отдаёт статус).
- UI-подсказка (не форс): официальной группе при наличии members-медиа рекомендовать approval.
- Тесты (образец `HubGroupUserServiceTests` / `HubGroupMediaServiceTests`):
  open→active; approval→pending; pending НЕ проходит `IsActiveAccountMemberAsync`
  (не видит тренировки/разборы); approve→active→видит; approve чужим юзером (не CanEdit) → 403;
  повторный join при pending — no-op/понятная ошибка.

## Шаг 2. Ярлык «родитель пловца» — **Sonnet** (после шага 1)

Чисто отображение, прав не меняет.

- `HubGroupUserMember.SwimmerId?` (FK Swimmers, `OnDelete SetNull` — не терять родителя при
  удалении пловца) + `Note` string(100)? — подпись («родитель», произвольный текст). Миграция.
- Указывается админом при добавлении по email и/или при approve; редактируется в списке
  user-members.
- Вывод в списке участников панели: «Displayname — родитель: <имя пловца>».
- Тесты: валидация существования пловца; SetNull при удалении пловца.

## Шаг 3. 2A — личное owner-only медиа — **Sonnet** (независим, можно параллельно шагу 1)

Полная спека — `docs/favorites-media-phase2-design.md` §3–§7. Кратко:
- `MediaController` `api/me/media` (GET/POST/PUT/DELETE), образец — `FavoritesController`
  (Authorize + AutoValidateAntiforgeryToken + owner-scope + `Sys_*` только через
  `SwimmDbContext`, RO-грант НЕ добавлять).
- `Sys_UserMedia`/entity `UserMedia` уже существуют (Phase 1) — эндпоинты не открыты.
- Visibility ВСЕГДА `private` (public в API → 400; публичного слоя нет по решению §0).
- Валидация: level↔id-поля; sourceType allowlist (youtube/vimeo/other); URL https;
  канонический id извлекается (см. клиентский `HelperMedia`).
- Клиент: «мои ссылки» владельцу в карточке пловца/заплыва; рендер существующим лайтбоксом
  `UI_SwimmerGallery` (embed из канонического id).
- Тесты §7: IDOR, валидации, дефолт private, каскад по SwimmerId.

## Шаг 4. Выбор заплыва в редакторе разборов — **Sonnet** (мелкий)

Сервер уже принимает `result_id` (валидация+денормализация SwimmerId готовы, эстафеты
отбиты). Только UI: в `MediaEditor` при выбранном пловце-якоре подгрузить его последние
N заплывов (`GET /api/results?swimmerIds=...` уже умеет; или paged c фильтром) → select
«привязать к заплыву» → слать `result_id`.

## Шаг 5. Усиление лайтбокса — **Sonnet** (мелкий, отдельный коммит)

`client/src/projects/components/mix/swimmer-gallery/swimmer-gallery.tsx`:
- youtube → `https://www.youtube-nocookie.com/embed/{id}`;
- iframe: атрибут `sandbox` (минимум для плеера) + суженный `allow`.
- Общий компонент! Прогнать визуально ВСЕ места: галерея группы, медиа тренировок,
  members-разборы, галерея заплыва в таблице результатов.

## Порядок

1 → 2 (ярлык вешается на гейтящееся членство). 3 и 5 — независимы, можно параллельно
шагу 1 (другой контур файлов). 4 — в любой момент после 2B′.

Старт по договорённости: Fable берёт шаг 1; параллельно `/delegate` Сонету шаг 3 (2A).
Шаги 2/4/5 — следующей волной.

## Footguns (для любого исполнителя)

- Build-lock: VS держит :5078 — сборка `-c Release`, API из `bin/Release` на :5079,
  клиент — конфигурация `client-5079` из `.claude/launch.json`.
- Два DbContext: EF-команды всегда `--context SwimmDbContext`.
- `Sys_*`-таблицы НЕ добавлять в RO-грант (`server/db/setup-roles.sql`).
- Кэш деталей группы 5 мин — мутации медиа зовут `InvalidateAllAsync` (идиома).
- Правило: primary favorite / членство — НЕ identity, прав не даёт
  (memory `rule-primary-favorite-untrusted`).
