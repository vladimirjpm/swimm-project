# Задание: убрать добавление медиа с публичных страниц + тесты /api/me/swims и реакций

## Контекст

Страница «My media» переделана в swim-centric (v3, design_handoff_my_swims_v3,1): список
заплывов favorite-пловцов с медиа, реакциями (❤ на медиа, 🎉 на заплыв) и добавлением
видео/фото. Решение Влада: **добавлять медиа теперь можно ТОЛЬКО на странице My media**
(`client/media.html` → `src/projects/my-media-project/`). С публичных страниц добавление
убираем; просмотр уже добавленного остаётся.

Серверная часть v3 уже готова (не трогать): `GET /api/me/swims` (MySwimsRepository),
`POST/DELETE /api/media/{id}/like`, `POST/DELETE /api/results/{id}/cheer`
(ReactionRepository, таблица `Sys_UserReactions`, миграция `AddUserReactions`).

## Решения (зафиксированы, не пересматривать)

1. В карточке спортсмена (`client/src/projects/sportsmen-details/sportsmen-details.tsx`)
   **убираем весь флоу добавления**: кнопку «+ video» на строке результата, инлайн-форму
   `InlineAttachForm`, и форму добавления ссылки в секции `MyMediaSection`.
2. **Просмотр остаётся**: иконка «есть видео» на строке (кнопка play → `onOpenMedia`),
   список ссылок в `MyMediaSection`, удаление НЕ добавляем и НЕ убираем — если удаление
   уже есть в `MyMediaSection`, оставить как есть.
3. Вместо форм добавления — в `MyMediaSection` одна ссылка-CTA:
   `<a href="./media.html">Manage in My media →</a>` (стиль соседних ссылок секции,
   английский текст — правило UI-only-English).
4. Тесты пишем на СУЩЕСТВУЮЩИЙ серверный код (ничего в нём не менять, если тест не
   вскроет реальный баг — тогда СНАЧАЛА зафиксируй баг в отчёте, потом минимальный фикс).

## Что уже готово (не переделывать)

- `server/Swimm.Infrastructure/Repositories/MySwimsRepository.cs` — агрегат:
  favorite-пловцы юзера → заплывы сезона (сентябрь–август, `season` = стартовый год,
  null → текущий) + PB-флаги + медиа по уровням + счётчики реакций.
- `server/Swimm.Infrastructure/Repositories/ReactionRepository.cs` — идемпотентные
  тогглы; лайк только на видимое медиа (своё / approved public / approved members для
  члена группы), cheer — на любой существующий Result.
- Тестовая инфраструктура: `server/Swimm.Tests/` на xUnit + InMemory/SQLite фикстуры —
  образцы: `UserMediaRepositoryTests.cs`, `UserFavoriteRepositoryTests.cs`,
  `UserMediaPublicationServiceTests.cs` (смотри, как там создаётся DbContext и сидятся
  Swimmer/Competition/Result/AppUser — повторяй этот паттерн).

## Шаги

### 1. Клиент: sportsmen-details.tsx

- Удалить: state `attachResultId`, колбэк `addMediaToResult`, проп-цепочку
  `onAddToResult`/`onAttachResult`/`showAttachButton`/`canAttachMedia` (строки ~119–144,
  959–1041, 1070–1233 — искать по этим именам), кнопку «+ video» (~1190) и компонент
  `InlineAttachForm` (~1237) целиком.
- В `MyMediaSection` (~579): удалить форму добавления (вызов `add(...)` ~659 и её UI),
  добавить CTA-ссылку `Manage in My media →` на `./media.html`.
- Иконку play (`hasMedia`, ~1177) и просмотр медиа НЕ трогать.
- Проверка: `npx tsc --noEmit` в `client/` без ошибок; в браузере карточка пловца
  открывается, «+ video» нет, play-иконка работает.

### 2. Тесты: MySwimsRepositoryTests.cs (новый файл в Swimm.Tests)

Кейсы:
- юзер без favorites → пустой ответ (swimmers/seasons/swims пустые);
- favorites двух пловцов → swims только их и только выбранного сезона
  (результат вне сезона не попадает; сезонная граница: даты 2025-08-31 → сезон 2024,
  2025-09-01 → сезон 2025);
- чужой заплыв (пловец не в favorites) не попадает НИКОГДА;
- эстафета (RelayId != null) попадает в swims с `IsRelay=true` и не участвует в PB;
- PB: у пловца два заплыва одной (StyleId, Distance), лучший по TimeMillisecond
  получает `IsPb=true`, худший — false; TimeFail не PB;
- медиа: level=result попадает в `Swims[].Media`, level=competition → `CompetitionMedia`,
  level=swimmer → `UnlinkedMedia`; медиа ДРУГОГО юзера не попадает вовсе;
- реакции: congrats двух юзеров на заплыв → `CongratsCount=2`, `MyCheer` true только
  у поставившего; лайк на медиа → `LikesCount`/`MyLike`.

### 3. Тесты: ReactionRepositoryTests.cs (новый файл)

- cheer на существующий Result → Count=1, Mine=true; повторный SetCheerAsync(on:true) —
  идемпотентно (Count остаётся 1); off → Count=0, Mine=false; несуществующий resultId → null;
- like на своё медиа → ok; like на чужое приватное медиа (без публикаций) → null;
- like на чужое медиа с approved public публикацией → ok;
- like на чужое медиа с approved members публикацией: член группы
  (HubGroupUserMembers) → ok, не член → null.

### 4. Проверка

```bash
dotnet build server/Swimm.sln
dotnet test server/Swimm.Tests
cd client && npx tsc --noEmit
```

## Footguns

- **Build-lock**: если `dotnet build` падает с MSB3027 («файл занят .NET Host») — жив
  предыдущий `dotnet run`/Visual Studio. Обход: собирай/тестируй с
  `--configuration Release`, API при необходимости запускай на :5079, клиент —
  конфиг `client-5079` из `.claude/launch.json`.
- `Results.CompetitionDate` — timestamp WITHOUT time zone: в тестовых данных даты
  создавай с `DateTimeKind.Unspecified` (НЕ Utc), иначе Npgsql бросает ArgumentException.
- Два DbContext: EF-команды не нужны (миграция уже применена), но если понадобятся —
  всегда `--context SwimmDbContext`.
- В `sportsmen-details.tsx` много legacy-пропов — удаляй цепочку целиком по компилятору
  (`tsc --noEmit` покажет всех потребителей), не оставляй мёртвых пропов.

## Вне скоупа (не делать)

- Не трогать `my-media-project/*` (страница v3 уже интегрирована).
- Не трогать серверные репозитории/контроллеры (кроме минимального фикса, если тест
  вскроет реальный баг — с описанием в отчёте).
- Не добавлять реакции (❤/🎉) на публичные страницы — отдельная будущая фаза.
- Не рефакторить sportsmen-details сверх удаления add-флоу.
