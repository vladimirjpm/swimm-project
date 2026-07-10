# Задание (Sonnet 5): HubGroups, фазы 1–2 — схема + миграция + настройки, админ-CRUD

Контекст: фича «группы» — неформальные тренировочные объединения пловцов
(пример: DolphinNetanyaMasters), которые люди из **разных клубов** создают сами.
Это НЕ `Club` (официальный справочник федерации) и НЕ эстафетная команда (`Relay`).
У группы есть страница: иконка, название, описание, состав с ролями; позже (фаза 3,
НЕ в этом задании) — агрегаты результатов участников.

Нейминг утверждён: `HubGroup` (сайт = SwimHub). В UI — «Группа».

## Скоуп этого задания

- **Фаза 1**: сущности + миграция + grant для `swimm_ro` + три настройки в `AdminSettingsService`.
- **Фаза 2**: админ-CRUD (Razor Pages, по образцу `Pages/Admin/Competitions/`).

**НЕ делать** (следующие фазы, за них не браться): публичную страницу группы, публичный
API, маппинг в `SwimmReadDbContext`, «Моё избранное» как виртуальную группу, заявки на
вступление, проверку `HubGroupCreationPolicy` на пользовательских эндпоинтах (их ещё нет).

## Фаза 1 — домен, миграция, настройки

### 1.1 Сущности (`server/Swimm.Domain/Entities/`)

`HubGroup.cs` — стиль как у `Club.cs` (RU-XML-комменты, EN-идентификаторы):

| Поле | Тип | Примечания |
|---|---|---|
| `Id` | int identity | |
| `Name` | string(200), required | название (иврит/кириллица) |
| `NameEn` | string(200) | название (англ.) |
| `Slug` | string(120), required | **unique index**; для будущего URL `/group/{slug}` |
| `Description` | string(4000)? | plain text |
| `IconUrl` | string(1000)? | медиа = только ссылки (решение по проекту) |
| `CoverImageUrl` | string(1000)? | обложка страницы |
| `Links` | string(2000)? | JSON-массив `[{"kind":"whatsapp","url":"…"}]`; kind: whatsapp/telegram/instagram/site |
| `Location` | string(200)? | город/бассейн, свободный текст |
| `ClubId` | int? FK → `Club` | если группа фактически при клубе |
| `OwnerUserId` | int FK → `AppUser` | кто создал/управляет |
| `IsPublic` | bool, default true | колонка в схеме сразу; пока значение диктует глобальная настройка |
| `CreatedAt` / `UpdatedAt` | DateTime UTC | проставлять в мутациях, как у `Record.UpdatedAt` |

`HubGroupMember.cs`:

| Поле | Тип | Примечания |
|---|---|---|
| `Id` | int identity | |
| `HubGroupId` | int FK → `HubGroup`, cascade delete | |
| `SwimmerId` | int FK → `Swimmer` | **unique index (HubGroupId, SwimmerId)** |
| `Role` | string(20), required | `member` / `captain` / `coach`; default `member` |
| `SortOrder` | int | |
| `JoinedAt` | DateTime UTC | |

### 1.2 DbContext + миграция

- Маппинг ТОЛЬКО в `SwimmDbContext` (`ToTable("HubGroups")` / `ToTable("HubGroupMembers")` —
  это **бизнес-таблицы, БЕЗ префикса `Sys_`**). `SwimmReadDbContext` не трогать (фаза 3).
- ⚠ FK `OwnerUserId` указывает на `Sys_AppUsers`. Это допустимо (FK на уровне БД не требует
  SELECT-права у читателей), но когда в фазе 3 появится RO-чтение — навигацию `Owner` там
  не включать. Сейчас просто не делать `Include(Owner)` нигде, кроме админки.
- Миграция `AddHubGroups` — через skill `/db-migrate` (два DbContext, нужен
  `--context SwimmDbContext`). Применить локально, миграцию отревьюить глазами.
- **Grant**: в конец миграции (`migrationBuilder.Sql`) и в список
  `server/db/setup-roles.sql` (блок `GRANT SELECT ON … TO swimm_ro`, строки ~60–78) добавить
  `"HubGroups", "HubGroupMembers"`.

### 1.3 Настройки (`AdminSettingsService.SeedDefaults`)

Плоские PascalCase-ключи, как существующие (`MaintenanceMode`, `ResultsLoadMode`):

| Ключ | Default | Тип | Scope | Описание (RU, в том же стиле) |
|---|---|---|---|---|
| `HubGroupCreationPolicy` | `admin` | string | livesite | кто создаёт группы: admin / coach / any |
| `HubGroupMaxPerUser` | `3` | int | livesite | лимит групп на пользователя (на админа не действует) |
| `HubGroupVisibility` | `public` | string | livesite | public / private / perGroup (perGroup = работает колонка IsPublic) |

- В `Update()` добавить enum-валидацию для `HubGroupCreationPolicy`
  (`admin|coach|any`) и `HubGroupVisibility` (`public|private|perGroup`) — по образцу
  существующей проверки `ResultsLoadMode` (`AdminSettingsService.cs:78`).
- Настройки пока только хранятся и редактируются на `/Admin/Settings` (страница универсальная,
  подхватит их сама) — enforcement-логики в этом задании нет.

## Фаза 2 — админ-CRUD

Образец: `Pages/Admin/Competitions/` (Index + Edit) и `Pages/Admin/Categories/`.
Clean Architecture: PageModel-ы работают через интерфейс из
`Swimm.Application/Abstractions` (`IHubGroupAdminService` — создать порт + DTO,
реализация в `Swimm.Infrastructure/Services`), НЕ через DbContext напрямую —
как это сделано у соседних админ-CRUD.

### `/Admin/HubGroups` (Index)

- Таблица: иконка (мини), Name, Slug, клуб (если привязан), число участников,
  IsPublic-бейдж, UpdatedAt; кнопки Edit/Delete (Delete — через `_ConfirmDialog`,
  antiforgery как у соседей).
- Пункт в сайдбар (`Shared/_Sidebar.cshtml`), рядом с Competitions; иконка — из
  `_Icon.cshtml`, подобрать подходящую имеющуюся (users/group), новую не рисовать без нужды.

### `/Admin/HubGroups/Edit` (create + edit)

- Поля группы: Name, NameEn, Slug (автогенерация из NameEn при пустом — lowercase,
  дефисы, только `[a-z0-9-]`; поле редактируемое; при конфликте unique — понятная ошибка),
  Description (textarea), IconUrl, CoverImageUrl, Location, ClubId (select из справочника),
  IsPublic (checkbox с подписью «действует только при HubGroupVisibility=perGroup»),
  Links — три-четыре поля (WhatsApp/Telegram/Instagram/Site), в БД собираются в JSON.
- `OwnerUserId` — текущий залогиненный админ при создании; в форме показать read-only.
- **Участники** (в том же Edit, ниже, как категории у Competition):
  - список участников: имя (RU+EN), год рождения, клуб пловца, Role (select
    member/captain/coach), SortOrder, кнопка удаления;
  - добавление: поиск по справочнику `Swimmers` (по подстроке в LastName/FirstName и
    EN-вариантах, top-20) — как реализован поиск в существующих админ-экранах, если
    готового поиска нет — простой input + серверный handler-поиск, без JS-фреймворков;
  - дубликат участника (unique-пара) — понятная ошибка, не 500.
- Tailwind: после правок классов — `npm run css:build` в `server/Swimm.API`,
  коммитить `admin.min.css` вместе со страницами (когда попросят коммитить).

## Приёмка

- `dotnet build server/Swimm.sln` зелёный; миграция применяется на локальный Postgres
  (`/db-migrate`), повторный `database update` — no-op.
- `docker exec swimm-postgres psql -U swimm_ro -d swimm -c 'SELECT count(*) FROM "HubGroups";'`
  работает (grant на месте); та же проверка для `"HubGroupMembers"`.
- На `/Admin/Settings` видны три новых ключа; попытка сохранить
  `HubGroupCreationPolicy=banana` отклоняется.
- Сценарий в админке: создать группу «DolphinNetanyaMasters» (slug сгенерился) → добавить
  2–3 участников из справочника с ролями → поменять роль → удалить участника → удалить
  группу (участники удалились каскадом, пловцы в `Swimmers` целы).
- Дубликаты: второй раз тот же пловец в группу — ошибка валидации; второй slug такой же —
  ошибка валидации.
- Смоук через `/smoke-test-api`: API поднимается, `/Admin/HubGroups` отдаёт 200 под
  админом (и redirect/403 без логина — как у соседних админ-страниц).

## Ревью-фиксы (итерация 2 — по результатам live-ревью 2026-07-10)

Реализация фаз 1–2 принята, сценарии прогнаны вживую (CRUD, участники, дубликаты, каскад,
grant `swimm_ro`, enum-валидация настроек — всё работает). Исправить три пункта:

1. **Создание группы под `DevAdminBypass` падает с ложной ошибкой** (воспроизведено).
   Bypass-пользователь несёт `NameIdentifier="0"` (`Program.cs`, ветка DevAdminBypass) →
   `CurrentUserId()` в `Edit.cshtml.cs` отдаёт 0 → INSERT падает на
   `FK_HubGroups_Sys_AppUsers_OwnerUserId`, а `SaveAsync` в `HubGroupAdminService`
   ловит ЛЮБОЙ `DbUpdateException` и пишет «slug „…" уже занят» — враньё. Сделать:
   - в `SaveAsync` различать причину: `PostgresException.SqlState == "23505"` (unique) →
     ошибка про slug; `"23503"` (FK) и прочее → честное сообщение
     («Владелец не найден…» / общее «Не удалось сохранить»);
   - в `CreateAsync` при `ownerUserId <= 0` или отсутствующем в `Sys_AppUsers` — подставлять
     первого пользователя с ролью Admin (кейс DevAdminBypass), а если админов нет — ошибка
     валидации с внятным текстом. RU-коммент почему (bypass несёт синтетический id 0).
2. **Slug не ограничен 120 символами**: `Slugify` может вернуть строку длиннее
   `MaxLength(120)` (Name до 200) → обрезка varchar → тот же ложный «slug занят».
   В `Slugify` усечь результат до 120 с `Trim('-')` после усечения; учесть, что
   автодедупликация добавляет суффикс `-N` — итог тоже не должен превышать 120.
3. **Гигиена**: `UpdateMemberAsync`/`RemoveMemberAsync` (и их handlers) не проверяют, что
   `memberId` принадлежит группе `Id` из URL. Передавать groupId в сервис и сверять
   `member.HubGroupId == groupId`, иначе «Участник не найден».

Приёмка фиксов: под `DevAdminBypass=true` группа создаётся (владелец = первый админ);
имя длиной 200 символов без slug — создаётся с усечённым slug; повторный slug по-прежнему
даёт «занят»; `dotnet build` зелёный. После проверки — остановить процесс API (build-lock).

## Правила репо

- RU-комментарии/EN-идентификаторы, стиль соседних файлов; **не коммитить без просьбы**.
- Контроллеры/PageModel-ы — только интерфейсы Application, не Infrastructure-типы.
- `/db-migrate` для миграций; после build-lock (`MSB3027`) — убить висящий `Swimm.API`
  (см. корневой CLAUDE.md).
- Чего-то не хватает или спека противоречит коду — стоп и вопрос, не импровизировать.
