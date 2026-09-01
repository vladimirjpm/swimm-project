# My media (`media.html`) — справочник

> **При любом баге добавления/показа/публикации медиа — читай этот файл сначала.**

Медиа на эстафетах (кто владелец строки при `RelayId != null`, как ищется участие через
`RelayMembers`) — см. `docs/relays.md`. Модель видимости (private/members/public,
модерация, авто-approve) не переписывается здесь — см. `docs/favorites-media-phase2-design.md`
и `docs/hubgroups-architecture.md`.

## 1. Что это

`My media` — личный кабинет пользователя по всем ссылкам на видео/фото, которые он
привязал к своим избранным пловцам: добавление ссылок (YouTube/Vimeo/фото), привязка к
конкретному заплыву или соревнованию, публикация в группы (HubGroups) с модерацией,
реакции (❤ на медиа, 🎉 на заплыв). Owner-only: юзер видит и правит только своё медиа
(`Sys_UserMedia.UserId`).

## 2. Точки входа и навигация

```
client/media.html → client/src/pages/media-page.tsx → MyMedia (my-media.tsx)
```

`MyMedia` (my-media.tsx:23) — гейт авторизации: пока `auth.loading` — пустой экран;
неавторизованному — заглушка с кнопкой Sign in; иначе рендерит `MyMediaContent` —
собственно страницу. Весь стейт живёт локально в `MyMediaContent` (useState/useMemo),
никакого Redux — фича не завязана на `store/store.ts`. Тёмная палитра в стиле
`groups.html`/`home.html` — осознанное решение (см. §9 Footguns).

Ключевые файлы клиента:
- `client/src/projects/my-media-project/my-media.tsx` — оркестратор экрана: фильтры,
  модалы, реакции, share/withdraw, добавление/удаление.
- `client/src/projects/my-media-project/components/add-link-modal.tsx` — попап добавления
  ссылки (3 шага/single-step, см. §5).
- `client/src/projects/my-media-project/components/swim-list.tsx` — список заплывов,
  сгруппированный по соревнованиям (`CompetitionGroup` → `MySwimRow` → развёрнутая
  `MediaLine` с inline share).
- `client/src/projects/my-media-project/components/media-card.tsx` — карточка
  «Unlinked media» (медиа без привязки к заплыву).
- `client/src/projects/my-media-project/components/moderation-panel.tsx` — вкладка
  Moderation (не описана подробно здесь — детали в hubgroups-architecture.md).
- `client/src/projects/my-media-project/components/status-styles.ts` — `derivedCardStatus`,
  `STATUS_COLORS`, общие CSS-классы чипов/сегментов.
- `client/src/projects/my-media-project/use-all-my-media.ts` — хук `useAllMyMedia`
  (список + add/remove) + standalone `addUserMedia()` + пикеры `fetchSwimmerCompetitionsBrief`/
  `fetchSwimmerResultsBrief`/`fetchPublishTargets`.
- `client/src/projects/my-media-project/use-my-swims.ts` — хук `useMySwims` (агрегат
  `GET /api/me/swims`) + `toggleLike`/`toggleCheer`.
- `client/src/hooks/useUserMedia.ts` — более старый хук `useUserMedia(swimmerId)`
  (личное медиа одного пловца — используется на карточке пловца/результатов, НЕ на
  странице My media) + `useMyMediaPublications` (публикации — переиспользуется my-media.tsx).
- `client/src/hooks/useCompetitionMedia.ts` — публично видимое медиа заплывов
  соревнования/события (`GET /api/media/results`) — для показа иконок видео в общей
  таблице результатов; к странице My media отношения не имеет напрямую, но источник
  данных тот же `Sys_UserMedia`.

## 3. Модель данных `Sys_UserMedia`

Сущность `UserMedia` (`server/Swimm.Domain/Entities`), маппинг —
`server/Swimm.Infrastructure/Data/SwimmDbContext.cs` (~строка 538). Приватная таблица,
без grant `swimm_ro` — читается только через RW-контекст `SwimmDbContext`.

| Поле | Тип/CHECK | Смысл |
|---|---|---|
| `Id` | PK | |
| `UserId` | FK → Users, cascade | владелец (owner-only) |
| `SwimmerId` | FK → Swimmers, cascade | чьё медиа |
| `Level` | `CK_UserMedia_Level`: `swimmer\|competition\|result` | уровень привязки — **выводится сервером**, не принимается от клиента |
| `Visibility` | `CK_UserMedia_Visibility`: `private\|public` | на создании всегда `private`; публичность идёт через отдельные `Sys_UserMediaPublications`, не через это поле напрямую в текущем потоке 2A |
| `MediaType` | `image\|video` | |
| `SourceType` | `youtube\|vimeo\|other` | |
| `Url` | до 1000 символов, `https://` | |
| `ResultId` | FK → Results, `Restrict`, nullable | задан только при `Level=result` |
| `CompetitionId` | FK → Competitions, `Restrict`, nullable | задан при `Level=competition` ИЛИ денормализован при `Level=result` (берётся из заплыва) |
| `CreatedAt` | UTC | |
| `LinkCheckedAt`/`LinkOk`/`LinkStatusCode`/`LinkError` | nullable, миграция `AddUserMediaLinkHealth` | здоровье ссылки — см. §8 |

Кто выводит `Level`: `UserMediaRepository.AddAsync` (`server/Swimm.Infrastructure/Repositories/UserMediaRepository.cs:91`) —
`ResultId` задан → `"result"` (и `CompetitionId` берётся из самого заплыва, клиентский
`competition_id` в этом случае игнорируется); иначе `CompetitionId` задан → `"competition"`;
иначе → `"swimmer"` (общее/unlinked медиа пловца). Клиент никогда не передаёт `level` —
только `swimmer_id`/`result_id`/`competition_id` (см. `AddUserMediaRequest` в
`server/Swimm.Application/Dtos/UserMediaDtos.cs`).

## 4. API

Все под `[Authorize]` + `[AutoValidateAntiforgeryToken]` в
`server/Swimm.API/Controllers/MediaController.cs` (маршрут-база `api/me/media`, часть
эндпоинтов через явный `[HttpGet("/api/me/swims")]`/`[HttpGet("/api/me/moderation/media")]`
переопределяет базовый префикс).

| Метод | Путь | Назначение | Заметки |
|---|---|---|---|
| GET | `/api/me/media?swimmerId=` | список медиа юзера (опц. фильтр по пловцу) | `IUserMediaRepository.GetForUserAsync` |
| GET | `/api/me/swims?season=` | агрегат My media v3 (заплывы сезона + медиа + реакции) | `IMySwimsRepository.GetMySwimsAsync`; сезон вне `[1990,2100]` сбрасывается на текущий |
| POST | `/api/me/media` | добавить медиа | `[EnableRateLimiting("media")]`; 400 если `swimmer_id<=0`; 400 при достижении `MaxMediaPerUser=500`; валидация `MediaUrlValidator.TryValidate` (media_type/source_type/https/regex youtube-vimeo) |
| DELETE | `/api/me/media/{id}` | удалить своё медиа | IDOR-проверка по `userId` в репозитории |
| GET | `/api/me/media/publications` | статусы заявок на публикацию всех моих медиа | |
| GET | `/api/me/moderation/media` | сводный inbox модерации по группам, где я владелец/админ (site admin — все) | `IUserMediaPublicationService.GetModerationFeedAsync` |
| GET | `/api/me/media/{id}/publish-targets` | в какие группы можно подать это медиа | пловец должен быть в ростере группы + я член |
| POST | `/api/me/media/{id}/publications` | подать заявку (`{hub_group_id, level}`) | `[EnableRateLimiting("media")]`; привилегия (владелец/админ группы/site-admin) → авто-`approved`; при авто-approve — `ICacheService.InvalidateAllAsync()` |
| DELETE | `/api/me/media/{id}/publications/{hubGroupId}` | отозвать заявку (любой статус) | сбрасывает кэш (публичная витрина группы могла показывать это медиа) |

Смежные, но не в `MediaController`:
- `GET /api/swimmers/{id}/competitions-brief`, `GET /api/swimmers/{id}/results-brief?competitionId=` —
  пикер шага 3 Add link (`UserMediaRepository.GetSwimmerCompetitionsBriefAsync`/`GetSwimmerResultsBriefAsync`).
- `POST/DELETE /api/media/{id}/like`, `POST/DELETE /api/results/{id}/cheer` — реакции,
  политика `[EnableRateLimiting("reactions")]` (60/мин по userId, см. `Program.cs`).
- `GET /api/media/results?competitionId=|eventId=|group=` — публично видимое медиа
  результатов (свой + одобренные публикации), использует `useCompetitionMedia`.
- `POST /api/admin/media/check-links` — админ-эндпоинт здоровья ссылок, см. §8.

**Rate limit / потолок** (`server/Swimm.API/Program.cs`, политика `"media"`): 20
запросов/мин, партиционирование по `userId` (fallback IP), `QueueLimit=0` — лишнее сразу
429. `MaxMediaPerUser = 500` — константа в `MediaController`, проверяется на добавлении
(`_media.CountForUserAsync(userId) >= MaxMediaPerUser` → 400). Обе меры — анти-спам,
введены коммитом `5ed1734`.

## 5. Потоки UI: Add link (`add-link-modal.tsx`)

Один компонент, два режима, различаются наличием `fixedResultId`/`fixedCompetitionId`:

**Глобальный «+ Add link» (3 шага)** — вызывается кнопкой в шапке чипов/floating-кнопкой
mobile; `swimmers` = полный список (favorites + «has media»), без фиксированной цели.
1. **URL** — вставить ссылку; авто-детект `YOUTUBE`/`VIMEO`/`PHOTO`/`OTHER` по regex/расширению
   (`detectType`); для `OTHER` — переключатель Video/Photo вручную.
2. **Пловец** — из списка `swimmers` (props), один выбор.
3. **Соревнование → заплыв** (опционально) — подгружает `fetchSwimmerCompetitionsBrief`
   по выбору пловца, затем `fetchSwimmerResultsBrief` по выбору соревнования; три варианта
   сохранения: `Save` (привязка к заплыву, `result_id`), `📎 Whole competition`
   (`competition_id`, доступно если выбрано соревнование, но не заплыв), `Skip — save as
   general` (ни то ни другое → `Level="swimmer"`, попадает в Unlinked media).

**Single-step** (`fixedResultId`/`fixedCompetitionId` заданы, `contextLabel` — карточка
контекста над инпутом) — цель уже известна, шаги 2–3 пропускаются, форма сразу на шаге 1
с кнопкой `Save`. Три места вызова в `my-media.tsx`:
- `addVideoSwim` — «+ Add video» на строке заплыва (`fixedResultId`, `swimmers` = массив
  из одного пловца заплыва).
- `addCompTarget` — «+ Photo/Video» в шапке группы соревнования (`fixedCompetitionId`).
- `linkSwimTarget` — «Link to a swim →» на карточке Unlinked media, стартует с `initialStep={3}`
  (это НЕ single-step в смысле `fixedResultId`, а обычный 3-шаговый флоу с урезанным
  списком пловцов = {1 пловец} и `initialUrl` заранее заполненным).

**«Link to a swim» = footgun.** PATCH-а на медиа нет — привязка существующего Unlinked
медиа к заплыву реализована как **delete+recreate** (`my-media.tsx`, блок `linkSwimTarget`,
`onSave`): сначала `add({...input, swimmer_id})` создаёт новую строку с `result_id`, и
только при успехе — `remove(linkSwimTarget.id)` удаляет старую. Побочный эффект: у
«привязанного» медиа новый `Id` и новый `CreatedAt`, а любые публикации
(`Sys_UserMediaPublications`), которые ссылались на старый `UserMediaId` через
`OnDelete: Cascade`, **теряются** — юзеру придётся подавать заявку на публикацию заново.

## 6. My swims (v3)

Агрегат собирает `MySwimsRepository.GetMySwimsAsync` (`server/Swimm.Infrastructure/Repositories/MySwimsRepository.cs`):

1. Избранные пловцы юзера (`UserFavorites`, `TargetType=swimmer`) — чипы фильтра,
   primary первым.
2. Сезон = сентябрь–август (`SeasonOf`: месяц ≥ 9 → тот же год, иначе год-1); список
   доступных сезонов для селектора собирается отдельным запросом по годам/месяцам
   результатов.
3. Заплывы сезона по `swimmerIds` **ИЛИ** участию в эстафете через `RelayMembers`
   (строка эстафеты принадлежит владельцу + всем ногам — см. `docs/relays.md`).
   `MemberSwimmerIds` донасыщается отдельным запросом — нужен клиенту для чип-фильтра
   («заплыв принадлежит пловцу, если он владелец строки ИЛИ нога эстафеты», `swimBelongsTo`
   в `my-media.tsx`).
4. **PB (personal best)**: лучшее время пловца за всё время по (стиль, дистанция) на
   индивидуальных незасчитанных-DSQ заплывах, сравнивается с временем сезонного заплыва —
   совпадение → `IsPb=true` (значок ⚡). Комментарий в коде явно фиксирует урок перфа:
   фильтры только по Id/датам, никаких JOIN по `.Name`.
5. Всё медиа юзера — одна выборка, затем раскладка по `Level`: `result` → в `swims[i].Media`
   (`ToLookup` по `ResultId`), `competition` → `CompetitionMedia`, `swimmer` → `UnlinkedMedia`.
6. Реакции: ❤ (`Sys_UserReactions`, `Kind="like"`, `MediaId`) по медиа юзера, 🎉
   (`Kind="congrats"`, `ResultId`) по заплывам сезона — групповые счётчики + флаг «мой».

На клиенте `my-media.tsx` держит **оптимистичные оверрайды** поверх ответа `/api/me/swims`
(`likeOverrides`/`cheerOverrides`, Map по id) — тоггл сразу меняет счётчик локально, сервер
подтверждает/поправляет асинхронно (`toggleLike`/`toggleCheer` из `use-my-swims.ts`,
идемпотентные, возвращают итоговый `{count, mine}`).

## 7. Публикации в группы + модерация

Заявка на публикацию (`level: members|public`) идёт через `POST /api/me/media/{id}/publications` —
если у подающего есть привилегия в целевой группе (владелец/админ группы/site-admin),
заявка сразу `approved` и сбрасывает общий кэш (публичная витрина группы читает
Publications через кэшируемый payload). Смена уровня уже поданной pending/approved
публикации не поддержана напрямую — клиент (`handlePublish` в `my-media.tsx`) сначала
делает `withdraw`, затем подаёт заново с новым `level`. Полная модель ролей/статусов —
в `docs/favorites-media-phase2-design.md` и `docs/hubgroups-architecture.md`, здесь не
дублируется.

Вкладка **Moderation** на странице (видна только если `auth.isAdmin` или юзер состоит
хотя бы в одной группе, `showModeration` в `my-media.tsx`) — сводный inbox
`GET /api/me/moderation/media` по всем группам, где юзер имеет право решать; сами решения
идут через существующий `POST /api/hub-groups/{id}/media/publications/{pubId}/decision`
(вызывается из `ModerationPanel` — не описан подробно, деталь в hubgroups-architecture.md).

## 8. Здоровье ссылок

Внешние ссылки (YouTube/Vimeo/фото) со временем умирают. Проверка живости — админский
инструмент, не часть публичной страницы My media: `docs/admin-pages/media.md`
(`/Admin/Media`, `POST /api/admin/media/check-links`, `IUserMediaLinkChecker`). Поля
`LinkCheckedAt/LinkOk/LinkStatusCode/LinkError` живут в той же таблице `Sys_UserMedia`,
что описана в §3, но заполняются только on-demand по кнопке — не при каждом чтении.
Карточка «Broken media links» на `/Admin` (дашборд «здоровье данных») ссылается на эту
страницу.

## 9. Footguns

- **«Link to a swim» = delete+recreate**, не PATCH — теряются `Id`, `CreatedAt` и любые
  публикации медиа (см. §5). Если баг про «пропала публикация после привязки к заплыву» —
  смотреть сюда в первую очередь.
- **`Level` выводит сервер**, клиенту не доверяем (`UserMediaRepository.AddAsync`) — если
  на клиенте что-то не так с уровнем/группировкой, проверяй именно серверную логику вывода,
  а не то, что «передал клиент».
- **AddLinkModal — фиксированная тёмная палитра** (жёсткие hex вроде `#0e2138`,
  `#7dd3fc`), НЕ через `var(--theme-mode-*)` — сознательное решение (см. комментарий в
  `my-media.tsx`: «Тёмный стиль groups.html/home.html»). Не пытайся «зачинить» под
  light/dark парными токенами — это ломает дизайн-решение, не баг.
- **Antiforgery обязателен** на всех мутациях (`[AutoValidateAntiforgeryToken]` на
  контроллере) — токен кэшируется в модуле (`cachedToken` в `use-all-my-media.ts` /
  `use-my-swims.ts` / `useUserMedia.ts`, каждый файл держит СВОЙ кэш, не общий!) и
  инвалидируется при 401/403 ответе.
- **Rate limit/потолок**: политика `"media"` — 20 запросов/мин по userId на добавление
  медиа и подачу публикаций; `MaxMediaPerUser=500` — жёсткий потолок строк на юзера.
  При тестировании массового добавления (сиды, e2e) легко упереться в оба лимита.
- **Эстафеты**: заплыв-эстафета «принадлежит» и владельцу строки, и всем участникам через
  `RelayMembers`, но медиа физически привязано к `ResultId` — т.е. к конкретной ноге
  (владельцу строки). Чтобы карточка ноги-не-владельца всё равно показывала это медиа,
  `GetForUserAsync` расширяет выборку по членству в `RelayMembers` (см. `docs/relays.md`,
  раздел «Баг: медиа эстафеты не видно ноге-не-владельцу»). Любой новый путь показа медиа
  эстафеты обязан повторить эту логику членства, иначе видео увидит только владелец строки.
- **Два независимых хука личного медиа**: `useUserMedia(swimmerId)` (`hooks/useUserMedia.ts`,
  для карточки пловца/результатов) и `useAllMyMedia()` (`my-media-project/use-all-my-media.ts`,
  для страницы My media) читают/пишут одну и ту же таблицу через разные эндпоинты-обёртки
  над одним контроллером, но не шарят состояние — обновление в одном месте не отразится
  в другом без ручного `reload()`.
- **`useMySwims` перезагружает весь агрегат** после любого add/remove (`reload()` в
  `my-media.tsx`) — точечный merge сознательно не сделан (комментарий в коде: «агрегат
  дешёвый»), при росте объёма данных это может стать узким местом.

## 10. Журнал ключевых коммитов

| Коммит | Что сделал |
|---|---|
| `2ad8b5d` | Media 2A: личное owner-only медиа пловца (`api/me/media` + «Мои ссылки») — база текущей модели |
| `4a966a6` | Media 2A: привязка личного видео к заплыву — `result_id` в API + UI карточки пловца |
| `4abadaa` | Media этап 2: публикации личного медиа в группы (модель + API) |
| `938d3c1`/`14b3255` | My media: первая версия сводной страницы администрирования медиа (`media.html`) |
| `7e19506`/`b0e3d8c`/`e4c795d` | Design brief + серверные ручки + вёрстка My media v2 по дизайн-хендофу |
| `9ce9bc1` | Селектор подачи предлагает только допустимые группы (`publish-targets`) |
| `95fc28c` | `UserMediaDto`: `swimmer_name`+`result_label` для сводной страницы |
| `590da8a` | Media этап 3/4: подача в группу из «Моих ссылок» + иконки видео в общей таблице результатов |
| `aead7f9` | Страница группы: published-медиа участников + inbox модерации |
| `745c0ef` | Тесты публикаций личного медиа — сценарий Влада + матрица правил (15 тестов) |
| `73f7f99` | Одобренные public-публикации показываются в публичной галерее группы |
| `47096ac` | Сервер: бэкенд My media v3 — `/api/me/swims` + реакции ❤/🎉 |
| `c60796d` | Клиент: My media v3 swim-centric — заплывы по сезонам + реакции (нынешняя структура my-media.tsx) |
| `575d643` | Клиент: убрать добавление медиа с публичных страниц (карточка пловца) |
| `a944147` | Клиент: иконка стиля заплыва в My media вместо текста |
| `5c8ddad` | Админ 7.5: проверка живости ссылок `Sys_UserMedia` + карточка на дашборде (см. §8) |
| `12b4e51` | Add video со страницы результатов: тумблер + попап на строке заплыва |
| `5ed1734` | Анти-спам медиа: rate limit 20/мин на юзера + потолок 500 медиа (см. §4/§9) |
