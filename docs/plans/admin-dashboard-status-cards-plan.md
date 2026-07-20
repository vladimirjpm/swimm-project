# Админ-дашборд: карточки статуса данных (дубли / loglig / discovery)

Статус: **план, не начато** (2026-07-20, Fable). Реализация — в отдельной сессии;
задача целиком делегируема Сонету (нет auth/миграций), таск-документ писать по этому
плану + скиллу /delegate.

## Что хотим (заказ Влада)

На дашборде `/Admin` (Index.cshtml) — карточки:
1. **Спорные дубли Swimmers** — отдельно (сейчас есть, оставить как есть).
2. **Спорные дубли Clubs** — отдельно (сейчас нет вообще).
3. **Loglig ID** — привязано / не привязано.
4. **Discovery** — сколько загружено (imported) / сколько нет (new).

## Текущее состояние (разведано 2026-07-20)

- `/Admin` (`Pages/Admin/Index.cshtml`): грид `#attention-grid` (скрыт, если пусто),
  заполняется из `GET api/admin/swimmers/attention-summary`
  (`SwimmersAdminController.GetAttentionSummary` → `SwimmerAttentionSummary(orphans,
  sureCandidates, unsureCandidates)` на лету из `ISwimmerDedupService.FindCandidatesAsync`).
  Карточки — ссылки на `/Admin/Swimmers`, показываются только при >0.
- Clubs: `ClubsAdminController` имеет `GET dedup-candidates` (`IClubDedupService.
  FindCandidatesAsync`), но сводки/карточки на дашборде нет. В отчёте клубных дублей
  есть признак «уверенная/спорная» — проверить точную форму отчёта (`ClubDedupService`).
- Loglig: поля `Swimmer.LogligId/LogligIdStatus` (Verified/Suggested/Rejected/null).
  Реальные пловцы — как в `SwimmerDedupService` (там есть понятие «реальных», сироты
  исключаются? — свериться с реализацией; для карточки достаточно всех Swimmers).
- Discovery: `Sys_DiscoveredCompetitions.Status`: сейчас в БД `new` (113) и `imported`
  (3); в коде проверить полный набор статусов (есть ли `ignored`/`failed` — см.
  `CompetitionDiscoveryService`).

## Решения

- **Один сводный эндпоинт** вместо четырёх запросов с клиента:
  `GET api/admin/dashboard/status` → новый `DashboardAdminController`
  (`[Authorize(Roles="Admin")]`, `[AutoValidateAntiforgeryToken]`), инжектит только
  интерфейсы Swimm.Application:
  ```json
  {
    "swimmers": { "orphans": 0, "sureCandidates": 0, "unsureCandidates": 0 },
    "clubs":    { "sureCandidates": 0, "unsureCandidates": 0 },
    "loglig":   { "verified": 0, "suggested": 0, "rejected": 0, "unlinked": 0 },
    "discovery":{ "imported": 0, "new": 0, "other": 0 }
  }
  ```
  Реализация — новый `IDashboardStatusService` (Swimm.Application/Abstractions) +
  сервис в Infrastructure, который дергает `ISwimmerDedupService.FindCandidatesAsync`,
  `IClubDedupService.FindCandidatesAsync` и два groupby-запроса (Swimmers по
  LogligIdStatus, Sys_DiscoveredCompetitions по Status). Дубли считаются на лету —
  это уже так в attention-summary, приемлемо (тысячи строк); если станет медленно —
  кэш 2 мин через ICacheService (НЕ делать сразу).
- `loglig.unlinked` = пловцы без `LogligIdStatus` (null). Отдельно `suggested` —
  «ждут ночной проверки», `rejected` — «отклонённые, посмотреть админом».
- Существующий `attention-summary` НЕ трогать (обратная совместимость не нужна, но
  и ломать незачем) — дашборд переводится на новый эндпоинт, старый можно удалить
  ТОЛЬКО если он больше нигде не используется (проверить grep'ом; сейчас
  использовался только Index.cshtml).
- **UI**: вместо скрываемого `#attention-grid` — постоянный блок «Статус данных» из
  4 карточек (клик — переход):
  - Swimmers → `/Admin/Swimmers`: большая цифра — спорные дубли; мелко — уверенные,
    сироты. 0 всюду → цифра зелёная, иначе оранжевая.
  - Clubs → `/Admin/Clubs`: аналогично (спорные крупно, уверенные мелко).
  - Loglig → `/Admin/Swimmers/Loglig`: крупно `verified`, мелко «не привязано N»,
    при `suggested>0` — жёлтая пометка «на проверке N», при `rejected>0` — «отклонено N».
  - Discovery → `/Admin/Discovery`: крупно `new` («не загружено»), мелко
    «загружено imported». `new>0` — оранжевый, иначе зелёный/серый.
  - Вёрстка — тот же стиль, что текущие карточки attention-grid (bg-white/[.07],
    rounded-[10px], hover). Tailwind-классы переиспользовать существующие, чтобы
    не пересобирать бандл; если нужны новые — `npm run css:build` (корневой CLAUDE.md).
- Тесты (xUnit, InMemory SwimmDbContext + фейки dedup-сервисов): счётчики loglig по
  статусам, счётчики discovery по Status, сшивка сводки; по образцу
  `LogligLinkServiceTests`/`LogligSuggestionServiceTests`.

## Порядок реализации

1. `IDashboardStatusService` + реализация + DI + тесты.
2. `DashboardAdminController` (`GET api/admin/dashboard/status`).
3. Index.cshtml: блок «Статус данных», убрать загрузку старого attention-summary
   (сам эндпоинт удалить, если grep не найдёт других потребителей).
4. Проверка: build/test + вживую `/Admin` (DevAdminBypass, API :5079) — 4 карточки
   с реальными цифрами dev-БД (ожидаемо: discovery ~113 new / 3 imported, loglig
   ~6 verified).

## Footguns (для таск-документа)

- Visual Studio/запущенный API держат Release-dll — перед сборкой убить процесс на
  :5079/:5078 (`netstat -ano | findstr :5079`).
- Два DbContext; дашборд — чтение, `SwimmDbContext` достаточно (Sys_* только в нём).
- Клубные дубли: форму отчёта `ClubDedupService.FindCandidatesAsync` посмотреть до
  написания DTO (поле «sure» может называться иначе, чем у пловцов).
- Не трогать чужие незакоммиченные правки, если ещё висят (my-media, HubGroups/
  MediaController).
