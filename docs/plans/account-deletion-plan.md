# План — кнопка «Delete account»

**Статус:** ✅ У0–У5 сделаны 23.10.2026 (не закоммичено, не на проде); У6 — отдельно, по запросу.
Миграция `AccountDeletionSetNullUserRefs` применена только локально — на прод едет вместе с выкаткой.

**Проверено:** юнит-тесты правила + 4 PG-теста сервиса (все развилки Р2/Р3/Р6/Р7, каскады, SET NULL,
обезличивание аудита); вживую в панели — блокировка у владельца групп (`utest-coach`), удаление
`utest-media-author` с сайта (публичное видео пропало со страницы пловца — кэш сброшен), удаление
`utest-member` из `/Admin/Users`. **Не проверено глазами:** «Sign in again» с настоящим Google (в dev
персонаж всегда «вошёл только что») и поле пароля в окне (у персонажей пароля нет; серверная ветка —
в PG-тесте).
**Решения:** Р1–Р7 приняты Владом 23.10.2026 — все по рекомендации (§6).

## Зачем

Сейчас удалить аккаунт можно только письмом на privacy@swimhub.co.il
([privacy/en.md](../../client/src/content/help/privacy/en.md), раздел «Deleting your account»), и
выполнять такую просьбу админу нечем: в коде нет ни одного пути удаления пользователя, кроме сброса
тестовых `utest`-персонажей (`PersonaSeeder.ResetAsync`). Нужна кнопка для человека и тот же путь для
админа — по письму, в том числе по просьбе родителя за ребёнка.

## Как сейчас (сверено с кодом и прод-базой 23.10.2026)

- Прод: 3 настоящих аккаунта, 1 группа, у неё нет других участников; 32 медиа, 10 публикаций.
  Сложные ветки (чужие люди в группе) пока теоретические — делаем дёшево, но правильно.
- Страницы аккаунта нет. Домашняя страница аккаунта по факту — `/setup-profile`: пункт меню
  «Set up profile» / «My profile» стоит первым в меню пользователя всегда (`app-topbar.tsx`,
  `MyProfileItem`). Новый маршрут не нужен.
- Чужие сессии после удаления умирают сами: `CookieSecurityStampValidator` при ре-валидации не
  находит пользователя → `RejectPrincipal` (до ~5 минут). Свою — гасим `SignOutAsync` сразу.
- Сброс кэша при удалении — сам: `CacheInvalidationInterceptor` добавляет таблицы, куда удаление
  каскадит в базе. Проверить тестом (У4), что публичное медиа пропадает со страницы пловца.

### Что держит пользователя (FK на `Sys_AppUsers`)

**Каскад — уходит само при `Remove(user)`** (личные данные, так и задумано):
`Sys_AppUserRoles`, `Sys_UserExternalLogins`, `Sys_UserLocalCredentials`, `Sys_UserSecurityTokens`,
`Sys_UserLoginHistory`, `Sys_UserFavorites`, `Sys_UserMedia` (→ каскадом `Sys_UserMediaPublications`,
`Sys_UserReactions` к этому медиа, `Sys_MediaReports` на это медиа), `Sys_UserReactions`,
`Sys_UserStartListPlans`, `Sys_UserNotificationSettings`, `Sys_UserPushSubscriptions`,
`Sys_ReminderDeliveries`, `Sys_MediaReports` (как жалобщик), `Sys_HubGroupUserMembers`,
`Sys_HubGroupAdmins` (как админ), `Sys_HubGroupAccountLevels`, `Sys_HubGroupBreaks`,
`Sys_HubGroupTrainingRsvps` (ответы «приду» от аккаунта — и прошлые тоже),
`Sys_HubGroupReminderMutes`, `Sys_HubGroupUserSeen`.

**SetNull — запись остаётся, автор пропадает:** `Sys_HubGroupMedia.CreatedByUserId` (ссылки в
галерее группы), `TrainingSessions.CreatedByUserId`, `LanePlans.CreatedByUserId`,
`Sys_HubGroupTrainingCancellations.CreatedByUserId`, `SetByUserId` у перерывов и RSVP,
`Sys_UserMediaPublications.DecidedByUserId`.

**Restrict — удаление упадёт** (это и есть работа):

| FK | Что это | Что делаем |
|---|---|---|
| `HubGroups.OwnerUserId` | владелец группы | **оставить Restrict** — ловит «забыли про группы»; группы решаем явно (Р2) |
| `Sys_HubGroupAdmins.GrantedByUserId` (not null) | кто выдал админство | миграция: nullable + SetNull |
| `Sys_HubGroupUserMembers.AddedByUserId` | кто добавил участника | миграция: SetNull (уже nullable) |
| `Sys_HubGroupClubRequests.UserId` (not null) | заявитель на «официальную» группу | **оставить Restrict**: заявитель — владелец группы на момент подачи, передачи групп нет, значит его заявки уходят каскадом вместе с его группой. Nullable заставил бы учить одобрение заявки жить без заявителя (роль Coach, письмо) |
| `Sys_HubGroupClubRequests.DecidedByUserId` | кто решил заявку (админ сайта) | миграция: SetNull |

**Без FK — сами не почистятся:** `Swimmers.LogligIdSuggestedByUserId` (обнулить),
`Sys_AdminAudit.ActorUserId` + `ActorName` = **снимок email** (Р5), `Sys_MediaReports.DecidedByUserId`
(админ сайта — оставить, так задумано в `MediaReport.cs`).

**Не трогаем:** результаты, пловцы (в т. ч. тот, на кого указывает `AppUser.SwimmerId` — «это я»),
локальные пловцы групп (принадлежат группе, не аккаунту), клубы.

## Как будет выглядеть

**`/setup-profile` → внизу блок «Delete account»** (виден всегда, и до завершения шагов). Кнопка
вторичная, красная обводка. По клику — окно на основе `confirm-dialog` (как `delete-group-dialog.tsx`):

1. **Что удалится** — числа из `GET /api/me/account/deletion-impact`: избранное N, ссылки на медиа N
   (из них опубликованы M — пропадут из групп и клубов), реакции, планы стартовых протоколов,
   участие в группах, уведомления. Группы, которыми владеет, — списком с тем, что уйдёт с каждой
   (переиспользуем `HubGroupDeleteImpactDto`).
2. **Что останется** — результаты соревнований и страницы пловцов; то, что добавлено в чужие
   группы (галерея, тренировки), — без имени автора (Р4).
3. **Блокировка, если нельзя** (Р2, Р6): «You own a group with other members — delete it or hand it
   over first» со ссылкой на группу; «Site admins can't delete their account here».
4. **Подтверждение** (Р3): ввести свой email + пароль (если есть локальный вход) или «Sign in again
   to confirm» (только Google, вход старше 10 минут).
5. Кнопка **Delete my account** → `DELETE /api/me/account` → редирект на `/` с тостом «Your account
   was deleted». Повторный вход тем же Google создаст новый пустой аккаунт — сказать об этом в окне.

Весь видимый текст — на английском (правило UI), длинные пояснения — `src/content` + `UI_Markdown`.

## Этапы

### У0. Миграция FK (Restrict → SetNull) ✅
- Три FK из таблицы выше (`GrantedByUserId` → `int?`, `AddedByUserId`, `ClubRequest.DecidedByUserId`).
  ⚠ `HubGroupAdminService`: `SelfJoined = AddedByUserId == null` — после удаления того, кто
  добавил, участник выглядит «вступил сам». Приемлемо (подпись в админке), записано здесь.
- `/db-migrate`. Таблицы `Sys_*` — грантов `swimm_ro` не нужно.
- **Приёмка:** миграция применяется локально; `\d` таблиц показывает `ON DELETE SET NULL`.

### У1. Правило и контракт (Application) ✅
- `Swimm.Application/Mapping/AccountDeletionRules.cs` — чистая функция: можно ли удалить и почему
  нет (`site-admin`, `owns-shared-group`), какие группы уходят вместе с аккаунтом. По образцу
  `HubGroupCreationRules`.
- `IAccountDeletionService`: `GetImpactAsync(userId)`, `DeleteAsync(userId, AccountDeletionRequest)`;
  DTO `AccountDeletionImpactDto` (счётчики, группы с их impact, причины блокировки).
- **Приёмка:** юнит-тесты правила на все развилки.

### У2. Сервис удаления (Infrastructure) ✅
Одна транзакция, порядок важен:
1. Проверки правила (У1) + подтверждения (Р3). Пароль — `Argon2PasswordHasher`.
2. Группы, уходящие вместе с аккаунтом, — через `IHubGroupAdminService.DeleteAsync`: он уже
   удаляет тренировки раньше группы (иначе падает на RESTRICT `Sys_TrainingResults` → пловец) и
   пишет аудит `hubgroup.delete`.
3. `Swimmers.LogligIdSuggestedByUserId = null` (ExecuteUpdate); обезличить `Sys_AdminAudit` (Р5).
4. Аудит `user.delete`: `EntityId` = id, сводка — только числа, **без email** (иначе удаление
   оставит то, что удаляли). Актор — сам пользователь (`self`) или админ (У5).
5. `Remove(user)` → `SaveChanges` → commit. Остальное — каскад базы.
- **Приёмка:** PgTests (InMemory не видит CK и порядок каскадов Postgres — правило из памяти).

### У3. API + клиент ✅
- Новый `AccountController` с `[Route("api/me/account")]` (по образцу `FavoritesController` —
  у каждой темы `api/me/*` свой контроллер): `GET deletion-impact`, `DELETE`. `[Authorize]`, antiforgery
  (SPA берёт токен с `/api/antiforgery/token`), rate limit `auth`. Ошибки: 409 + причина
  блокировки, 403 — неверное подтверждение. После успеха — `SignOutAsync`.
- Клиент: кнопка «Delete account» под карточкой `/setup-profile` (оба вида — чек-лист и итог),
  окно `components/delete-account-dialog.tsx` на общем `ConfirmDialog`, вызовы — `account-api.ts`
  (повтор только на 400 — протухший антифоржери; 403 не повторяется, он в лимите входа). После
  удаления — экран «Your account was deleted» на той же странице. Короткие подписи — в коде:
  длинной справки нет, раздел «Deleting your account» — в `/privacy`.
- Ре-аутентификация Google: claim `swimm:auth_time` (`AccountDeletionRules.AuthTimeClaim`) пишет
  `IssueAuthCookieAsync` (`IssuedUtc` не годится — sliding expiration его переписывает);
  «Sign in again» = `/auth/login/google?reauth=true&returnUrl=/setup-profile?delete=1` —
  `reauth` просит у Google выбор аккаунта (`prompt=select_account`), `?delete=1` открывает окно
  само и убирается из адреса. В Development `DevAdminBypass` пишет `auth_time` = сейчас на каждом
  запросе: персонажу войти через Google заново нечем.
- **Приёмка:** в панели браузера на тестовом персонаже `utest` — полный путь до удаления; после
  удаления `/auth/me` — гость, вторая вкладка теряет сессию после ре-валидации.

### У4. Тесты ✅
`AccountDeletionPgTests`:
- аккаунт «со всем» (избранное, медиа с публикацией в группу и реакциями, RSVP, push, участие в
  чужой группе, ссылка в галерее чужой группы, выданное админство) → удалён, ни одной строки с его
  id, ссылка в галерее осталась без автора;
- владелец группы без других людей → группа с локальными пловцами и результатами тренировок ушла;
- владелец группы с участником → 409, ничего не удалено;
- админ сайта → 409;
- неверный пароль / email → 403, ничего не удалено;
- после удаления публичное медиа пропало со страницы пловца (сброс кэша по каскаду).

### У5. Админка `/Admin/Users` + тексты ✅
- Кнопка «Удалить аккаунт» в карточке пользователя — тот же `IAccountDeletionService`, без
  пароля, с причиной (письмо privacy@, просьба родителя), антифоргери. Владелец групп с
  участниками — админ решает за него (удалить группы или отказать). MD страницы
  `docs/admin-pages/users.md` — обновить (правило definition of done).
- Политика `privacy/{en,ru,he}.md` «Deleting your account»: кнопка на My profile + письмо как
  запасной путь; что удаляется и что остаётся — по факту реализации (Р4). `terms/*` — ссылка.
- `ServicesCatalog.cs` (Gmail privacy@) — «удалить аккаунт» теперь делается кнопкой в /Admin/Users.
- `docs/ROADMAP.md` 4.7 → ✅.

### У6. Передача группы (отдельно, по запросу)
Когда у кого-то появится группа с людьми: владелец передаёт группу одному из её админов
(`Sys_HubGroupAdmins`), он же — путь «hand it over» из окна удаления. Свой план, не блокирует У0–У5.

## 6. Решения (приняты Владом 23.10.2026 — все по рекомендации)

| # | Вопрос | Рекомендация | Альтернатива |
|---|---|---|---|
| Р1 | Удалять сразу или с отсрочкой на отмену? | **Сразу и навсегда.** Бэкапы уходят за 14 дней — это уже написано в политике. | Деактивация + удаление джобом через 7 дней, вход отменяет (новый джоб в `/Admin/Jobs`, +0,5 дня) |
| Р2 | Группы, которыми владеет | **Группы без других людей уходят вместе с аккаунтом (видно в окне); с участниками или админами — кнопка заблокирована** до удаления группы или передачи (У6) | Удалять все группы вместе с аккаунтом с жирным предупреждением |
| Р3 | Как подтверждать | **Ввести email + пароль (если есть локальный вход); для Google-only — вход не старше 10 минут** | Письмо со ссылкой (`SecurityTokenPurpose.AccountDeletion`) — одинаково для всех, но зависит от доставки почты |
| Р4 | Ссылки, добавленные в галерею/тренировки чужих групп | **Остаются в группе без автора** (это контент группы; поправить формулировку политики) | Удалять вместе с аккаунтом |
| Р5 | `Sys_AdminAudit.ActorName` хранит email актора | **Заменить на `deleted user #id`** в записях этого пользователя | Оставить как есть (законный интерес, но противоречит «удаляем данные аккаунта») |
| Р6 | Админ сайта жмёт кнопку | **Нельзя, пока у аккаунта роль Admin** — снять роль отдельно | Можно, если он не последний Admin |
| Р7 | Удаление из админки по письму | **Да, У5** — тем же сервисом, с причиной в аудите | Только кнопкой у пользователя; письма — вручную SQL |

## 7. Ревью безопасности (23.10.2026)

**Проверено атакой на локальном API** (персонаж `utest-member`, не админ):
- чужой аккаунт: `DELETE /api/me/account` с подсунутыми `userId` и чужим email → 403
  `email-mismatch`; id берётся только из куки, в теле его нет;
- `GET deletion-impact?userId=чужой` → ответ про себя (параметр игнорируется);
- обработчик `/Admin/Users?handler=DeleteAccount` не-админом → редирект на вход, ничего не удалено;
- без `X-XSRF-TOKEN` → 400. Кука `HttpOnly`, `SameSite=Lax`; CORS — только localhost и только
  в Development; `swimm:auth_time` лежит в зашифрованной куке, подделать нельзя; dev-обход
  (`auth_time` = сейчас) включается только при `IsDevelopment()` и флаге `DevAdminBypass`.

**Найдено и исправлено:**
1. **Оракул пароля мимо блокировки.** Проверка пароля при удалении не трогала счётчик входа —
   из чужой открытой сессии пароль можно было подбирать (10/мин с IP) без lockout. Теперь
   `ILocalAuthService.VerifyPasswordAsync`: тот же счётчик, 5 неверных → блокировка на 15 минут
   и для входа, и для удаления; попытки — в журнале входов. Тест
   `DeleteSelf_WrongPasswords_ShareLoginLockout_EvenRightPasswordRefused`.
2. **XSS в `/Admin/Users`** (три места были и до этой работы, четвёртое добавил я): email внутри
   `onsubmit="return confirm('… @u.Email …')"`. Регистрация пропускает email ``x'-alert`1`-'@a.co``
   (`MailAddress` считает его валидным), Razor кодирует `'`, браузер раскодирует до запуска JS —
   код выполнялся при клике админа (в ProdAdmin — против прод-базы). Подтверждено на локальной
   базе, исправлено на `data-confirm` + `confirm(this.dataset.confirm)`; сторож —
   `AdminInlineJsInjectionTests` (проверено, что на старой версии файла он падает).

3. **Открытый редирект** `GET /auth/login/google?returnUrl=…` и `/auth/logout?returnUrl=…` —
   был `Redirect(returnUrl)` без проверки (до этой работы): ссылка на наш вход уводила после него на
   чужой сайт. Теперь `Security/ReturnUrlRules.cs`: путь своего сайта (не `//host`, не `/\host`)
   или полный http(s)-адрес на хост запроса, канонический хост (`Seo:BaseUrl`) или localhost на
   машине разработчика; иначе `/`. Полный адрес оставлен намеренно — топбар и окно входа шлют
   `window.location.href`, а в деве страница на порту Vite. Проверяется и в `LoginGoogle`, и в
   callback (туда можно прийти прямой ссылкой). Тесты `ReturnUrlRulesTests`; вживую curl по
   `/auth/logout`: `https://evil.com`, `//evil.com`, `/\evil.com`, `https://swimhub.co.il@evil.com` →
   `/`, свой путь и адрес Vite (в т. ч. через прокси Vite) — как были.

**Найдено, не исправлено:**
- **Чужие сессии живут до 5 минут** после удаления (интервал ре-валидации куки). Писать им нечего:
  все личные таблицы держат FK на пользователя, запись упадёт.
- **Гонка с группами:** список своих групп читается до транзакции — если кто-то вступит в
  «одинокую» группу в эти миллисекунды, она удалится с ним. Вредит только самому удаляющему.

## Как вернуться к работе

1. §6 — решения приняты; если что-то пересматривается, править там.
2. Начать с У0 (`/db-migrate`), затем У1 → У2 → У4 (тесты рядом с сервисом) → У3 → У5.
3. Проверять на `utest`-персонаже (`/api/dev/persona?as=<ник>`, после — `?as=default`).
4. Перед push — `docs/pre-push-rules.md` (кэш, админ-страница → её MD).
