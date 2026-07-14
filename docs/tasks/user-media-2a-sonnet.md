# Задание Sonnet: 2A — личное owner-only медиа (api/me/media + клиент)

Источник: `docs/tasks/media-membership-plan.md` шаг 3; полная спека —
`docs/favorites-media-phase2-design.md` §3–§7 (прочитай §0–§8 перед стартом).
Суть: залогиненный юзер ведёт **личную** подборку ссылок (youtube/vimeo/прочее)
к пловцу. Видит только он сам. Публичного слоя НЕТ.

## Решения (зафиксированы, не пересматривать)

1. **Миграций НЕТ.** Таблица `Sys_UserMedia` и entity `Swimm.Domain/Entities/UserMedia.cs`
   уже существуют (миграция `AddUserFavoritesAndUserMedia`). Канонический id ролика
   отдельной колонкой НЕ храним: сервер валидирует, что URL даёт id (иначе 400),
   клиент извлекает id при рендере существующим `HelperMedia`. Схему БД не трогать
   (параллельно в другой ветке идёт другая миграция — конфликт снапшота).
2. **Visibility всегда `private`.** Поле в POST/PUT-запросе не принимаем вообще
   (в DTO его нет); в entity остаётся дефолт `"private"`. Эндпоинта `/api/media`
   (публичного) НЕ создаём.
3. **Level в 2A — только `swimmer`.** Уровни `competition`/`result` сервер отклоняет 400
   («not supported yet») — UI для них появится в следующих шагах. `SwimmerId` обязателен.
   `ResultId`/`CompetitionId` в DTO не принимаем.
4. **Валидация URL на сервере** (в репозитории или контроллере — см. шаги):
   - `SourceType` ∈ {`youtube`, `vimeo`, `other`}; `MediaType` ∈ {`image`, `video`}.
   - `youtube`: URL матчится regex `(?:youtube\.com/(?:watch\?v=|shorts/|embed/)|youtu\.be/)([a-zA-Z0-9_-]+)`, иначе 400.
   - `vimeo`: URL матчится `vimeo\.com/(\d+)`, иначе 400.
   - Любой SourceType: URL абсолютный, схема **https** (`Uri.TryCreate` + `Scheme == "https"`), длина ≤ 1000, иначе 400.
   - `image` допустим только с `SourceType='other'` (произвольный img-хост); рендер на
     клиенте — как в существующей галерее (тайл-превью), это private-only, приемлемо.
5. **Рендер на клиенте — НИКОГДА не сырой URL в iframe.** youtube/vimeo → лайтбокс
   `UI_SwimmerGallery` (он уже строит embed из id через `HelperMedia`); `other` —
   внешняя ссылка `<a target="_blank" rel="noopener noreferrer nofollow">`.
6. `Caption`/подпись — НЕ добавляем (нет колонки, а миграции запрещены).
7. PUT в 2A не нужен (менять нечего: visibility фиксирован, подписи нет) — только
   GET/POST/DELETE. В спеке §3 PUT указан для visibility — он появится в 2B.

## Что уже готово (не переделывать)

- Entity `server/Swimm.Domain/Entities/UserMedia.cs` — поля см. в файле.
- `SwimmDbContext` уже маппит `Sys_UserMedia` (DbSet `UserMedia`).
- **Эталон серверного паттерна** — `server/Swimm.API/Controllers/FavoritesController.cs` +
  `Swimm.Application/Abstractions/IUserFavoriteRepository.cs` +
  `Swimm.Infrastructure/Repositories/UserFavoriteRepository.cs` +
  `Swimm.Application/Dtos/FavoriteDtos.cs`. Копируй структуру один в один:
  `[Authorize]` + `[AutoValidateAntiforgeryToken]` + `CurrentUserId()` + все операции
  scoped по userId.
- Регистрация DI — `Swimm.Infrastructure/DependencyInjection.cs` (рядом с
  `IUserFavoriteRepository`).
- Клиент: эталон API-хука — `client/src/hooks/useFavorites.ts` (та же куки/antiforgery
  механика — повтори её точно). Регекспы id — `client/src/utils/helpers/helper-media.ts`.
  Лайтбокс — `client/src/projects/components/mix/swimmer-gallery/swimmer-gallery.tsx`
  (смотри, как его зовут groups.tsx / training-by-session.tsx — тот же паттерн).

## Шаги

### Сервер

1. `Swimm.Application/Dtos/UserMediaDtos.cs`:
   - `UserMediaDto { Id, SwimmerId, Level, MediaType, SourceType, Url, CreatedAt }`
     (snake_case naming в JSON как у FavoriteDto — посмотри, как там устроено).
   - `AddUserMediaRequest { SwimmerId, MediaType, SourceType, Url }` (Level не принимаем —
     сервер ставит `"swimmer"`).
2. `Swimm.Application/Abstractions/IUserMediaRepository.cs`:
   ```csharp
   Task<List<UserMediaDto>> GetForUserAsync(int userId, int? swimmerId = null);
   Task<UserMediaDto?> AddAsync(int userId, AddUserMediaRequest request); // null = swimmer не найден
   Task<bool> RemoveAsync(int userId, int mediaId);
   ```
3. `Swimm.Infrastructure/Repositories/UserMediaRepository.cs` — по образцу
   UserFavoriteRepository, `SwimmDbContext`. AddAsync проверяет существование Swimmer
   (иначе null → контроллер 400/404).
4. Валидация форматов (п.4 решений) — статическим хелпером в Application
   (например `Swimm.Application/Validation/MediaUrlValidator.cs`), чтобы тестировать
   без контроллера. Контроллер возвращает 400 с `{ error = "..." }` как FavoritesController.
5. `Swimm.API/Controllers/MediaController.cs`, `[Route("api/me/media")]`:
   - `GET ?swimmerId=` → список своих.
   - `POST` → валидация → 201.
   - `DELETE {id:int}` → owner-scoped, 204 / 404.
6. DI-регистрация в `DependencyInjection.cs`.

### Клиент

7. `client/src/hooks/useUserMedia.ts` — по образцу useFavorites: `list(swimmerId)`,
   `add({...})`, `remove(id)`; состояние + перезагрузка после мутаций.
8. UI: в карточке пловца (`client/src/projects/sportsmen-details/sportsmen-details.tsx`)
   секция «Мои ссылки» — рендерится ТОЛЬКО залогиненному (смотри, как страница узнаёт
   auth-статус; если карточка его не знает — возьми механику из useFavorites/auth-хука):
   - список своих медиа по текущему пловцу: превью через `HelperMedia.resolveThumbUrl`,
     клик по youtube/vimeo → лайтбокс `UI_SwimmerGallery`, `other` → внешняя ссылка;
   - форма добавления: input URL + кнопка; sourceType определяй автоматически
     (`extractYoutubeId` → youtube, `extractVimeoId` → vimeo, иначе other),
     mediaType: video для youtube/vimeo, для other — переключатель image/video;
   - удаление своих ссылок (крестик с confirm).
   Стилистика — как соседние секции карточки; RU-тексты как в остальном UI.

### Тесты (обязательно)

9. `server/Swimm.Tests/UserMediaRepositoryTests.cs` — по образцу
   `UserFavoriteRepositoryTests.cs` (та же инфраструктура тестовой БД):
   - IDOR: чужое media не приходит в GetForUser, RemoveAsync чужого → false;
   - Add с несуществующим SwimmerId → null;
   - фильтр `swimmerId` в GetForUserAsync;
   - Visibility у созданной записи = `private`;
   - каскад: удаление Swimmer удаляет его UserMedia (проверь поведение FK из миграции).
10. `MediaUrlValidatorTests.cs`: youtube watch?v= / youtu.be/ / shorts/ / embed/ → ok;
    vimeo /123 → ok; мусорный URL / http:// / javascript: → reject; other + https → ok.

## Проверка

- `dotnet build server/Swimm.sln -c Release` — чисто.
- `dotnet test server/Swimm.Tests -c Release` — все зелёные (было 189).
- `cd client && npx tsc --noEmit` — чисто.
- Живой смоук: API Release на :5079 (см. footguns), клиент `client-5079`;
  залогиниться, открыть карточку пловца, добавить youtube-ссылку, увидеть в списке,
  открыть лайтбокс, удалить. Незалогиненному секция не видна.

## Footguns

- **Build-lock:** Visual Studio держит :5078 и Debug-биты. Собирай `-c Release`,
  API запускай из `bin/Release` на `--urls http://localhost:5079`, клиент — конфигурация
  `client-5079` из `.claude/launch.json`. После смоука процесс API убить.
- **Два DbContext:** `Sys_UserMedia` — ТОЛЬКО через `SwimmDbContext`. EF-команды не
  понадобятся (миграций нет) — если показалось, что нужна миграция, остановись и
  напиши в отчёте.
- **swimm_ro:** `Sys_UserMedia` в RO-грант НЕ добавлять (личные данные).
- DevAdminBypass даёт синтетического юзера id=0 — POST под ним упадёт FK; для смоука
  логинься реальным аккаунтом.

## Вне скоупа (не делать)

- Публичный слой `/api/media`, visibility=public, rate-limit, модерация (это 2B).
- Уровни result/competition и выбор заплыва в UI (шаг 4 плана).
- Миграции схемы, новые колонки (Caption, CanonicalId).
- Правки лайтбокса swimmer-gallery (nocookie/sandbox — отдельный шаг 5 плана).
- Любые правки HubGroup*/членства (параллельная ветка).
