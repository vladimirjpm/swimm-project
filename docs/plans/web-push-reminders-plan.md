# Напоминания о тренировках через Web Push — план

Создан 20.10.2026. Статус: **П0–П2 сделаны в коде 20.10.2026** (не закоммичено, не на проде);
§11 решён — все рекомендации приняты. П3–П5 — следующим заходом.

## 0. Как вернуться к работе (новый чат — начни отсюда)

1. Прочитай этот файл целиком, особенно §4 (главная ловушка) и §12 (что сделано и как устроено).
2. **Что осталось до «готово» П0–П2** (делает Влад или с ним):
   - сгенерировать прод-ключи ОДИН раз: `dotnet run --project server/Swimm.API -- --generate-vapid-keys`,
     положить `WebPush__PublicKey` / `WebPush__PrivateKey` в App Service (реестр —
     `azure-deploy-plan.md` §5), приватный — в менеджер паролей;
   - миграция `AddWebPushReminders` на прод — из ProdAdmin (`--migrate`), как обычно;
   - деплой; проверить Always On (§4 п.3);
   - на проде: Android Chrome и iPhone с экрана «Домой» → `/reminders` → «Turn on» → «Send test»
     доходит; отозвал разрешение → после «Send test» устройство пропало из списка;
   - тест-группа `utest`: расписание на сегодня через ~2 ч 05 мин → одно напоминание; рестарт
     API дубля не даёт; на `/Admin/Jobs` (ProdAdmin) карточка «Напоминания о тренировках» —
     «работает».
   Локально всю цепочку можно проверить в обычном Chrome на `http://localhost:5173/reminders`
   (localhost — безопасный контекст): ключи для разработки лежат в gitignored
   `appsettings.Local.json` (секция `WebPush`). Во встроенном браузере приложения push запрещён
   самим браузером — там видно только «Notifications are blocked».
3. Перед кодом сверься с [`../ARCHITECTURE.md`](../ARCHITECTURE.md) и
   [`../pre-push-rules.md`](../pre-push-rules.md): новые таблицы `Sys_*`, новый джоб на
   `/Admin/Jobs` и переменные окружения прода подпадают под правила «поменял X → обнови Y».

## 1. Цель и что решено

**Цель:** участник группы получает на телефон напоминание о ближайшей тренировке, как от
обычного приложения.

Решено 20.10.2026, обсуждалось в чате:

- **SMS не делаем.** Azure в Израиль SMS напрямую не шлёт, только через Messaging Connect
  (Infobip). Эта функция пока в preview. Стоимость ~$0.25 за сообщение, и напоминания вышли бы
  в сотни долларов в месяц.
- **Телефон не собираем и не подтверждаем.** Человека уже подтверждает вход через Google или
  email. Push-подписка привязана к аккаунту, номер для неё не нужен.
- **Приложений в Google Play и App Store не делаем.** Уведомления шлёт сам сайт (Web Push),
  без сторов, взносов и проверки Apple.
- **Канал — Web Push**, письмо через Resend — запасной канал (П4).
- **Стоимость — $0.** Push-службы Google, Apple и Mozilla бесплатны, библиотека тоже.

## 2. Как работает Web Push (коротко)

```
браузер ──(1) разрешение + subscribe(VAPID public key)──▶ push-служба браузера
   │                                                     (FCM у Chrome, Apple у Safari,
   │                                                      Mozilla у Firefox)
   └──(2) POST /api/push/subscriptions {endpoint, p256dh, auth} ──▶ наш API → Sys_UserPushSubscriptions

наш джоб ──(3) зашифрованный POST на endpoint, подписан VAPID private key──▶ push-служба
push-служба ──(4)──▶ service worker на телефоне → showNotification() → тап открывает сайт
```

- **VAPID** — пара ключей сервера. Публичный ключ отдаём клиенту, приватный хранится в
  конфиге. ⚠ Смена ключей аннулирует ВСЕ подписки: сгенерировать один раз и больше не менять.
- **Service worker** (`/sw.js`) получает push, даже когда вкладка закрыта.
- Ответ push-службы **404/410** значит, что подписка умерла (человек отозвал разрешение,
  переустановил браузер). Такую строку удаляем.

### Платформы

| Где | Работает? | Условие |
|---|---|---|
| Android, Chrome / Edge / Samsung | ✅ | разрешение по кнопке, ставить ничего не надо |
| iPhone / iPad | ✅ с iOS 16.4 | **только если сайт добавлен на экран «Домой»** (Safari → Поделиться → На экран «Домой») и открыт с иконки. В обычной вкладке Safari push нет |
| Десктоп Chrome / Edge / Firefox / Safari | ✅ | разрешение по кнопке |

Особенности iOS, которые надо учесть:
- разрешение можно спрашивать **только по нажатию пользователя** (кнопка), не при загрузке;
- каждый push **обязан показать уведомление**. «Тихий» push Safari считает нарушением и
  отзывает подписку;
- кнопок-действий в уведомлении (`actions`) на iOS нет, поэтому «Going / Not going» прямо из
  уведомления работает только на Android (П5);
- по отзывам разработчиков, подписки на iOS иногда «отваливаются» после долгого простоя.
  Поэтому нужен запасной канал (П4) и отметка «последняя успешная доставка» у подписки.

## 3. Что уже есть в коде (инвентарь на 20.10.2026)

| Есть | Где | Что даёт плану |
|---|---|---|
| `manifest.json` (имя SwimHub, `display: standalone`, иконки 192/512), подключён во всех html | `client/public/manifest.json` | основа для установки на экран «Домой». Не хватает `id`, `scope` и maskable-иконки (П0) |
| `apple-touch-icon.png` | `client/public/` | иконка на экране «Домой» iPhone |
| **Service worker — нет** | — | заводим `client/public/sw.js` (П0) |
| **Поля телефона и push — нет** | — | всё с нуля |
| Расписание группы и ближайшие занятия (местное время Израиля, разовые, отмены) | `Swimm.Domain/GroupTrainingSchedule.cs`, `Swimm.Application/Mapping/TrainingOccurrences.cs` (`Upcoming`, `Find`) | «какие тренировки скоро», брать готовое |
| Ответы «иду / не уверен / не приду» по ключу (дата + начало) | `HubGroupTrainingRsvp` (`Sys_HubGroupTrainingRsvps`), `TrainingRsvpRules.SessionKey` | кого не беспокоить (ответил «No») |
| Статус «On break» / «Doesn't train» | `HubGroupBreak` (`Sys_HubGroupBreaks`) | им не напоминать |
| Участники-аккаунты | `HubGroupUserMember` (`Status` active/pending) | получатели — только active |
| Шаг фоновых расписаний + журнал | `Swimm.API/BackgroundServices/ScheduledJob.cs`, `Sys_JobRuns`, `Sys_JobRunLog`, `/Admin/Jobs` | каркас джоба и карточка в админке |
| Почта | `IEmailSender` (Resend на проде) | запасной канал (П4) |
| Rate limiting, antiforgery для SPA | `Program.cs` (политики), `GET /api/antiforgery/token` | защита новых эндпоинтов |

## 4. ⚠ Главная ловушка: на проде расписания сейчас НЕ работают

В `Program.cs` фоновые службы двух сортов:
- **очереди** — только при `adminEnabled`;
- **расписания** (Discovery, loglig) — только при `backgroundJobsEnabled`, то есть **только в
  ProdAdmin на машине Влада**. На самом проде из фоновых работают лишь пинг БД и мост кэша.

Напоминания должны идти **с прода круглосуточно**: ProdAdmin запущен не всегда. Отсюда:

1. **Третий сорт — «прод-расписание».** Отдельный флаг `PushRemindersEnabled`: на проде —
   `true`, в ProdAdmin и в локальной разработке по умолчанию `false`. ProdAdmin пишет в ту же
   прод-базу. Если джоб включится и там, напоминания уйдут дважды.
2. **Защита от двойной отправки в базе, а не во флаге.** Сначала вставляем строку в журнал
   доставки с UNIQUE `(UserId, HubGroupId, SessionDate, SessionStart, Kind)`, потом шлём. Не
   вставилось — значит, уже отправлено. Это же страхует от рестарта посреди прогона и от
   будущего Scale out > 1.
3. **Always On** на App Service включён (`azure-deploy-plan.md`), без него B1 засыпает и
   джоб не тикает. Проверить в портале перед П2.
4. Карточка на `/Admin/Jobs` читает `Sys_JobRuns` прод-базы — в ProdAdmin её будет видно,
   хотя сам джоб там выключен. В карточке подписать: «работает на проде».

## 5. Модель данных (всё в `Sys_*`, БЕЗ grant `swimm_ro`)

```
Sys_UserPushSubscriptions
  Id, UserId (FK AppUser, cascade), Endpoint (text, UNIQUE), P256dh, Auth,
  DeviceLabel (из UserAgent: "iPhone · Safari", "Android · Chrome"), CreatedAt,
  LastSuccessAt, LastFailureAt, FailureCount

Sys_UserNotificationSettings          -- или колонки на AppUser; решить в П1
  UserId (PK), RemindersEnabled, LeadKind ('2h' | '1h' | 'evening' | ...), EmailFallback

Sys_HubGroupReminderMutes             -- «не напоминать про эту группу»
  UserId, HubGroupId (PK пара)

Sys_ReminderDeliveries                -- журнал и защита от дубля (§4 п.2)
  Id, UserId, HubGroupId, SessionDate, SessionStart, Kind ('reminder' | 'cancelled'),
  Channel ('push' | 'email'), CreatedAt, SentAt, Result
  UNIQUE (UserId, HubGroupId, SessionDate, SessionStart, Kind)
```

- Миграция — через `/db-migrate` (два контекста). Публичных таблиц нет, `02-grants.sql` не
  трогаем.
- Кэш: таблицы пишутся вне витрин, но сторож К5 («запись без меток данных») может
  потребовать регистрацию. Проверить `docs/plans/cache-tags-plan.md` §4 при П1.
- `seed-tables.txt`: эти таблицы на прод НЕ переносятся (личные данные, подписки
  привязаны к устройству) — отметить в списке исключений.
- `sync-from-prod` не должен тянуть подписки в локальную базу: иначе локальный тест
  «отправить всем» разошлёт push живым людям. Проверить, что `Sys_*` туда не едут.

## 6. API (контроллеры видят только интерфейсы Application)

| Метод | Путь | Что |
|---|---|---|
| GET | `/api/push/vapid-public-key` | публичный ключ, без авторизации |
| POST | `/api/push/subscriptions` | `[Authorize]` + antiforgery: upsert по `Endpoint`, привязка к текущему пользователю |
| DELETE | `/api/push/subscriptions` | тело `{ endpoint }`, удаляет только свою |
| POST | `/api/push/test` | push себе на все свои устройства («Send test») — rate limit |
| GET / PUT | `/api/me/notification-settings` | включено, когда, запасной канал, отключённые группы |

Порты: `IPushSubscriptionRepository`, `IPushSender` (отправка одного сообщения, итог
`Delivered | Gone | Failed`), `IReminderPlanner` (кому что слать — чистая логика),
реализации — в Infrastructure. Библиотека — NuGet `WebPush` (web-push-csharp) или
`Lib.Net.Http.WebPush`; выбрать в П0 по живости репозитория.

Конфиг: `WebPush:PublicKey`, `WebPush:PrivateKey`, `WebPush:Subject` (`mailto:` адрес
сайта). На проде — переменные окружения App Service; дописать в реестр
`azure-deploy-plan.md` §5. Приватный ключ в репозиторий не кладём.

## 7. Правило «кому и когда» (чистая функция в Application + юнит-тесты)

Вход: «сейчас» (местное время Израиля), группы с расписанием, занятия из
`TrainingOccurrences.Upcoming`, участники, ответы, перерывы, настройки, журнал доставки.
Выход: список `(UserId, HubGroupId, Occurrence, Kind)` к отправке.

Черновик, окончательно решается в §11:
- занятие не отменено и начинается через «время напоминания» (окно = период джоба,
  например 5 минут: `start - lead ∈ (now - 5 мин, now]`);
- участник `active`, нет открытого `HubGroupBreak`, группа не отключена у него;
- ответ «No» — не напоминаем; «Yes» — «Training today 18:00 · <группа>»; ответа нет —
  «Are you coming?»;
- в журнале доставки такой строки ещё нет;
- одно напоминание на пользователя на занятие, сколько бы детей семьи ни было в группе.

Текст — английский (правило UI), название группы — как в базе (данные). Тап по
уведомлению открывает таб Trainings группы — ссылку строить через `routes.*()` на клиенте
или зеркальным правилом на сервере (контракт чистых URL, три зеркала — `client/CLAUDE.md`).

Тесты — по образцу `GroupTrainingScheduleTests` / `StartListScheduleServiceTests`: граница
окна, отменённое, разовое, «On break», ответ No, повторный прогон не дублирует, переход на
летнее время.

## 8. Клиент

- **`client/public/sw.js`** — простой JS, отдаётся как `/sw.js` (scope `/`): `push` →
  `showNotification` (иконка, `tag` = ключ занятия, чтобы повтор заменял, а не копился),
  `notificationclick` → открыть или сфокусировать вкладку с адресом из `data.url`.
- **Регистрация** — в общем модуле, который зовёт каждая точка входа (сборка multi-page:
  правка одного `index.tsx` до других страниц не доедет).
- **UI** (дизайн уточнить у Влада, хендоффа пока нет):
  - в меню пользователя — «Reminders»: включить на этом устройстве, «Send test», время
    напоминания, список устройств, запасной канал;
  - в табе Trainings группы — переключатель «Remind me» на группу;
  - **iPhone без экрана «Домой»** (`navigator.standalone === false` в Safari iOS) — вместо
    кнопки подсказка «Add SwimHub to Home Screen» со ссылкой на справку `/help/reminders-iphone`
    (три файла `src/content/help/…/{en,ru,he}.md`, правило длинных текстов).
- Разрешение спрашивать только по кнопке. Отказал — показать, как включить в настройках
  браузера, и больше не дёргать.
- `manifest.json`: добавить `id: "/"`, `scope: "/"`, maskable-иконку.

Сервер (`Program.cs`):
- `/sw.js` — `Cache-Control: no-cache`, правильный `Content-Type`, rewrite чистых URL его
  не трогает;
- Cloudflare не должен держать `/sw.js` в кэше долго — проверить правила кэша.

## 9. Этапы

| Этап | Что | Готово, когда |
|---|---|---|
| **П0** спайк | ключи VAPID, `sw.js`, кнопка «Send test» на себя без таблиц (подписка в памяти), выбор библиотеки | push пришёл на Android Chrome и на iPhone с экрана «Домой» — **на проде** (iPhone требует HTTPS, localhost проверяет только десктоп) |
| **П1** подписки | миграция `Sys_UserPushSubscriptions` + настройки, API §6, переключатель «Reminders» в меню, удаление умерших (410) | вкл/выкл на двух устройствах, «Send test» доходит на оба, отозвал разрешение → строка ушла после первой отправки |
| **П2** напоминания | правило §7 + тесты, `Sys_ReminderDeliveries`, джоб под `PushRemindersEnabled`, карточка `/Admin/Jobs` + `docs/admin-pages/jobs.md` | на тест-группе `utest` приходит одно напоминание, рестарт API дубля не даёт, ProdAdmin не шлёт |
| **П3** настройки и iPhone | время напоминания, «Remind me» на группу, справка про экран «Домой» | utest-персонаж видит подсказку на iPhone, отключённая группа не напоминает |
| **П4** запасной канал и отмены | письмо, если push-подписок нет или они умерли (по настройке); мгновенный push «Training cancelled» при отмене тренером (событие, а не расписание) | письмо пришло пользователю без подписки; отмена → push участникам, ответившим Yes |
| **П5** (по желанию) | кнопки «Going / Not going» в уведомлении — только Android | ответ из уведомления виден в «Who's coming» |

Объём: П0–П2 — ~3–4 дня, П3–П4 — ~2 дня.

## 10. Footguns (известные заранее)

- **ProdAdmin пишет в прод-базу** — джоб там выключен флагом, а дубль страхует UNIQUE (§4).
- **Локальная база с подписками** — никогда не слать «всем» из локальной разработки.
  Тестировать на себе (`/api/push/test`) и на персонажах `utest-*` с их собственными подписками.
- **Смена VAPID-ключей** = все подписки мертвы, все должны включить заново.
- **iOS:** push без показанного уведомления → подписку отзовут; разрешение только по нажатию.
- **Время** — всё «когда» в местном времени Израиля (как в `GroupTrainingSchedule`); на
  переходе на летнее время окно не должно ни пропустить, ни задвоить (тест).
- **Ранние тренировки** (5:30): «за 2 часа» = 3:30 ночи. См. §11 В5.
- **Чистые URL** — `/sw.js` и `/manifest.json` не должны попасть под rewrite.

## 11. Решения (20.10.2026 Влад принял все рекомендации)

| # | Вопрос | Решено |
|---|---|---|
| В1 | За сколько напоминать по умолчанию? | за 2 часа; человек меняет: выкл / 1 ч / 2 ч / вечером накануне в 20:00 |
| В2 | Кому: всем активным, кроме ответивших «No», или только ответившим «Yes»? | всем активным, кроме «No» и «On break»; без ответа — текст «Are you coming?» |
| В3 | Слать ли мгновенный push при отмене тренировки? | да, в П4, только ответившим Yes или Maybe |
| В4 | Запасной канал (письмо) — по умолчанию вкл или выкл? | выкл, человек включает сам |
| В5 | Тихие часы: если время напоминания попало на 22:00–07:00? | перенести на 21:00 накануне |
| В6 | Включать ли напоминания по умолчанию тем, кто разрешил push? | да — разрешение и есть согласие; отключение в один клик |
| В7 | Тренеру — сводка «завтра идут N человек»? | не сейчас, отдельной задачей после П2 |

Ещё два решения 20.10: в этом заходе — П0+П1+П2; UI без хендоффа — скромно из того, что есть
(каркас и карточки My favorites, `deep-seg`), хендофф потом наложим.

## 12. Что сделано 20.10.2026 (П0–П2) и как устроено

| Часть | Где |
|---|---|
| Таблицы (миграция `AddWebPushReminders`, применена локально) | `Sys_UserPushSubscriptions` (UNIQUE Endpoint), `Sys_UserNotificationSettings` (CHECK LeadKind), `Sys_HubGroupReminderMutes` (под П3), `Sys_ReminderDeliveries` (UNIQUE `UX_Sys_ReminderDeliveries_Session`). Сущности — `Swimm.Domain/Entities/{UserPushSubscription,UserNotificationSettings,HubGroupReminderMute,ReminderDelivery}.cs`, конфиги — `Data/Configurations/PushConfigurations.cs`. В seed-tables — в исключениях |
| Правило «кому и когда» | `Swimm.Application/Mapping/TrainingReminderRules.cs` (`DueAt`, `UpcomingStarts`, `Plan`, `Compose`); тесты — `TrainingReminderRulesTests` |
| Правила подписок | `PushRules.cs`: белый список хостов push-служб (FCM, Apple, Mozilla, WNS; https), base64url ключей, подпись устройства из User-Agent, не больше 10 устройств; тесты — `PushRulesTests` |
| Отправка | `IPushSender` → `WebPushSender` (NuGet **Lib.Net.Http.WebPush 3.3.1** — выбран в П0: живой, aes128gcm без BouncyCastle на net5+). Конфиг `WebPush:*`; генерация пары — CLI `--generate-vapid-keys` (`Cli/PushCli.cs`) |
| Подписки и настройки | `IPushSubscriptionService` → `PushSubscriptionService`; тесты — `PushSubscriptionServiceTests` |
| Прогон напоминаний | `ITrainingReminderService` → `TrainingReminderService`: строка журнала ДО отправки, UNIQUE отсекает дубль; 404/410 удаляет устройство; журнал старше 30 дней чистится. Postgres-тест — `WebPushPgTests` (два прогона = одна отправка, UNIQUE, CHECK) |
| Джоб | `TrainingReminderBackgroundService`, тик 5 мин; флаг `PushReminders:Enabled` (`HostingMode.PushRemindersEnabled`): `appsettings.Production.json` — true, `appsettings.ProdAdmin.json` — false явно. Карточка `/Admin/Jobs` — `BackgroundJobsStatusService.BuildTrainingReminders`; новый `IJobRunStore.RecordStateAsync` — тик без строки истории |
| API | `PushController`: `GET /api/push/vapid-public-key`, `POST/DELETE /api/push/subscriptions`, `DELETE /api/push/devices/{id}`, `POST /api/push/test`, `GET/PUT /api/me/notification-settings`, `PUT /api/me/notification-settings/groups/{id}` (П3). Rate limit `push` (10/мин на мутации) |
| Клиент | `client/public/sw.js`; `src/utils/push.ts` (поддержка, iOS без экрана «Домой», подписка); страница `/reminders` (`reminders.html`, `src/pages/reminders-page.tsx`, `src/projects/reminders-project/`); пункт **Reminders** в меню пользователя (`app-topbar.tsx` и `home-header.tsx`, десктоп и мобильное); маршрут — три зеркала; `robots.txt` закрывает `/reminders`; `manifest.json` — `id`, `scope` |
| `/sw.js` на сервере | `Program.cs` → `UseStaticFiles`: `Cache-Control: no-cache`, `text/javascript`; rewrite чистых URL его не трогает (есть расширение) |

Как устроено и почему так (не переизобретать):
- **Окна «±5 минут» нет.** Напоминание уходит, когда его время НАСТУПИЛО и занятие ещё не
  началось; повтор отсекает журнал. Пропущенный тик (деплой) не теряет напоминание.
- **Тихие часы** (В5): время, попавшее в 22:00–07:00, переносится на 21:00 вечера перед ними
  (04:00 → 21:00 накануне, 22:30 → 21:00 того же дня). Опоздавшее из-за простоя напоминание
  ночью не шлётся — ждёт 07:00, если занятие ещё впереди.
- «Maybe» напоминается как «Yes» (с хвостом «You said maybe»), без ответа — «Are you coming?».
- **Service worker регистрируется только со страницы Reminders**: он один на сайт (scope `/`)
  и после регистрации получает push на любой странице, так что 16 точек входа не трогали.
- **Чужой браузер.** Подписка привязана к браузеру; вошёл другой человек и открыл Reminders —
  строка тихо переезжает к нему (upsert по endpoint). При выходе из аккаунта подписка НЕ
  снимается — хвост П3 (снять на «Sign out», если Влад решит).
- Адрес подписки — полусекрет: в URL и в лог не пишется (заголовок `X-Push-Endpoint`).
- Шаблон адреса в уведомлении — `/groups/{slug}?tab=trainings` (зеркало `routes.group`).

Хвосты (не сделано): maskable-иконка в `manifest.json` (нужна картинка); справка
`/help/reminders-iphone` (П3 — пока подсказка прямо на странице); «Remind me» в табе Trainings
(П3 — API готово); письмо и «Training cancelled» (П4); `pushsubscriptionchange` в `sw.js` (подписка,
которую браузер сменил сам, — пока ловится повторным открытием Reminders).

## 13. Источники

- iOS: требования к web push — https://pushpad.xyz/blog/ios-special-requirements-for-web-push-notifications ,
  ограничения PWA — https://www.magicbell.com/blog/pwa-ios-limitations-safari-support-complete-guide
- Azure SMS (почему не SMS) — https://learn.microsoft.com/en-us/azure/communication-services/concepts/sms/messaging-connect ,
  тариф Twilio в Израиль — https://www.twilio.com/en-us/sms/pricing/il
