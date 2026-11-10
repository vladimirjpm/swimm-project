# Заявка тренера на группу («Request access») — план

> Статус: **К1–К5 сделаны и проверены локально 10.11.2026** (ROADMAP 8.17), не закоммичено,
> не на проде. Решения Влада — §2, приняты 10.11.2026. Как сделано — §7.

## 0. Как вернуться к работе (новый чат — начни отсюда)

1. Прочитай §1–§4 этого файла. Код смотри по ссылкам §3 — там всё, на что опираться.
2. Порядок работ — §5. Миграция нужна (§4), значит перед деплоем — `--migrate` на проде
   (`docs/deploy.md`, «Выкатить код», шаг 2; запускает Влад или агент по его просьбе).
3. Перед push — `docs/pre-push-rules.md` по диффу.

## 1. Зачем

В My profile (`/setup-profile`) у роли тренера есть кнопка **«Are you a coach? → Request access»**
(`client/src/projects/setup-profile-project/setup-profile.tsx:167`, действие — `:737`). Сейчас она
только ставит ответ анкеты `AppUser.ProfileRoleCoach` (`ProfileSetupController.cs:48-62`, «Прав не
даёт») и уводит на `/groups`. **Никакой заявки никто не получает** — надпись врёт.

Сегодня тренера делает админом только владелец (или админ сайта) руками: `/groups` → My groups →
карточка группы → **Group admins** → email → «+ Admin» (`my-groups/admins-editor.tsx`,
`POST /api/me/hub-groups/{id}/admins`, право `CanManageAdmins = isAdmin || isOwner`,
`HubGroupPermissionService.cs:41`). Повод — Аня Гостомельская, тренер Дельфин-мастерс (#17).

## 2. Решения Влада (10.11.2026)

| # | Вопрос | Решение |
|---|---|---|
| Т1 | Что даёт одобренная заявка | **Админ группы + привязка к пловцу**: строка в `Sys_HubGroupAdmins`, участие active, и метка тренера `Sys_HubGroupUserMembers.SwimmerId` = его пловец «Me» (если он в видимом составе группы) |
| Т2 | Где владелец одобряет | **Таб Admin страницы группы** — карточка «Coach requests» рядом с инбоксом публикаций, счётчик на табе |
| Т3 | Как сообщить владельцу | **Счётчик + Web Push** — пуш владельцу (и админам сайта? — нет, владельцу группы) через уже работающие подписки напоминаний; кто пуш не включал — видит только счётчик |

Открытые мелочи (решить по ходу, по умолчанию — как написано):
- **Кто одобряет:** только тот, у кого `CanManageAdmins` (владелец, админ сайта) — админ группы
  админов не назначает, значит и заявку на админство не одобряет. Карточку видят только они.
- **Отклонить:** заявка снимается, человек остаётся обычным участником (членство не трогаем).
- **Повторная заявка** после отказа — можно (новая строка/флаг заново).
- **Группа с JoinPolicy=approval:** одобрение заявки тренера заодно активирует его членство.

## 3. На что опираться в коде

- Членство аккаунта: `HubGroupUserMember` (`server/Swimm.Domain/Entities/HubGroupUserMember.cs`),
  статусы active/pending; вступление — `HubGroupUserService.JoinAsync` (`:265-293`), одобрение
  pending — `ApproveUserMemberAsync` (`MyHubGroupsController.cs:397-407`).
- Админы: `AddAdminAsync` (`HubGroupUserService.cs:149-187`), таблица `Sys_HubGroupAdmins`.
- Метка тренера: `PUT …/user-members/{userId}/label` (`HubGroupUserService.cs:225-240`); резолвер
  «кем аккаунт стоит в группе» — `HubGroupPersonResolver` (метка > AppUser.SwimmerId > Me > Family).
- «Me» пользователя: `Sys_UserFavorites.IsPrimary` (ставит шаг «It's me» профиля,
  `setup-profile-project/profile-api.ts:51-74`).
- Шаг группы в профиле: `joinGroup` (`profile-api.ts:81`), окно выбора группы —
  `setup-profile-project/components/profile-edit-popup.tsx` (`GroupOption`, `join_policy`).
- Таб Admin группы: `group-page.tsx` (~`:356`), инбокс публикаций `components/group-admin.tsx`
  (`PublicationsInbox`), счётчик `pendingPublications` (`group-page.tsx:210-221`, бейдж `:362`).
- Web Push: `IPushSender` (`Swimm.Application/Abstractions/IPushSender.cs`), подписки —
  `IPushSubscriptionService`; устройство и footguns — `docs/plans/web-push-reminders-plan.md`
  (§5 модель, §10 ловушки; на проде пуш включён флагом `PushReminders:Enabled`).

## 4. Модель (предложение)

Заявка привязана к членству: колонка **`Sys_HubGroupUserMembers.CoachRequestedAt`**
(`timestamp null`) — «этот участник просит права тренера». Миграция одна, Sys_-таблица, грант
`swimm_ro` не нужен. Одобрение: `AddAdmin` + метка на «Me»-пловца (если он в составе) + pending→active
+ `CoachRequestedAt = null`. Отказ: `CoachRequestedAt = null`.
(Альтернатива — отдельная таблица заявок; не нужна, пока заявка ровно одна на пару группа–аккаунт.)
⚠ InMemory не видит CK Postgres — новые пути записи покрыть *PgTests (правило проекта).

## 5. Этапы

- ✅ **К1. Сервер.** Миграция; `POST /api/me/hub-groups/{id}/coach-request` (вступить, если ещё нет,
  с пометкой; уже админ — 409/ок без действия); `GET …/coach-requests` и
  `POST …/coach-requests/{userId}/approve|decline` (только `CanManageAdmins`). Юнит-тесты +
  PgTests на запись.
- ✅ **К2. Пуш.** При новой заявке — push владельцу группы (заголовок «Coach request», текст «<имя>
  asks to coach <группа>», URL — таб Admin группы, tag `coach-req:{groupId}`). Пусто/не настроено —
  молча (`IsConfigured`).
- ✅ **К3. Профиль.** «Request access» открывает выбор группы (то же окно, что шаг группы) и шлёт
  coach-request вместо простого join; после — понятный текст «Request sent to the group owner».
- ✅ **К4. Таб Admin.** Карточка «Coach requests» (имя, его «Me»-пловец, Approve / Decline), бейдж
  таба = публикации + заявки.
- ✅ **К5. Доки.** Строка в `docs/ROADMAP.md`, обновить `docs/hubgroups-architecture.md` (права в
  группе), справка о группах `src/content/help/groups` — если там описано, как стать тренером.

## 6. Как проверить

Персонажи utest (`docs/plans/test-personas-plan.md`, `/api/dev/persona?as=<ник>`): тренер-персонаж
жмёт Request access → владелец видит счётчик и карточку → Approve → у тренера группа в My groups,
он видит редактор дорожек; метка на его пловца стоит. Отказ — остаётся участником.

## 7. Как сделано (10.11.2026)

**Сервер.**
- Колонка `Sys_HubGroupUserMembers.CoachRequestedAt` (`timestamp null`), миграция
  `20261110064335_AddHubGroupCoachRequest`. Грант не нужен (Sys_).
- `IHubGroupUserService` (+ `HubGroupUserService`): `RequestCoachAccessAsync` (не участник —
  вступление через `JoinAsync`, то есть с той же политикой, приватностью и потолками, затем
  пометка; уже участник — только пометка; владелец/админ — `AlreadyCoach`; повтор — успех без
  `Created`), `GetCoachRequestsAsync` (DTO `HubGroupCoachRequestDto`: «Me»-пловец с ивритским
  именем и флаг «в видимом составе»), `ApproveCoachRequestAsync`, `DeclineCoachRequestAsync`.
  `JoinedHubGroupDto.CoachRequested` — висит ли заявка.
- Одобрение одной записью: админ (если ещё не админ/владелец) + `Status=active` + метка `SwimmerId`
  на «Me», **только если метки ещё нет** (ручную метку владельца, напр. «родитель X», не
  перебиваем) + `CoachRequestedAt=null`. Гонка по UNIQUE админов (23505) — довести остальное без
  строки админа.
- `MyHubGroupsController`: `POST {id}/coach-request` (409 — уже управляет; ответ
  `{ created }`), `GET {id}/coach-requests`, `POST {id}/coach-requests/{userId}/approve|decline` —
  все три последних под `CanManageAdmins`.
- Push: `ICoachRequestNotifier` → `CoachRequestNotifier` — только владельцу, только при
  `Created`, молча при `!IsConfigured`/без подписок; заголовок «Coach request», текст
  «<DisplayName> asks to coach <Name>», URL `/groups/{slug}?tab=admin`, tag `coach-req:{id}`;
  умершие подписки удаляются тем же `PushDeliveryBookkeeping`. Флаг `PushReminders:Enabled` на
  него не действует (это флаг джоба напоминаний).

**Клиент.**
- `setup-profile`: «Also → Are you a coach? → Request access» ставит роль и открывает окно
  `ProfileEditPopup` в режиме `coach` (тот же список групп, кнопка «Send request», плашка «the
  owner will review»); тост «Request sent to the group owner» без Undo (заявку отзывать нечем).
  У кого роль тренера уже отмечена — строка «Coaching a group? Request coach access» (под шагами
  и в итоге).
- Группа: карточка `components/group-coach-requests.tsx` в табе Admin над инбоксом публикаций;
  бейдж таба = публикации + заявки. Права на клиенте не дублируются: 403 от сервера → карточки нет,
  в бейдж 0 (админ группы её не видит).
- Справка `/help/groups` (en/ru/he): раздел «You coach a group someone else created».

**Тесты.** `CoachAccessRequestTests` (10, InMemory: вступление с пометкой, повтор, владелец/админ,
тест-группа, approve с активацией и меткой, ручная метка не перебивается, уже админ, decline и
повторная заявка, push — только владельцу/Gone удаляется/не настроено — молча) и
`CoachAccessRequestPgTests` (настоящий Postgres: заявка → approve/decline, перечитано из базы,
права `IsGroupAdmin`/`!CanManageAdmins`). Весь набор — 3684/3684.

**Проверено вживую (10.11, :5079 + :5181, персонажи utest):** `utest-swimmer-me` → заявка в
`utest-open` (200 `created:true`, повтор `false`, свой список — 403); `utest-group-admin` —
403; `utest-coach` (владелец) видит карточку («Me: מעוז עדי · in the roster»), бейдж «2 new»
(1 публикация + 1 заявка) → Approve → карточка ушла, «1 new», в базе админ + active + метка 7932,
повторная заявка → 409. Отказ: `utest-newbie` → `utest-approval` (pending) → Decline → заявка
снята, участие pending осталось. Push не проверен на устройстве: локально подписок нет. После
проверки данные utest откатаны, persona → default.

**Перед деплоем:** `--migrate` на проде (`docs/deploy.md`, «Выкатить код», шаг 2).
