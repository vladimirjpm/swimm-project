# ПЕРЕДАЧА: шапка сущности, роли, «иду на тренировку» (фаза 8.15)

Сессия 28.09.2026. **Читать первым, если продолжаешь.** План и решения целиком —
[entity-hero-roles-plan.md](entity-hero-roles-plan.md) (§1 — решения Влада поверх хендоффа,
§3 — что сделано в Ш1, §4 — Ш2). Хендофф — `!design_handoff/group-club-changes/`.

## 1. Где мы

| Этап | Что | Статус |
|---|---|---|
| Ш1 | Шапка группы/клуба (2a/4a/7a/6b), табы по ролям, «Coach tools», липкая полоса, Results с чипами, список стартов, мобильное фото | ✅ сделан, **не закоммичен** |
| Ш2 | Ответы «иду / не уверен / не приду»: полоса, счётчики, кнопки, тренер отвечает за участника, чип липкой полосы, список «кто идёт» в Trainings → Sessions | ✅ сделан, **не закоммичен** |
| Ш3.0 | Квоты групп + rate limit `hubgroups` (план §5 «Квоты — сделаны») | ✅ 28.09, коммит f15f6795 (Ш1–Ш2 — 71491980) |
| Ш3.1 | Сервер дорожек: «On break», уровень аккаунта, `usual_lanes` / `lane_view` (план §5 «Ш3.1 — сделано») | ✅ 28.09, коммит 68470683; миграция `AddHubGroupBreaksAndAccountLevels` применена к локальной базе |
| Ш3.2 | Раскладка: резолвер «кем стоит аккаунт», `lane_view` в ответе RSVP, «Who's coming» (план §5 «Ш3.2 — сделано») | ✅ 28.09, коммит ad352cf9 |
| Ш3.3 | Клиент: бассейн 3b, переключатель, заметки, «On break», уровень аккаунта, настройки дорожек (план §5 «Ш3.3 — сделано») | ✅ 28.09, коммит 1a51f477 |
| Ш3 | Вид по дорожкам (3b) в Trainings → Sessions, быстрые заметки, позже — отметка посещения | **следующий**, не начат |
| Ш4 | Баннер «Are you coming?» над фото для не ответившего участника — галка тренера `rsvp_top` в расписании (план §6) | ✅ 29.09, **не закоммичен** |

Проверено: серверные тесты 2483/2483, `tsc` клиента чистый, глазами — гость / участник /
тренер, телефон и десктоп, светлая и тёмная тема. Миграции `AddHeroMobileImage` и
`AddHubGroupTrainingRsvps` применены к ЛОКАЛЬНОЙ базе (прода нет).

## 2. Незакоммиченные файлы — чьи

⚠ **Не `git add -A`.** В рабочей копии лежит и работа Влада, начатая ДО этой сессии.

**Правки Влада до сессии (не мои, при коммите — спросить Влада):**
`client/.../mix/swimmer-gallery/swimmer-gallery.tsx`, `hub-groups-project/components/group-levels.tsx`,
`hub-groups-project/components/lane-plans-api.ts`, `hub-groups-project/components/use-group-levels.ts`,
`components/deep/hero-band.tsx`, `docs/plans/lane-plans-plan.md`, `!design_handoff/*`.
Смешанные (там и его, и мои правки): `components/deep/hero-photo.tsx` (файл его, я добавил
`mobileUrl`), `components/deep/deep-theme.css`, `components/deep/entity-page.tsx`,
`club-project/components/club-hero.tsx`, `club-project/club-project.tsx`,
`hub-groups-project/components/group-hero.tsx`, `group-cards.tsx`, `group-page.tsx`,
`group-training-slots.tsx`, `swimmer-project/swimmer-page.css`, `docs/plans/README.md`,
`docs/ui-components.md`.

**Мои (этой сессии) — полностью:**
- клиент, общие: `components/deep/hero-identity.tsx`, `sticky-bar.tsx`, `view-chips.tsx`,
  правки `tabs.tsx`, `kpi.tsx`, `entity-page-types.ts`, `display-settings-card.tsx`;
  `hooks/useClubOverview.ts`; `swimmer-project/components/swimmer-panels.tsx` (чипы → общий компонент);
- клиент, группа: `group-competitions.tsx`, `group-membership.tsx`, `group-rsvp.tsx`,
  `group-trainings-tab.tsx`, `use-training-rsvp.ts`, правки `group-bits.tsx`, `types.ts`;
- сервер: `HubGroupTrainingRsvp.cs`, `TrainingRsvpRules.cs`, `HubGroupCompetitionsBuilder.cs`,
  `TrainingRsvpDtos.cs`, `ITrainingRsvpService.cs`, `TrainingRsvpService.cs`,
  `HubGroupTrainingRsvpController.cs`, обе миграции + snapshot; правки DTO групп/клуба,
  `HubGroupPublicRepository.cs`, `ClubOverviewRepository.cs`, `EntityDisplay*`,
  `EntityDisplaySettings.cs`, `Club.cs`, `HubGroup.cs`, `SwimmDbContext.cs`,
  `DependencyInjection.cs`, `HubGroupAdminService.cs`, `HubGroupUserService.cs`,
  `HubGroupsController.cs`;
- тесты: `HubGroupCompetitionsBuilderTests.cs`, `HubGroupCompetitionsTests.cs`, `TrainingRsvpTests.cs`;
- доки: `entity-hero-roles-plan.md`, этот файл, строки в `ROADMAP.md` (8.15), `important.md`,
  `plans/README.md`, `ui-components.md`.

Перед push — [pre-push-rules.md](../pre-push-rules.md): совпадают правила 5 (реестр
компонентов — сделано), 7 (ROADMAP — сделано), 10 (important.md — сделано). Новая таблица —
`Sys_`, правило 6 (грант `swimm_ro`) НЕ применяется.

## 3. Как поднять и посмотреть

1. `dotnet build server/Swimm.sln` (стенд запускает API из Debug с `--no-build`).
2. Стенд `stack-5183-api5082-personas` (API :5082 + Vite :5183) — не трогает :5078 Влада.
3. Персонаж: `http://localhost:5183/api/dev/persona?as=<ник>` — `utest-guest`, `utest-member`,
   `utest-media-author` (участники `utest-open`), `utest-coach` (управляет `utest-open`),
   `default` = живой аккаунт Влада (админ) — только смотреть, ничего не жать.
4. Страницы: `/groups/utest-open` (тест-группа, без фото и расписания),
   `/groups/dolphin-netanya-masters` (фото + расписание Tue/Sat/Sun), `/clubs/438` (клуб с
   официальной группой).
5. **Ответы (Ш2) на `utest-open` не видны — у неё нет расписания.** Для проверки ставил
   временно прямо в базе и потом вернул NULL:
   ```sql
   update "HubGroups" set "TrainingSchedule" = '{"slots":[{"day":1,"start":"23:30"},{"day":2,"start":"23:30"},{"day":3,"start":"23:30"},{"day":4,"start":"23:30"},{"day":5,"start":"23:30"},{"day":6,"start":"23:30"},{"day":7,"start":"23:30"}],"place":"בריכת נתניה","pool_type":"25m"}' where "Slug"='utest-open';
   -- после проверки:
   delete from "Sys_HubGroupTrainingRsvps" where "HubGroupId"=(select "Id" from "HubGroups" where "Slug"='utest-open');
   update "HubGroups" set "TrainingSchedule"=NULL where "Slug"='utest-open';
   ```
   (`docker exec swimm-postgres psql -U swimm -d swimm -c '…'`). Правка мимо API серверный
   кэш страницы не сбрасывает — ставить ДО запуска стенда или перезапускать его.
6. После проверки — остановить стенд (`preview_stop`), иначе Debug-сборка заблокирована.

## 4. Что дальше — Ш3 (вид по дорожкам, вариант 3b)

Хендофф §6: блок бассейна с «водой», дорожки 40px с пунктирными «канатами», подпись
«1 · fast»; кружки 26px — сплошной = идёт, пунктир золотом = не уверен, свой — с кольцом
`--deep-live`; «+N» при >5 на дорожке; счётчики «N in the water · N maybe · N out»;
переключатель ответа на три положения со скользящим ползунком; после Going / Not sure —
заметки «Late ~10 min · 1st hour only · Leaving early» (одна на выбор; сервер уже принимает
`note`: `late | first-hour | leaving-early`). Место — Trainings → Sessions, карточка
«Next training» (`group-trainings-tab.tsx`, `NextSessionCard`).

**⇒ Вопрос обсуждён 28.09.2026 — решения и ответы Влада: план §5. Работу НЕ начинать без команды.** Ниже —
исходная постановка.

**Открытый вопрос Владу (задать ДО работы):** откуда дорожки. Предложение — из опубликованного
плана дорожек на эту дату (фаза 8.14, `Sys_LanePlans`): там дорожки с уровнями и раскладка
людей. Но план раскладывает ПЛОВЦОВ, а отвечают АККАУНТЫ — человек попадёт в дорожку, только
если его аккаунт в группе привязан к пловцу (`HubGroupUserMember.SwimmerId`). Непривязанных
показывать отдельной полосой «без дорожки»? Плана на дату нет — вид дорожек не показывать
(только список)?

Там же дальше: отметка посещения после занятия (тренер уже может править ответы неделю
после — `TrainingRsvpRules.ManagerDaysBack`), `Remind N` (нет канала — отложено), список
«кто идёт» участникам по настройке группы.

## 5. Грабли сессии

- **Vite HMR после правок многих модулей** может оставить две копии `login-modal-context` →
  «useLoginModal must be used within <LoginModalProvider>» и белая страница. Лечится
  перезапуском стенда, не кодом.
- **Скриншот pane во время плавной прокрутки** приходит с пустой половиной или таймаутит —
  проверять через `javascript_tool` (DOM, геометрия), а не картинкой.
- **Тест `DirectMemoryCache_OnlyInTheAllowedPlaces`** падает, если `--artifacts-path` вне репо
  (ищет `Swimm.sln` выше папки сборки) — это не баг кода.
- **Флаг-картинка в подзаголовке** (`UI_FlagEmoji` — это `<img>`, preflight делает его block)
  рвала строку; в подзаголовках обёртка `[&_img]:inline-block`.
- **Имя сущности — `dir="auto"`, не `rtl`**: жёсткий rtl переставлял скобки в латинском имени.
- **Ответы — только личным запросом.** В общий кэшируемый ответ страницы группы нельзя класть
  ни `mine`, ни имена: он один на всех зрителей.
