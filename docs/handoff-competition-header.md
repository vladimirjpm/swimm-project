# Handoff — шапка соревнования + табы (results_main.html)

Хендофф для продолжения работы в новом чате. Фича: модульная шапка соревнования с
табами на `results_main.html` по образцу Group Header. Дизайн-хендофф дизайнера:
`!design_handoff/design_handoff_competition_overview/` (вариант **1b «Афиша»** десктоп
+ **2a** мобайл; прототип `Competition Overview.dc.html`). План:
`docs/design-handoff-competition-header.md`.

## Что уже сделано и ЗАКОММИЧЕНО (до 4310968, дерево чистое)

Тесты сервера **639/639** (Release), client `tsc --noEmit` чист. Всё проверено вживую
в браузере (кроме залогиненного флоу — см. «Не проверено»).

### Сервер
- **`GET /api/competition-overview?competitionId=|eventId=`** (`ResultsController`,
  маршрут `/api/competition-overview`): дэшборд — summary (результаты/дни/пловцы/клубы),
  дни, best_swim (max FINA + тай-брейки), top_clubs (10) + top_clubs_men/women (3,
  Gender="male"/"female"!), top_medalist, records. Кэш+ETag как у club-summary.
  - DTO: `server/Swimm.Application/Dtos/CompetitionOverviewDto.cs`
  - Метод: `ResultRepository.GetCompetitionOverviewAsync` + `CloneWithGender` + `GetIsraelRecordsAsync`
- **Детекция рекордов**: `server/Swimm.Application/Mapping/CompetitionRecordsDetector.cs`
  (чистый, серверный аналог клиентского `isRecordTime`; оси gender/pool/style/distance,
  категории age по возрасту/masters по диапазону/open/национальный ключ "ISR").
  Тесты: `CompetitionRecordsDetectorTests.cs` (14).
- **`/api/media/results`** теперь отдаёт competition-level медиа (`result_id` nullable):
  `UserMediaPublicationService.GetVisibleForResultsAsync` (group-скоуп НЕ тронут).
- **`/api/results` отдаёт `member_swimmer_ids`** (проекция `Relay.Members` в
  `ResultMapping`). Тест-страж `GetPaged_RelayRow_CarriesMemberSwimmerIds`.

### Клиент — `client/src/projects/results-main-project/components/competition-header/`
- `competition-header.tsx` — контейнер (hero → персональная полоса → табы)
- `competition-header-top.tsx` — hero: название, даты, бассейн, Masters, счётчики,
  **Change ▾** (складка селектора внутрь), Add media
- `competition-tabs.tsx` — Overview|Swims|Clubs|Records(если есть)|Media + Combine All
  Results справа (десктоп; на мобиле остаётся в фильтрах через `lg:hidden`)
- `competition-overview.tsx` — дэшборд: best swim, medalist, records, summary, top clubs ♂♀
- `competition-clubs.tsx` — таб Clubs + drill-down по клубу (`?club=<имя>`)
- `competition-records.tsx` — таб Records
- `competition-media.tsx` + `use-competition-add-media.tsx` — таб Media + Add media (Sonnet)
- `competition-personal-strip.tsx` — ⭐ primary / ❤️ Favorites / My media (токены `--theme-personal-*`)
- `use-competition-overview.ts` — хук дэшборда, `types.ts`

**Врезка** в `results-main-project.tsx`: `DataSourceDDL` получил проп `renderHeader` →
в режиме соревнования рендерит нашу шапку, оставляя себе панель выбора (одна шапка!).
`?tab=` и `?filter=` в URL, Overview дефолт.

**НАВ-контракт**: `?swim=<resultId>` (диплинк на заплыв, пресет фильтров + скролл/
подсветка `data-result-id` строк), `?club=<имя>` (drill-down), `?filter=my|favorites`
(скоуп Swims, обнуляет остальные фильтры).

**Токены** `--theme-personal-*` (золотые, light+dark) в `client/src/index.css`.

### Ключевой урок — эстафеты (docs/relays.md!)
Матчинг «заплыв ↔ пловец по id» ТОЛЬКО через `HelperSwimmer.resultBelongsToSwimmer` /
`resultBelongsToAny` (эстафеты по `member_swimmer_ids`, не по владельцу строки). НЕ
сравнивать `swimmer_id` напрямую в новых фильтрах — иначе теряются эстафеты (репро был:
4X50 комплекс Сабины, eventId=1).

## ОСТАЛОСЬ сделать
1. **Мобильная + dark пиксельная доводка** по прототипу `Competition Overview.dc.html`
   (нужны глаза человека — агент скриншоты снять не мог).
2. ~~**Страница пловца**~~ ✅ СДЕЛАНО (v1): самостоятельный диплинкуемый `swimmer.html?swimmer=<id>`.
   - Сервер: `GET /api/swimmers/{id}` → `SwimmerProfileDto` (имя RU/EN, клуб, страна alpha-3,
     пол, год, origin), кэш 5м; репозиторий `GetSwimmerProfileAsync`; 404 если нет. Тесты
     `SwimmerProfileRepositoryTests` (3): found/EN-fallback/unknown-id. Всего 642/642 Release.
   - Клиент: `projects/swimmer-project/` (hero + плитки career + медали + best-by-style),
     `use-swimmer-profile.ts`; карьера all-time — переиспользуемый `useAthleteCareer(fullName)`
     (адресация по id, но career матчится по имени — тот же контракт/оговорка namesakes, что
     у попапа). Врезка `swimmer.html` в `vite.config.js` input. Точка входа: в попапе
     `SportsmenDetails` ссылка «Open full profile →» (по `swimmer_id` строки).
   - Проверено вживую (API :5079): id=6415 → полный профиль+карьера (2 comp/4 races/449 pts/
     4 gold/best times), id=1 → 404 «Swimmer not found», консоль чистая.
   - ✅ Медиа-галерея (v2): `GET /api/swimmers/{id}/media` (`GetVisibleForSwimmerAsync` — те же
     правила видимости, 4 теста), секция Media на странице (грид + лайтбокс). ✅ Ссылки в
     overview (best swim/medalist) + попап SportsmenDetails.
   - TODO-доводка: favorite/⭐-«это я» на странице, мобильная/dark пиксельная доводка.
3. ~~**Add media edge**~~ ✅ СДЕЛАНО: `use-competition-add-media.tsx` теперь мёржит
   пловцов из `/api/me/media` + `/api/me/favorites` (параллельный fetch, дедуп по
   swimmer_id; медиа → hint 'has media', фавориты → 'favorite'). Юзер без медиа, но с
   фаворитами получает непустой список. Логин-флоу вживую агентом не проверялся.
4. ~~**Фоновая загрузка Swims после Overview**~~ ✅ СДЕЛАНО: `DataSourceDDL` получил
   проп `canLoadResults`; в режиме соревнования тяжёлый фетч `/api/results` откладывается,
   пока не пришёл Overview (`compOverview != null`, prefetch в фоне) или пока не открыт
   таб Swims. `primeSource` заполняет шапку из `CompetitionSource` без результатов;
   `pendingSourceRef` + эффект дотягивания (full) / paged-эффект синхронизации (paged).
   Проверено в браузере: overview инициируется РАНЬШЕ `/api/results`, Swims-таб
   наполняется (2583 строки, регресса нет).
5. **⚠️ Баг данных (chip-задача)**: в `Records` для PoolType=50m/Category=age дистанции
   сдвинуты (в "50m" лежат времена 100м, 100м-строк нет) → ложный "National record" в
   overview. Фикс сидера/импорта isr.org.il, затем перезалить рекорды.

## Не проверено вживую
Залогиненный флоу (персональная полоса, Add media, скоупы my/favorites) — агент не может
залогиниться. Владу прокликать под своим аккаунтом.

## Как запускать / проверять
- API: `preview_start name=swimm-api` (:5078) или VS. Клиент: `preview_start name=client`
  (Vite, авто-порт). Проверка: `results_main.html?competitionId=last`.
- Build/test: `dotnet build|test server/Swimm.sln --configuration Release` (VS держит
  Debug-биты → всегда Release). Client: `npx tsc --noEmit` в `client/`.
- Дефолтные фикстуры: competitionId=last = 2-дневное событие 8-11, eventId=1 то же.

## Память
`~/.claude/projects/.../memory/competition-header-tabs-plan.md` — актуальный статус
по сессиям. `docs/relays.md` — канон эстафет (читать ПЕРВЫМ при багах эстафет/медиа).
