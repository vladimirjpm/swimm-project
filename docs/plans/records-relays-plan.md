# Эстафеты в справочнике рекордов: смешанные, WJR, мастерс (Э0–Э6)

**Статус: СДЕЛАНО — Э0–Э6 (22.09.2026, ветка records-freshness, не закоммичено).** Хвосты — §5.
[ROADMAP](../ROADMAP.md) (решение Влада 21.09.2026: «эстафеты берём»). Новый чат — с этого файла.


## 0. Для нового чата (передача 22.09.2026)

План ЗАКРЫТ — не планировать заново. Что где:
- **Правило пола:** `mixed` — смешанная эстафета (Records и Results), `none` — пол НЕ ИЗВЕСТЕН.
  Слепо `none` → `mixed` нельзя; доказательство — только состав (`RelayGender.Resolve`,
  Application). В справочнике `mixed` — только у `4X…` (`Record.ValidateGender`, Domain).
  Ключ переимпорта: `ResultMatcher.KeyGender` (none≡mixed у эстафет). Правило — в `docs/important.md`.
- **Источники:** «mix» парсеров → «mixed» в одном месте, `WorldAquaticsSource.RecordGender`
  (WR, NR стран, isrorg-age). `wa-junior` запрашивает `gender=X`, полоса смешанных `14-18`
  (`MixedBand`). Мастерс-эстафеты не берём.
- **Метки рекордов у эстафет:** `CompetitionRecordsDetector.RelayCandidateKeys` (сервер) и
  `HelperResults.relayRecordStepAge` (клиент) — одно правило: пол известен, ступень по САМОМУ
  СТАРШЕМУ, старшему >18 или состав неизвестен → open.
- **Витрина:** кнопка «Mixed» (`rk-discipline-picker`), правило «mixed только у эстафеты» —
  выводом в `records-project.tsx`; `genderLabel` — `utils/helpers/helper-gender.ts`;
  рекорды команды на странице пловца — `SwimmerPageRepository.GetRecordsHeldAsync` (целая часть держателя).
- **Разбор PDF федерации:** пол — на блок, границы блока в `IsrOrgAgeRecordsParser.AssignBlocks`
  (И-31). ⚠ Вердикт «ошибка федерации» — только после взгляда на КАРТИНКУ страницы.
- **Данные (локальная БД, всё применено):** миграция `RelayGenderMixed`; 571 смешанный NR стран;
  бассейн 1555–1562 → 50m (И-30); `isrorg-age` перезалит с починенным парсером (И-31).
- **Следующее:** хвосты §5, первый — 104 эстафеты с `none` к loglig.

## 1. Что есть — проверено 22.09.2026, не перепроверять

⚠ **Однополые эстафеты в `Records` УЖЕ ЕСТЬ** — 1977 строк. Раньше в планах и ROADMAP стояло
«эстафет нет вообще» — это была ошибка (смотрели только на `Style`, а эстафета узнаётся по
дистанции).

- **Форма строки:** `Style = freestyle | individual_medley`, `Distance = 4X50m | 4X100m | 4X200m`
  (заглавная X — форма базы, `RecordRankingQuery.Create` и `parseRecordsQuery` к ней приводят).
  Отдельных эстафетных стилей нет и не нужно.
- **Где лежат:**

  | ось | строк |
  |---|---|
  | `country/*/open` (NR всех стран) | 1792 |
  | `country/ISR/age` (федерация) | 169 |
  | `world/open` (WR) | 16 |

- **Витрина их уже показывает:** `/records` таб NR и WR — `allowRelays`, метка «relay» у
  заголовка (`isRelay`), рейтинг стран по эстафете работает. На табах Masters и World Junior
  эстафеты выключены.
- **Смешанные НЕ пишутся нигде.** `WorldRecordsParser` смешанные читает (пол `mix`,
  `"Mixed 4x100m Medley Relay"`), но `WorldRecordsSourceProvider.cs:97` их отбрасывает:
  «mixed-relay — вне модели осей Record (open male/female)». `Record.Gender` — `male | female`.
- **В результатах соревнований смешанная эстафета — пол `none`**
  (`IsrOrgCompetitionParser.cs:226`, `RelayMastersBuilder.cs:111`), не `mixed`.
- **WJR:** `WaJuniorRecordsSourceProvider` отбрасывает ВСЕ эстафеты, даже однополые (8 строк
  21.09: ж/м LCM 4×100 и 4×200 в/с, м 4×100 комплекс). Однополые можно писать уже сейчас в той
  же форме, что у WR. Смешанные (`gender=X`) провайдер даже не запрашивает.
- **Мастерс WA** (`wa-masters`): эстафеты не берём сознательно — полосы там по СУММЕ возрастов
  четвёрки («100-119»), это другая ось, а не другой ключ (решение 16.09.2026).

## 2. Решения Влада (22.09.2026)

1. **Пол смешанной эстафеты — `mixed`, и в `Records`, и в `Results`.** `none` остаётся
   значением «пол НЕ ИЗВЕСТЕН» (в PDF бывает непонятно — уточняется по loglig). Это два разных
   смысла, **`none` → `mixed` НЕ переводить**. Заплыв с `none` не матчится ни с каким рекордом,
   пока пол не уточнён. Значит, парсеры (`IsrOrgCompetitionParser.cs:226`,
   `RelayMastersBuilder.cs:111`) должны писать `mixed`, когда эстафета точно смешанная, а
   существующие `none` — разобрать (какие смешанные, какие неизвестные).
2. **Однополые WJR-эстафеты — писать сразу (Э1).** Да.
3. **Эстафеты мастерс — не берём.**
4. **Метки рекордов у эстафет в протоколе — ДА (новая функция, этап Э4б).** Сейчас эстафеты
   не сверяются с рекордами вообще (см. 2а).

## 2а. Итог Э0 (22.09.2026)

**База:** в `Results` все 845 строк с `none` — эстафеты (4X50 — 661, 4X100 — 184). По полу
участников (`RelayMembers` → `Swimmers.Gender`): **741 смешанные** (есть и м, и ж),
70 все мужчины, 14 все женщины, 19 частично неизвестны, 1 без участников. Т.е. `none` сейчас на
~88% — это смешанные, а 84 однополых с `none` — ошибка пола (кандидаты на loglig).
В `Records` мастерс-эстафет 0 (провайдер `isrorg-masters` их не фильтрует, но в PDF не нашлось).

**Эстафеты с рекордами сейчас НЕ матчатся нигде:** `ResultRepository.cs:855` отсекает
`RelayId != null` до `CompetitionRecordsDetector`; `ImportRecordPreviewService.cs:87/93`
пропускает эстафеты и не-м/ж. Значит, «бить рекорд эстафетой в протоколе» — новая функция, не
починка (вопрос 4 — к Владу).

**Жёсткие `male|female` (чинить под `mixed`):**
- провайдеры: `WorldRecordsSourceProvider.cs:96`, `WorldRecordsParser.cs:363` (пишет `mix`, не
  `mixed`!), `WaJuniorRecordsSourceProvider.cs:129,153`, `IsrOrgAgeRecordsSourceProvider.cs:117`,
  `IsrOrgMastersRecordsSourceProvider.cs:114`, `WorldAquaticsCountryFetcher.cs:122,168`;
- ⚠ `WorldBestReference.cs:107` — всё не-м/ж молча превращает в `male`;
- админка: `RecordAdminRepository.cs:165,178`, `Admin/Records/Index.cshtml` (5 `<select>`);
- API: `RecordsController.cs:117,182`;
- клиент: `routes.ts` (три парсера → null), `rk-disciplines.ts:16,59,83`,
  `records-helper.ts:19`, `useRecordsCompare/Ranking`, GENDERS в `records-compare-project.tsx`
  и `rk-discipline-picker.tsx`; ⚠ «Women если female, иначе Men» — mixed покажется как Men:
  `records-project.tsx:184`, `rk-filter-bar.tsx:66,71`, `rk-junior-table.tsx:82`, `rc-table.tsx:81`;
  `results-table.tsx:510` — `none` → `male` для поиска рекорда;
- результаты (для Э4): `IsrOrgCompetitionParser.cs:226` + `HebrewTextHelper.cs:97,112`
  (`mix` → `none`), `RelayMastersBuilder.cs:111`, `RelayBandReconstructor.cs:88,107`,
  `InvariantDataChecks.cs:113` (эстафета — не больше одного пола; для `mixed` правило другое),
  `DataCheckRunner`/`DataQualityService` (`none` = unknown), `ResultRepository.cs:416,555,571,844`.

## 3. Этапы

- ✅ **Э0. Разведка** (итог — §2а): где пол сравнивается с `male|female` жёстко (grep `"male"`, `Gender ==`
  в `RecordDiffService`, `RecordPlausibility`, `RecordRankingBuilder`, `SwimmerPageRepository`,
  детектор `CompetitionRecordsDetector`, клиент `rk-disciplines.ts`, `records-helper.ts`);
  есть ли эстафеты в PDF федерации (`isrorg-age` — да, 169; `isrorg-masters` — проверить).
- ✅ **Э1 сделан 22.09 (не закоммичен; в базу применён — проверка #12, 7 изменений).** Держатель эстафеты — команда
  (`fullName`: имён участников источник не отдаёт). Тесты: `Parse_Relays_WrittenInWrShape`,
  `JuniorRelay_FasterThanWorldRelay_IsFound` (сторож уже был — ключ по дистанции). Dry-run
  `--records-check --source wa-junior` (проверка #11): +6 эстафет LCM (ж/м 4×100 и 4×200 в/с,
  4×100 комплекс), в SCM источник эстафет не отдаёт; плюс давний 56.65 → 56.35.
- **Э1. WJR однополые (исходная постановка):** `WaJuniorRecordsSourceProvider` — вместо отбрасывания писать
  `4X100m`/`4X200m` с `freestyle`/`individual_medley` («Freestyle Relay» / «Medley Relay»);
  тест на фикстуре `wa-junior-m-lcm.json` (5 эстафет). Сторож: WJR-эстафета быстрее WR-эстафеты.
- ✅ **Э2 сделан 22.09 (не закоммичен).** `Record.Genders` + `Record.ValidateGender(gender,
  distance)` в домене — одно правило «`mixed` только у 4X…, `none`/`mix` не пол»; им пользуются
  админка (`RecordAdminRepository`) и `/api/records/ranking`; compare принимает `mixed`.
  `WorldBestReference` больше не гонит `mixed` в `male` (`none` — по-прежнему). Админка
  `/Admin/Records`: `mixed` в фильтре и формах рекорда/спорной записи, ⚥ на дашборде.
  Клиент: тип `RecordGender` в `routes.ts`, одна подпись `genderLabel()` в `rk-disciplines.ts`
  вместо пяти «female ? Women : Men». Чип «Mixed» в пикерах — Э5. Тесты `RecordGenderTests`.
- **Э2. `mixed` в модели (исходная постановка):** комментарий `Record.Gender`, валидация на входе API/админки,
  фильтры `/records` (пол «Mixed» только при эстафетной дистанции).
- ✅ **Э3 сделан 22.09 (не закоммичен, в базу НЕ применён).** Один перевод пола
  `WorldAquaticsSource.RecordGender` («mix» парсеров → «mixed», только у 4X…) — в
  `WorldRecordsSourceProvider`, `WorldAquaticsCountryFetcher` (мировые + NR стран) и
  `IsrOrgAgeRecordsSourceProvider` («מיקס»). `wa-junior`: запросы `gender=X` (код
  `disciplineGender=2`), полоса смешанных **`14-18`** (объединение 14–17 и 15–18 — решение
  22.09, поменять можно константой `MixedBand`). Мастерс федерации: «מיקס» в PDF есть, но
  эстафеты мастерс не берём (решение 3), парсер их пол и не распознаёт.
  Dry-run: `wa-junior` #13 — +3; `worldrecords` #14 — +4 world, +4 ISR open (+86 «изменится» —
  обычный откат И-13); `isrorg-age` #15 — +76 (open, adults, ступени 12–18).
  Национальные смешанные остальных стран приедут при батч-прогоне всех стран.
- **Э3. Импорт смешанных (исходная постановка):** `WorldRecordsSourceProvider` (снять пропуск), `wa-junior`
  (запрос `gender=X`, +2 медленных запроса), `isrorg-*` (если в PDF есть).
- ✅ **Э4 сделан 22.09 (не закоммичен; миграция `RelayGenderMixed` применена локально).**
  Итог и детали — И-29 в `docs/data-integrity.md`. `RelayGender.Resolve` (Application) — одно
  правило для импорта и переливки; `ResultMatcher.KeyGender` — none≡mixed у эстафет в ключе;
  `HebrewTextHelper.IsMixToken`; `LogligRelayBandMatcher` не затирает известный пол «none».
  В базе: mixed 741, none 104 (ждут loglig). Тесты `RelayGenderTests`.
- **Э4. `mixed` в результатах (исходная постановка):** парсеры пишут `mixed` для точно смешанных эстафет; разбор
  существующих `none` в `Results` (смешанные → `mixed` через loglig/протокол, остальные остаются
  `none`); матч рекорд ↔ заплыв — `mixed` с `mixed`, `none` не матчится ни с чем.
- ✅ **Э4б сделан 22.09 (не закоммичен).** Сервер: `CompetitionRecordsDetector.RelayCandidateKeys`
  — пол только male/female/mixed (`none` — без метки), мастерс-эстафеты не сверяем; ступень —
  по САМОМУ СТАРШЕМУ участнику (все 4 года рождения известны), старшему > 18 — open; состав
  неизвестен — только open. Держатель в карточке «New records» — команда (`Relay.TeamName`).
  Кандидаты в `ResultRepository` и превью импорта (`ImportRecordPreviewService`) берут эстафеты.
  Клиент: бейдж строки — то же правило (`HelperResults.relayRecordStepAge`), «держатель» по имени
  эстафете не ставится. Тесты — 4 новых в `CompetitionRecordsDetectorTests`.
  **Живая проверка (API :5079), 11 находок:** 1581 (2 — время равно рекорду, поставлен там же),
  1484 Маккабиада (2 смешанных 17 лет — правдоподобно; **1 мусор: м 4×50 компл. 00:40.87** —
  время одного этапа, пометить «Качеством»), **1560/1562 (6) — ложные из-за бассейна:** все 8 дней
  «טריילז בוגרים קיץ 2026» (1555–1562) записаны `25m`, а летний чемпионат — 50 м (ж 4×200 08:21.11
  бьёт 25-м рекорд 08:23.48, до 50-м 07:51.83 далеко). Бассейн соревнований НЕ трогал — к Владу.
- **Э4б. Метки рекордов у эстафет в протоколе (исходная постановка):** снять отсев `RelayId == null` в
  `ResultRepository.cs:855` (кандидаты для `CompetitionRecordsDetector`), нормализовать
  дистанцию `4X100` ↔ `4X100m`, пол по правилу Э4 (`none` — без метки). Держатель — команда:
  проверить, как бейдж рисуется в строке эстафеты. Тесты детектора на эстафету.
- ✅ **Э5 сделан 22.09 (не закоммичен), кроме последнего пункта.** Кнопка «Mixed» в
  `rk-discipline-picker` — только у эстафетной дистанции (и на табе WR без выбора дисциплины);
  «Mixed» и на `/records/compare`. Правило «mixed только у эстафеты» — ВЫВОДОМ (`useMemo` в
  `records-project.tsx`, `rawFilters` → `filters`), не эффектом: эффект успевал отправить
  `?gender=mixed&distance=100m` (400). Выбор не теряется — вернулся на эстафету, снова mixed.
  Эстафеты на табе World Junior включены (`personalOnly` = только Masters); смешанные там идут
  полосой 14–18. Проверено в браузере (stack-5181-api5079): рейтинг mixed 4×100 компл. (WR
  03:36.70, ISR — оспариваемая 03:32.04), переключение 4×100 → 100 → 4×100, таб Junior mixed.
  **Страница пловца (решение Влада 22.09: «показывать»):** `SwimmerPageRepository.GetRecordsHeldAsync`
  — второй путь: эстафетные рекорды, где имя пловца — ЦЕЛАЯ часть держателя («A, B, C, D»), не
  подстрока; «где проплыт» — по эстафетам, где он в составе (RelayMembers). Тест
  `HeldRelayRecordTests`. Живой пример — מרק טלר (61990): 16 → 28 рекордов, у смешанных справа
  WJR полосы 14–18. `genderLabel` переехал в `utils/helpers/helper-gender.ts` (подпись WJR на
  странице пловца печатала mixed как «men»). ⚠ Слева в строке эстафеты — имя самого пловца,
  а не команды: держатель там не показывается и раньше.
- **Э5. Витрина (исходная постановка):** чип «Mixed» на `/records`; эстафеты на табе World Junior; карточка рекорда
  пловца — рекорды пловца-участника эстафеты (держатель эстафеты — страна/сборная, а не
  человек: как сейчас показывается?).
- ✅ **Э6 сделан 22.09.** `important.md` (правило «mixed ≠ none»), ROADMAP ✅, индекс планов,
  WJR-план, И-29/И-30 в `data-integrity.md`. Прогон всех стран — §5.
- **Э6. Тесты и доки (исходная постановка):** `data-integrity.md`, `important.md`, этот план → «сделано», строка
  ROADMAP → ✅.

## 4. Как проверять

```sql
-- эстафеты в справочнике по осям и полу
select "RegionType","Category","Gender","Distance",count(*) from "Records"
where "Distance" ilike '4x%' group by 1,2,3,4 order by 5 desc;
```

`--records-check --source <ключ>` — dry-run с записью в журнал свежести (не применяет);
`--records-refresh --source wa-junior` — применить один однохозяйный источник.

## 4а. Прогон всех стран (22.09.2026)

Батч из `/Admin/Import` (214 стран, ISR пропущен по правилу): 14 479 строк, упавших 0,
несовпадение кода одно — известная ГДР (И-24). Дифф: **+571, изменений 0**, все — смешанные
эстафеты (4×100 — 359, 4×50 — 212) по 159 странам; применено. Итого `mixed` в справочнике:
country/open 581, country/age 66, world/open 4, world/junior 3. Рейтинг mixed 4×100 в/с 50 м —
154 страны (1-е США 03:18.48 = WR, Израиль 21-й, 03:27.02).

## 5. Хвосты (22.09.2026)

- **104 эстафеты с `none`** в `Results` — сверить пол по loglig (84 однополых по составу —
  почти наверняка ошибка шапки протокола; 19 — состав известен не полностью; 1 — без участников).
- ~~Спорная строка ISR 03:32.04~~ — **закрыто 22.09**: это была ошибка нашего разбора PDF
  (мужская строка ушла в «מיקס»), не федерации; починено, И-31 в `data-integrity.md`.
- **18 карточек пловцов с `M`/`F`** вместо male/female — старая грязь, не разбиралась.
- **Откуда 25m у «טריילז בוגרים קיץ 2026»** при импорте — не разбиралось (И-30).
- **Строка эстафетного рекорда на странице пловца** слева показывает имя самого пловца, а не
  команду — отдельная правка вёрстки, если понадобится.
