# Favorites/Media — Phase 2 (media): дизайн-документ

Статус: **фаза закрыта целиком** (сверено 2026-09-01). 2B′ реализован 2026-07-14 (§10),
2A — позже, страницей My media v3; что и где — в §11. Публичный UGC-слой (старый 2B)
отменён решением Влада — вместо него group-scoped members-слой. Предшественник: Phase 1
(favorites) сделан и живёт.

⚠ Документ — **дизайнерский**: он объясняет модель видимости и почему она такая. Как
подсистема устроена СЕЙЧАС (таблицы, API, потоки, footguns) — в
[`media-page.md`](media-page.md); правило про недоверенный primary — `rule-primary-favorite-untrusted`.

## 0. Финальная модель (обсуждение Влад ↔ Fable, 2026-07-14)

Вместо «private → public с модерацией» — «private → group-scoped». Матрица:

| Кто добавил | Привязка | Кто видит |
|---|---|---|
| Обычный юзер (в т.ч. «я = пловец» через primary) | пловец/заплыв (Sys_UserMedia, 2A) | только он сам |
| Управляющий **официальной** группы (CanEdit) | группа / пловец+заплыв (Sys_HubGroupMedia) | активные user-члены группы |

Ключевые решения:
- **«Тренер» — ярлык, не носитель прав** (Role='coach' в watchlist — только отображение).
  Права мутаций медиа = CanEdit (владелец/админ группы/site-админ); видимость = активное
  членство в Sys_HubGroupUserMembers. Выписали из группы → мгновенно нет доступа.
- **Групповое медиа принадлежит группе**, не автору: уволенный тренер не уносит контент;
  CreatedByUserId — только аудит.
- **Публичного UGC нет вообще**: у HubGroupMedia.Visibility только public (витрина группы,
  как раньше) | members (разборы). Значения 'public-to-world for anchored media' не существует.
- **Якорь на персону (SwimmerId/ResultId) — только members** (check-constraint): публично
  вешать медиа на пловца нельзя. Официальность отвечает за поведение группы целиком
  (watchlist-состав приемлем: аудитория — только члены, а официальность можно отозвать).
- **Members-слой — только официальные группы** (валидация в сервисе).
- Эстафетные Result не якорятся (строка закреплена за одним пловцом — разбор ушёл бы не тому).

## 1. Что уже есть (не переделывать)

- **Таблица `Sys_UserMedia`** создана (миграция `AddUserFavoritesAndUserMedia`), entity
  `Swimm.Domain/Entities/UserMedia.cs`. Поля: `UserId`, `SwimmerId` (обязателен — рычаг «стереть
  всё медиа по пловцу»), `ResultId?`, `CompetitionId?`, `Level` (swimmer/competition/result),
  `MediaType` (image/video), `SourceType` (youtube/vimeo/other), `Url` (≤1000), `Visibility`
  (private/public, дефолт private), `CreatedAt`.
- **Эндпоинтов media НЕТ** (нет MediaController, нет репозитория) — намеренно, Phase 1 завёл
  только таблицу.
- **Эталон паттерна — `FavoritesController`**: `[Route("api/me/favorites")]`, `[Authorize]`,
  `[AutoValidateAntiforgeryToken]`, `CurrentUserId()` из `ClaimTypes.NameIdentifier`, все
  операции scoped по `userId` (IDOR-защита), репозиторий `IUserFavoriteRepository` →
  `Sys_*` только через `SwimmDbContext` (роль `swimm_rw`), в RO-грант НЕ добавлять.
- **Sys_UserMedia в RO-грант НЕ входит** (личные данные) — как favorites. Публичный показ
  (если будет) читается через `SwimmDbContext`, а не `SwimmReadDbContext`.

## 2. Почему media отложили (threat model)

Media = **произвольный публичный UGC от любого залогиненного юзера**. Риски, которых нет у favorites:
- **XSS/clickjacking через embed**: сырой `Url` в `<iframe>`/`<img>` = дыра. Нельзя рендерить
  произвольный URL.
- **SSRF/безопасность**: URL от пользователя не резолвим на сервере (не фетчим превью по нему).
- **Абьюз/спам**: массовое добавление, ссылки на чужой/незаконный контент.
- **Модерация/ответственность**: публичный показ = сайт отвечает за то, что видно другим.
- **Приватность**: дефолт private; публикация — осознанное действие.

Отсюда — фазирование: сначала минимальная поверхность с нулевым публичным риском, потом
публичный слой с sandbox+модерацией.

## 3. Эндпоинты (MediaController, `api/me/media`)

Зеркалит FavoritesController (auth + antiforgery + owner-scope). Репозиторий `IUserMediaRepository`.

| Метод | Роут | Назначение |
|---|---|---|
| GET | `/api/me/media?swimmerId=&competitionId=&resultId=` | Мои медиа (все мои, опц. фильтр по привязке) |
| POST | `/api/me/media` | Добавить ссылку. Валидация: level↔id-поля, sourceType∈allowlist, URL по паттерну провайдера, извлечь и хранить канонический id (см. §4) |
| PUT | `/api/me/media/{id}` | Правка (напр. Visibility private↔public, подпись) — owner-scoped |
| DELETE | `/api/me/media/{id}` | Удалить — owner-scoped |

Публичный показ (только если выбран объём с public, §8):
| GET | `/api/media?swimmerId=&competitionId=&resultId=` | Только `Visibility='public'`, любого автора. Кэш+ETag как другие публичные GET. |

Валидация входа (в контроллере, 400):
- `Level` ∈ {swimmer, competition, result}; соответствующий id обязателен (result→ResultId,
  competition→CompetitionId, swimmer→только SwimmerId). `SwimmerId` обязателен ВСЕГДА.
- `MediaType` ∈ {image, video}; `SourceType` ∈ allowlist (§4).
- `Url` проходит паттерн провайдера, из него извлекается id; иначе 400 (не храним сырой мусор).
- `Visibility` ∈ {private, public}, дефолт private.

## 4. Sandbox / allowlist embed (ключевая защита)

**Правило: никогда не рендерим сырой `Url` в iframe/img.** Вместо этого:
- **Allowlist провайдеров** (`SourceType`): `youtube`, `vimeo` для video; для image — на старте
  БЕЗ произвольных URL (или узкий allowlist доверенных хостов; произвольный `<img src>` = трекинг/
  mixed-content, решаем отдельно). `other` — НЕ рендерим как embed, максимум показываем как
  внешнюю ссылку с `rel="noopener noreferrer nofollow"` и явным «внешняя ссылка».
- На вводе **извлекаем канонический id** (youtube videoId, vimeo id) регэкспом из URL и храним
  и `Url`, и распарсенный id. Рендер — из id по фиксированному шаблону embed
  (`https://www.youtube-nocookie.com/embed/{id}`, `https://player.vimeo.com/video/{id}`), НЕ из
  пользовательского URL. Это отсекает инъекции параметров и произвольные хосты.
- **iframe с `sandbox`** (`allow-scripts allow-same-origin` минимально нужное для плеера) +
  ограниченный `allow`. Youtube — nocookie-домен.
- **CSP**: `frame-src` ограничить youtube-nocookie.com + player.vimeo.com; никаких `*`.

## 5. Приватность

- Дефолт `private` — видит только владелец (GET `/api/me/media`).
- `public` — осознанный переход (PUT visibility). Виден в `/api/media` всем.
- Даже public-медиа НЕ даёт прав и НЕ связывает аккаунт с пловцом (правило
  `rule-primary-favorite-untrusted`): это «подборка ссылок к пловцу от юзера X», не заявление
  «это мои фото официально».
- Существующие `Gallery`/`GalleryItem` НЕ переиспользуем (это медиа заплыва из импорта, без
  владельца/приватности) — отдельная сущность.

## 6. Rate-limit + модерация (только для public-объёма)

- **Rate-limit** на POST `/api/me/media` (напр. N добавлений в час на юзера) — тот же механизм,
  что на auth-эндпоинтах.
- **Модерация**: таблица жалоб `Sys_UserMediaReports` (кто/на что/причина); админ-экран
  Review/Takedown; снятие = `Visibility='removed'` (не hard-delete — аудит). Автору — уведомление
  опционально.
- **Мягкий старт**: можно открыть public с флагом настройки (как `HubGroupVisibility`), чтобы
  включать/выключать публичный слой без деплоя.

## 7. Тесты (обязательно, по образцу Swimm.Tests)

- IDOR: чужое media не читается/не правится/не удаляется (scoped по userId).
- Валидация level↔id, sourceType allowlist, извлечение id из разных форм URL (youtube watch?v=,
  youtu.be/, shorts/; vimeo /id) + отказ на мусорном URL.
- Visibility: дефолт private; public виден в `/api/media`, private — нет.
- Каскад «стереть всё по пловцу» через SwimmerId.
- (public-объём) rate-limit срабатывает; report→takedown прячет из `/api/media`.

## 8. Развилка объёма (решение Влада — принято «сначала спроектировать»)

Рекомендую **фазировать внутри Phase 2** (частями, а не всё сразу):

- **2A — private-only slice (рекомендую первым):** MediaController CRUD, `Visibility` всегда
  private (public в API отклоняем 400), клиент — добавить/удалить ссылку в карточке пловца/
  заплыва, рендер embed через sandbox (§4) только владельцу. Нулевой публичный риск, даёт
  сразу ценность (личная подборка), обкатывает embed-sandbox и извлечение id. Модерация/
  rate-limit НЕ нужны (никто, кроме владельца, не видит).
- **2B — public + модерация (после обкатки 2A):** открыть `Visibility='public'`, `/api/media`,
  rate-limit, `Sys_UserMediaReports` + админ-review, CSP/feature-flag. Полноценный UGC.

Плюс фазирования: 2A маленький и безопасный, можно даже делегировать Сонету по чёткому таску;
2B — тяжёлый и security-sensitive, проектируем/делаем сами, с ревью.

## 9. Открытые вопросы к Владу (до старта кода)

1. **image-media**: на старте разрешаем произвольные image-URL (`<img src>`) или только
   video-embed (youtube/vimeo), а картинки — позже? (произвольный `<img>` = трекинг/mixed-content).
2. **2A или сразу 2A+2B?** Рекомендация — 2A первым.
3. **Публичный показ media** — где именно в UI (карточка пловца? заплыв? отдельная галерея на
   странице спортсмена)? Влияет на публичные GET и клиент.
4. **Модерация** (для 2B): ты единственный админ вручную, или нужен поток жалоб от юзеров?

## 10. Реализация 2B′ (2026-07-14, Fable)

Сервер:
- `HubGroupMedia`: + `Visibility` (public|members, дефолт public, бэкфилл существующих в public),
  `SwimmerId?`, `ResultId?` (cascade). Check-constraints: Visibility-enum и AnchorMembersOnly.
  Миграция `AddHubGroupMediaMembersLayer` (применена локально).
- `HubGroupMediaService`: `GetGalleryAsync` фильтрует public; новый `GetMembersMediaAsync`
  (разборы с контекстом якоря: имя пловца + result_label «стиль дистанция · дата · соревнование»);
  `AddAsync` валидирует members-инварианты (официальность, якоря, эстафеты, денормализация
  SwimmerId из заплыва — источник правды заплыв, не вход).
- `GET /api/hub-groups/{slug}/media/members` — Authorize, аудитория тренировок (CanEdit или
  активный член), без кэша (личное).
- Тесты: +8 (189 всего) — официальность, якорь при public, денормализация, эстафета, изоляция
  галереи, контекст выдачи, игнор visibility у тренировочного медиа.

Клиент:
- `useHubGroupMedia`: + membersMedia/isOfficial/groupSwimmers; addMedia шлёт visibility/swimmer_id.
- `MediaEditor` (my-groups-panel): назначение «Public gallery | 🔒 Members reviews | тренировка»,
  при members — select пловца из состава; список разборов с удалением.
- `groups.html`: секция «🔒 Reviews» (MembersReviews) — грузится только аутентифицированным,
  403/пусто → скрыта; рендер видео тем же лайтбоксом (embed из канонического id, не сырого URL).
- UI выбора конкретного заплыва (result_id) в редакторе тогда НЕ делался — сервер
  поддерживал, UX отложили. Сделано позже, см. §11.

Проверка: build+tsc чистые, 189/189; живой смоук на :5079 — инвариант «якорь при public» отбит
400 с текстом, GET отдаёт разбор с ивритским именем и result_label, публичная галерея изолирована;
страница groups.html без регрессий. Нюанс dev-окружения: POST под DevAdminBypass падает FK
(синтетический юзер id=0) — артефакт обхода, не бага; реальный логин не затронут.

## 11. Что оставалось после 2B′ — и чем закрылось

Сверено с кодом 2026-09-01: не осталось ничего из списка.

- **2A: личное owner-only медиа** — сделано. `MediaController` (`api/me/media`) поверх
  `Sys_UserMedia` плюс страница **My media v3** (`media.html`, swim-centric: заплывы по
  сезонам, реакции ❤/🎉, добавление ссылки только на своём заплыве). Публичный показ пришёл
  отдельно, моделью видимости (`PublicMediaController`). Подробности — [`media-page.md`](media-page.md).
- **Выбор заплыва в редакторе разборов** — сделано: селект `resultId` в
  `client/src/projects/hub-groups-project/my-groups-panel.tsx`.
- **`youtube-nocookie` + `sandbox` в лайтбоксе `UI_SwimmerGallery`** — сделано: embed идёт на
  `youtube-nocookie.com`, у iframe стоит `sandbox="allow-scripts allow-same-origin
  allow-presentation allow-popups"`.
