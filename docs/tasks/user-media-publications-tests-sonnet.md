# Задание: тесты модели публикаций личного медиа (этап 2 media-visibility-model)

## Контекст

Реализована модель публикаций личного медиа в группы (коммит 4abadaa):
`UserMediaPublicationService` (`server/Swimm.Infrastructure/Services/UserMediaPublicationService.cs`,
интерфейс `server/Swimm.Application/Abstractions/IUserMediaPublicationService.cs`,
сущность `server/Swimm.Domain/Entities/UserMediaPublication.cs`). Код НЕ покрыт тестами —
задача: два набора тестов, «умный» сценарный (сценарий Влада дословно ниже) + матрица правил.
Тестируем сервис напрямую (in-memory SQLite), контроллеры не трогаем.

## Решения (зафиксированы, не пересматривать)

- Новый файл `server/Swimm.Tests/UserMediaPublicationServiceTests.cs`.
- Образец инфраструктуры: `server/Swimm.Tests/HubGroupMediaServiceTests.cs` и
  `UserMediaRepositoryTests.cs` (SQLite in-memory `CreateDb`, сидинг FK-обязательных
  Club/Style у ResultRecord — образец в `AthleteCareerRepositoryTests.cs`).
- Семантика сервиса (проверяй ЕЁ, менять реализацию нельзя, если тест выявит баг —
  НЕ чини сам, опиши в отчёте «Отклонения»):
  - `SubmitAsync(ownerUserId, mediaId, request{HubGroupId, Level}, isGroupPrivileged)`:
    - level только members|public;
    - медиа должно принадлежать ownerUserId;
    - группа должна существовать;
    - пловец медиа (`UserMedia.SwimmerId`) должен быть в ростере группы (`HubGroupMembers`);
    - при `isGroupPrivileged=false` податель должен быть активным user-членом
      (`HubGroupUserMembers`, Status=active); pending-член — отказ;
    - при `isGroupPrivileged=true` — членство не требуется, статус сразу approved,
      DecidedBy=owner;
    - повторная подача при существующей pending/approved → отказ "publication already exists";
    - повторная подача после rejected → ТА ЖЕ строка возвращается в pending (Id не меняется),
      level можно сменить.
  - `WithdrawAsync(ownerUserId, mediaId, hubGroupId)`: удаляет публикацию любого статуса,
    только владельцем медиа (чужая → false).
  - `DecideAsync(hubGroupId, publicationId, approve, decidedByUserId)`: approve→approved,
    !approve→rejected (в т.ч. для approved = «снять с публикации»); чужой groupId → false.
  - `GetForOwnerAsync(userId)` — все публикации всех медиа юзера.
  - `GetForGroupAsync(groupId)` — pending+approved (rejected скрыт), pending первыми.
  - `GetApprovedForGroupAsync(groupId, level)` — только approved нужного уровня.

## Набор 1 — сценарный тест (дословно сценарий Влада, один большой Fact)

Сидинг: юзер Влад (userId V), два ребёнка-пловца Child1, Child2, пловец-сам Vlad-swimmer;
три группы: G1 «мастерс» (в ростере Vlad-swimmer), G2 (в ростере Child1), G3 (в ростере Child2).
Влад — активный user-член всех трёх групп. Отдельный юзер-«чужак» Stranger (активный член G1).
Владелец/админ групп — юзер Coach (для DecideAsync).

Шаги и проверки:
1. Влад добавляет медиа: M_self (Vlad-swimmer), M_c1 (Child1), M_c2 (Child2) — по записи
   `UserMedia` (сидить напрямую в БД, репозиторий не нужен).
2. M_self остаётся приватным (никаких публикаций) → `GetForGroupAsync(G1..G3)` его не содержат.
3. Влад подаёт M_c1 в G2 (members) → pending; `GetApprovedForGroupAsync(G2, members)` пуст,
   `GetForGroupAsync(G2)` содержит со статусом pending, `GetForOwnerAsync(V)` — тоже.
4. Влад подаёт M_self в G1 (members) → pending; Coach отклоняет → rejected;
   `GetForGroupAsync(G1)` больше НЕ содержит (rejected скрыт), у владельца статус rejected.
5. Coach одобряет заявку M_c1→G2 → approved; `GetApprovedForGroupAsync(G2, members)` содержит
   ровно её, с SwimmerName Child1.
6. Влад подаёт M_c2 в G3 (public), Coach одобряет → `GetApprovedForGroupAsync(G3, public)`
   содержит; уровень members для G3 пуст.
7. Кросс-проверки изоляции: M_c1 нигде не виден в G1/G3; M_c2 — в G1/G2; подача M_c1 в G1
   (Child1 не в ростере G1) → отказ "swimmer is not in this group's roster".
8. Каскад: удаление записи M_c2 из `UserMedia` (Remove + SaveChanges) → её публикация
   исчезла из G3 (**SQLite in-memory уважает каскады EF** — проверь через новый запрос).

## Набор 2 — матрица правил (отдельные Fact/Theory)

- level мусорный ("friends") → отказ.
- Чужое медиа (mediaId другого юзера) → "media not found".
- Несуществующая группа → "group not found".
- Податель не член группы (и не privileged) → "you are not an active member of this group".
- Податель pending-член → тот же отказ.
- isGroupPrivileged=true: без членства, сразу approved, DecidedByUserId=owner, DecidedAt задан.
- Дубль подачи при pending → "publication already exists"; при approved → тот же отказ.
- Resubmit после rejected: тот же Id, статус pending, level сменился, DecidedBy/DecidedAt null.
- WithdrawAsync чужим юзером → false, запись жива; владельцем → true, записи нет.
- DecideAsync с чужим hubGroupId → false; повторное решение (approved→rejected) работает.

## Проверка

```bash
dotnet build server/Swimm.sln --configuration Release
dotnet test server/Swimm.Tests --configuration Release
```

Все существующие 299 тестов должны остаться зелёными.

## Footguns

- Build-lock: Visual Studio держит Debug-бинарники — работай ТОЛЬКО в Release.
- ResultRecord требует навигаций Club/Style (FK) — сидь по образцу AthleteCareerRepositoryTests.
- `HubGroupUserMemberStatus.Active` = "active" — используй константы, не строки.
- UserMedia сидить с Level/Visibility валидными для CHECK-констрейнтов
  ("swimmer"/"private" достаточно; для теста с ResultLabel НЕ обязательно сидить заплыв —
  ResultId можно оставить null, ResultLabel тогда null).
- В SQLite CHECK из HasCheckConstraint работают — не подсовывай мусорные статусы сидингом.

## Вне скоупа (не делать)

- Правки сервиса/контроллеров/миграций (баги — в отчёт, не чинить).
- Тесты контроллеров/авторизации, клиент, UI.
