# Задание: страница группы — опубликованное медиа участников + inbox модерации (этап 3/4)

## Контекст

Сервер публикаций личного медиа готов (коммиты 4abadaa + продолжение): участники подают своё
видео в группу, админ группы одобряет. Владельческий UI (подача из «Моих ссылок») уже сделан.
Осталась страница группы (`client/src/projects/hub-groups-project/groups.tsx`, страница
groups.html): показать одобренные публикации участников и дать управляющим inbox модерации.

## API (готово, менять нельзя)

- `GET /api/hub-groups/{slug}/media/published?level=public` — одобренные public-публикации,
  доступно всем (аноним включительно).
- `GET /api/hub-groups/{slug}/media/published?level=members` — одобренные members-публикации;
  сервер сам отдаёт 401/403 не-членам.
- `GET /api/hub-groups/{id:int}/media/publications` — inbox для управляющих (CanEdit):
  pending первыми, затем approved; rejected скрыт.
- `POST /api/hub-groups/{id:int}/media/publications/{publicationId}/decision`
  body `{"approve": true|false}`, antiforgery — одобрить/отклонить (для approved
  `approve:false` = «снять с публикации»).

Форма элементов published/inbox (`GroupPublicationInboxItemDto`, snake_case):
`{id, level, status, created_at, media_type, source_type, url, owner_user_id, owner_email,
swimmer_id, swimmer_name, result_id, result_label}`.

## Решения (зафиксированы)

1. **Public-слой**: в существующую секцию `GroupGallery` НЕ лезть — под ней отдельная секция
   «From members» (тот же стиль карточки `hp-card-std`, тайлы `GalleryTile`-подобные), данные
   с `?level=public`. Рендер видео — только через `UI_SwimmerGallery` лайтбокс (никогда сырой
   URL в iframe) — образец `MembersMediaSection` в этом же файле (~строка 328). Подпись тайла —
   `swimmer_name` (+ `result_label`, если есть). Пустой список → секция не рендерится.
2. **Members-слой**: в существующей members-секции (`MembersMediaSection`) добавить ПОСЛЕ
   тренерских разборов блок публикаций участников с `?level=members` — та же сетка/лайтбокс.
   Fetch делать только когда members-секция вообще доступна (там уже есть такая логика).
3. **Inbox модерации**: видят только управляющие. Признак «я управляю группой» на странице
   уже есть (см. `manages` в groups.tsx ~строка 226) — используй его же источник. UI: карточка
   «Заявки на публикацию» со строками: превью-ссылка (домен), swimmer_name, result_label,
   owner_email, level (бейдж «участникам»/«публично»), status; кнопки: pending → «Опубликовать»
   / «Отклонить»; approved → «Снять». После решения — рефетч inbox И обоих published-списков.
   Пустой inbox → карточку не рендерить.
4. Мутации — POST с antiforgery: в проекте готовый паттерн `apiFetch` в
   `use-my-hub-groups.ts` — используй его (экспортируй или продублируй локально по образцу).
5. Никаких правок сервера. Никаких правок sportsmen-details/results-table (делаются параллельно!).

## Что уже готово (не переделывать)

- `groups.tsx`: `GalleryTile`, `GroupGallery`, `MembersMediaSection` (fetch members-медиа
  ~строка 338), `manages`/`isMember` (~строка 226), лайтбокс-паттерн `UI_SwimmerGallery`
  с `openIndex`/`indexById`.
- Типы медиа: `HubGroupMediaItem` в `utils/interfaces/results.ts`; для публикаций объяви
  свой интерфейс в `hub-groups-project/types.ts` по wire-форме выше.

## Шаги

1. Тип `GroupPublicationItem` в `types.ts` (snake_case поля из wire-формы).
2. Хук(и) в groups.tsx (локально, по образцу MembersMediaSection): fetch published по slug
   и level; fetch inbox по id (только если manages).
3. Секция «From members» (public) на странице группы под Gallery.
4. Блок публикаций внутри members-секции.
5. Карточка «Заявки на публикацию» (только manages) с кнопками решения.
6. Проверка: `npx tsc --noEmit` из папки client (`cd client; npx tsc --noEmit`).

## Footguns

- Build-lock: сервер НЕ собирать и не трогать; работа только в client/.
- `tsc` запускать из `client/` (в корне нет typescript).
- Рендер видео — только канонический embed через `UI_SwimmerGallery`; `other`-ссылки —
  `<a target="_blank" rel="noopener noreferrer nofollow">`.
- result_id в DTO — number|null; поля snake_case.
- Страница групп рендерится и для анона: fetch published?level=public — без предусловий,
  members/inbox — только при соответствующих правах (иначе лишние 401 в консоли).

## Вне скоупа

- Сервер, тесты сервера, sportsmen-details, results-table, админка сайта.
- Дизайн-система: используй существующие классы карточек groups.tsx, ничего нового не изобретать.
