# Таск: справочный документ `docs/media-page.md` (страница My media / media.html)

## Контекст и цель

Нужен **референс-документ** по всей подсистеме пользовательского медиа с центром на
странице `media.html` (My media): как устроено, какие потоки, где footguns. Цель —
чтобы при любом баге/доработке медиа сначала читали этот файл. Аналог уже есть:
`docs/hubgroups-architecture.md` — держись того же тона (RU-проза, EN-идентификаторы,
пути файлов кликабельны, разделы с заголовками, таблицы где уместно).

**Только документация. Код НЕ менять.** Не коммить. **НЕ запускай субагентов/фоновые
агенты** — выполняй сам через Read/Grep/Bash.

## Что прочитать (источник истины — код, не догадки)

Клиент:
- `client/media.html` → `client/src/pages/media-page.tsx` → `client/src/projects/my-media-project/`
- `client/src/projects/my-media-project/my-media.tsx` — оркестратор (режимы AddLinkModal)
- `client/src/projects/my-media-project/components/add-link-modal.tsx` — попап (3 шага + single-step)
- `client/src/projects/my-media-project/components/swim-list.tsx` и другие components/
- `client/src/projects/my-media-project/use-all-my-media.ts` (вкл. standalone `addUserMedia`)
- `client/src/projects/my-media-project/use-my-swims.ts`
- `client/src/hooks/useUserMedia.ts`, `client/src/hooks/useCompetitionMedia.ts`

Сервер:
- `server/Swimm.API/Controllers/MediaController.cs` (route `api/me/media`, `/api/me/swims`,
  публикации, moderation feed; rate-limit "media", потолок MaxMediaPerUser)
- `server/Swimm.Infrastructure/Repositories/MySwimsRepository.cs`
- `server/Swimm.Infrastructure/Repositories/UserMediaRepository.cs`
- `server/Swimm.Application/Dtos/UserMediaDtos.cs`
- `server/Swimm.Application/Validation/MediaUrlValidator.cs` (если есть)
- сущность `Sys_UserMedia` (в `server/Swimm.Infrastructure/Data/SwimmDbContext.cs` + Domain entity)

Контекст модели видимости (не переписывай, сошлись): `docs/favorites-media-phase2-design.md`,
`docs/hubgroups-architecture.md`.

## Структура документа `docs/media-page.md`

1. **Что это и когда сюда смотреть** — 3–5 строк + явная строка-баннер: «При любом баге
   добавления/показа/публикации медиа — читай этот файл». Ссылка на `docs/relays.md`
   для медиа на эстафетах (я пишу его параллельно).
2. **Точки входа и навигация** — media.html → page → my-media-project; где живёт стейт.
3. **Модель данных `Sys_UserMedia`** — поля, `Level` (swimmer|competition|result — кто
   выводит и как), `Visibility` (всегда private на создании), `UserId` (owner-only),
   `ResultId`/`CompetitionId`. Таблица полей.
4. **API** — таблица эндпоинтов `api/me/media*` + `/api/me/swims` + `/api/me/moderation/media`:
   метод, назначение, кто выводит level/competition, валидация (swimmer_id, MediaUrlValidator),
   antiforgery, rate-limit "media", потолок MaxMediaPerUser.
5. **Потоки UI** (по add-link-modal.tsx):
   - Глобальный «+ Add link» — 3 шага (URL → пловец → соревнование/заплыв), варианты save
     (result / competition / general).
   - Single-step (`fixedResultId`/`fixedCompetitionId`) — контекст известен, только URL.
   - «Link to a swim» для unlinked (relink = delete+recreate — ЯВНО отметить как footgun,
     теряются id/created_at/публикации).
6. **My swims (v3)** — как `GetMySwimsAsync` собирает заплывы сезона (сезон = сент–авг),
   донасыщение (PB, реакции ❤/🎉), раскладка медиа по уровням (byResult/competition/unlinked).
7. **Публикации в группы + модерация** — заявка (members|public), авто-approve привилегии,
   сброс кэша, moderation feed. Кратко — детали в hubgroups-architecture.md (сошлись).
8. **Здоровье ссылок** — если найдёшь (коммит 5c8ddad, `Sys_UserMedia` link health на
   дашборде) — короткий раздел + ссылка на дашборд-доку.
9. **Footguns** — собери реальные: relink=delete+recreate; медиа эстафеты привязано к
   владельцу строки (см. docs/relays.md); level выводит сервер (клиенту не доверяем);
   AddLinkModal фиксированная тёмная палитра (не темизируется); antiforgery обязателен;
   rate-limit/потолок.
10. **Журнал ключевых коммитов** — таблица (хеш — что сделал). Собери через
    `git log --oneline -i --grep='media\|медиа\|видео'`; включи хотя бы: 2ad8b5d (2A),
    4a966a6 (result_id привязка), 4abadaa (публикации), c60796d (My media v3),
    575d643 (убрали add с публичных), 12b4e51 (add video на результатах),
    5ed1734 (rate limit+потолок). Проверь тела коммитов `git log -1 <hash>`.

## Проверка

- Все пути файлов существуют и ведут куда написано (кликни/сверься).
- Никаких код-изменений в `git status` (только новый `docs/media-page.md`).
- Markdown валиден, таблицы рендерятся.

## Вне скоупа

- Правки кода, тестов, других доков.
- Дублирование модели видимости из hubgroups-architecture.md — только ссылки.
- Запуск сервера/клиента (док чисто по коду).
