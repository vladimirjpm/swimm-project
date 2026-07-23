# Handoff: Админ-дашборд «Здоровье данных» (/Admin)

## Выбранный вариант: 1c (master–detail)
Реализовывать **1c**: компактные строки-блоки слева (ручка «⠿» — drag-reorder; блок, ставший первым, автоматически открывает свои детали), справа sticky-панель деталей выбранного блока. Варианты 1a/1b в прототипе — только для справки. Порядок блоков сохранять per-admin (localStorage или user settings).

## Обзор
Редизайн главной страницы админки Swimm (\`Pages/Admin/Index.cshtml\`): один экран, показывающий **все** проблемы с данными и их масштаб. Каждая карточка кликабельна и ведёт на соответствующую админ-страницу с готовым query-фильтром. Три варианта компоновки:

- **1a — Плотный**: 4-колоночная сетка блоков-панелей, всё на одном экране 1920.
- **1b — Умеренный**: блоки-строки на всю ширину, hero-число слева, карточки справа; вертикальный скролл допустим.
- **1c — Master–detail**: компактные строки-блоки слева (метрики — inline-пилюли), справа закреплённая панель деталей выбранного блока (клик по строке выбирает).

## О файлах дизайна
Файл \`dashboard-data-health.dc.html\` — **дизайн-референс в HTML** (прототип внешнего вида и поведения), не production-код. Задача — воссоздать выбранный вариант в существующем окружении: Razor Pages + Tailwind 4 (\`Styles/admin.css\`, сборка в \`wwwroot/css/admin.min.css\`), JS-рендер в \`@section Scripts\` как на текущем Index.cshtml.

## Fidelity
**High-fidelity.** Все цвета, размеры и отступы взяты из реальной темы \`admin.css\` проекта — воспроизводить точно, через существующие Tailwind-токены (\`admin-bg\`, \`admin-surface\`, \`admin-border\`, \`admin-accent\` и т.д.), а не хардкод-hex.

## Данные
Один запрос \`GET /api/admin/dashboard/status\` (расширенные \`DashboardStatusDtos\` + \`DashboardStatusService\`, DTO сгруппировать по блокам ниже). Дорогие подсчёты — IMemoryCache 2 мин. Числа в прототипе — реалистичные заглушки.

## Блоки и метрики
Семантика цвета значения: **зелёный** #4caf50/#66bb6a = 0 проблем · **оранжевый** #ffa726 = есть работа · **красный** #ff6b6b = блокирующее · **серый** #8a8f9c = не проверялось/долг · **голубой** #4fc3f7 = инфо.

1. **Пловцы** → /Admin/Swimmers. Hero: total 12 480 (подстрока: isr · local · SYNTH). Карточки: Спорные дубли (уверенных — подстрока), Сироты, Без SwimmerOrgId, Без результатов, Loglig не привязано (инфо; на проверке/отклонено — подстрока).
2. **Клубы** → /Admin/Clubs. Hero: total (реальные/псевдо). Карточки: Спорные дубли, Без пловцов, Без страны, Заявки на статус клуба → /Admin/HubGroupClubRequests (НЕ путать с заявками на вступление в группу — блок 6).
3. **Соревнования** → /Admin/Competitions. Hero: total (с результатами/пустых). Карточки: Входящие не загружено (?stage=OnSite), Discovery с ошибкой (красный), Без OrgCompId (серый), Без результатов; Ignored — чип.
4. **Результаты** → /Admin/Results. Hero: total (TimeFail — подстрока, инфо). Карточки: FK-аномалии (красный >0), Эстафеты без участников.
4a. **Рекорды** → /Admin/Records. Карточка на набор (World/worldaquatics, Israel/isr.org.il): кол-во + дата обновления; «устарело (> N мес)» серым, порог N конфигурируемый (по умолчанию 3). Кнопка **«Проверить апдейты»** — синхронный dry-run импорта рекордов 2.6 с прогрессом по источникам прямо на дашборде (спиннер → ✓), по завершении «Найдено N изменений → дифф в /Admin/Import».
5. **Медиа** → /Admin/Media. Hero: total (видео/фото). Карточки: Битые ссылки (красный), Не проверялось (серый), На модерации (оранж — очередь).
6. **Пользователи и группы** → /Admin/Users. Hero: пользователи (активных 7 дн / деактивированных). Карточки: Заявки на вступление в группы (→ /Admin/HubGroups?tab=requests), Группы (официальных — подстрока).
7. **Системная строка** (внизу, мелко, muted): последний импорт (+статус), последняя проверка медиа-ссылок, последний discovery-скан, ошибки audit за 7 дней (оранж >0, → /Admin/Audit).

**Кастом-слот**: пунктирная карточка «＋ Кастом-карточка» — задел под конфигурируемые SQL-счётчики без правки вёрстки.

## Контракт ссылок (deep links)
Каждая метрика — ссылка на админ-страницу с query-параметром \`filter\` (значения — на английском, kebab-case). Целевая страница ОБЯЗАНА принять параметр и при загрузке применить соответствующий фильтр/открыть нужную секцию; если секции нет — добавить фильтр в существующий список/таблицу. Неизвестное значение \`filter\` игнорируется (страница открывается как обычно). Реализация: читать query в OnGet / init-скрипте страницы и проставить состояние фильтра до первого рендера списка.

Полная карта (блок → метрика → URL):

**Пловцы** /Admin/Swimmers
- Спорные дубли → \`/Admin/Swimmers?filter=dedup-unsure\`
- Уверенные дубли → \`/Admin/Swimmers?filter=dedup-sure\`
- Сироты → \`/Admin/Swimmers?filter=orphans\`
- Без SwimmerOrgId → \`/Admin/Swimmers?filter=no-org-id\`
- Без результатов → \`/Admin/Swimmers?filter=no-results\`
- Loglig (не привязано / на проверке / отклонено) → \`/Admin/Swimmers/Loglig?filter=loglig-unlinked|loglig-pending|loglig-rejected\` (loglig-список живёт на отдельной странице, не на /Admin/Swimmers)

**Клубы** /Admin/Clubs
- Спорные дубли → \`/Admin/Clubs?filter=dedup-unsure\` (уверенные: \`dedup-sure\`)
- Без пловцов → \`/Admin/Clubs?filter=no-swimmers\`
- Без страны → \`/Admin/Clubs?filter=no-country\`
- Заявки на статус клуба → \`/Admin/HubGroupClubRequests?status=pending\`

**Соревнования** /Admin/Competitions
- Входящие не загружено → \`/Admin/Competitions?filter=discovery-new\`
- Discovery с ошибкой → \`/Admin/Competitions?filter=discovery-error\`
- Без OrgCompId → \`/Admin/Competitions?filter=no-org-comp-id\`
- Без результатов → \`/Admin/Competitions?filter=no-results\`
- Ignored → \`/Admin/Competitions?filter=ignored\`

**Результаты** /Admin/Results
- FK-аномалии → \`/Admin/Results?filter=fk-anomaly\`
- Эстафеты без участников → \`/Admin/Results?filter=empty-relay\`

**Рекорды** /Admin/Records
- Карточка набора → \`/Admin/Records?region=world|israel\`
- Дифф после «Проверить апдейты» → \`/Admin/Import?diff=records\`

**Медиа** /Admin/Media
- Битые ссылки → \`/Admin/Media?filter=broken-links\`
- Не проверялось → \`/Admin/Media?filter=unchecked\`
- На модерации → \`/Admin/Media?filter=moderation-pending\`

**Пользователи и группы**
- Всего/активные/деактивированные → \`/Admin/Users\` (деактивированные: \`?filter=deactivated\`)
- Заявки на вступление → \`/Admin/HubGroups?tab=requests\`
- Группы → \`/Admin/HubGroups\` (официальные: \`?filter=official\`)

**Системная строка**: импорт → \`/Admin/ImportHistory\`; медиа-ссылки → \`/Admin/Media?filter=unchecked\`; discovery → \`/Admin/Competitions?filter=discovery-new\`; audit → \`/Admin/Audit?period=7d&level=error\`.

Для Claude Code: пройтись по целевым страницам и добавить обработку перечисленных параметров там, где её нет (сейчас часть страниц query-фильтры не принимает). Hero-число блока и заголовок ведут на страницу блока без параметров.

## Правила отображения
- Нулевые метрики сворачиваются в чипы-пилюли «✓ Название · 0» (зелёная рамка/текст, рамка с alpha ~33%); информационные чипы (Ignored) — серые. Поведение за флагом.
- Карточка метрики: фон admin-bg #14161a на панели admin-surface #1c1f26, рамка admin-border #2c303a, radius 8px; hover — рамка admin-accent #4fc3f7 (transition border-color .12s), cursor pointer.
- Панель блока: фон #1c1f26, рамка #2c303a, radius 10px, padding ~14px (1a) / 16–18px (1b).
- Заголовок блока — ссылка на страницу блока (hover → accent); «⤢» — детальный оверлей блока (1a/1b) или выбор строки (1c).

## Типографика (системный стек, как в админке)
- H1 «Dashboard»: 20px/600 #fff; подзаголовок 13px #8a8f9c.
- Hero-число: 24px (1a) / 32–34px (1b, detail-панель) / 20px (строки 1c), weight 700.
- Число карточки: 20px (1a) / 26px (1b), 700, цвет = состояние. Лейбл 11–12px; подстрока 10–11px #5c6270.
- Чипы: 11px/600, radius 99px. Системная строка: 11px #5c6270.

## Интеракции
- Клик по карточке/чипу/строке → переход на href с фильтром (в прототипе — toast-заглушка «Переход: …»). Целевые страницы должны принимать query-параметры (см. href в данных класса Component: ?filter=…, ?dedup=…, ?stage=…).
- Детальный оверлей блока (1a/1b): затемнение rgba(0,0,0,.65), панель 560px, список метрик со ссылками; клик по фону/✕ закрывает. Плейсхолдер «тренд метрики по снапшотам — v2».
- 1c: клик по строке выбирает блок; выбранная строка: фон #20242e + рамка #4fc3f7; панель деталей справа 540px, sticky top 24px.
- «Проверить апдейты»: кнопка accent (#4fc3f7, текст #0c2536, hover #81d4fa), disabled на время прогона; спиннер 10px (рамка 2px rgba(79,195,247,.25), border-top #4fc3f7, вращение .7s linear) у активного источника, ✓ #66bb6a по завершении; итог оранжевым со ссылкой на дифф.
- Toast: fixed bottom-center, фон surface, рамка accent, 13px, автоскрытие ~2.6s.

## Состояние
\`status\` (весь DTO дашборда), \`expanded\` (блок оверлея), \`selected\` (индекс блока в 1c), \`check: {status: idle|running|done, srcState: {[source]: wait|running|ok}, changes}\`.

## Дизайн-токены (= @theme в Styles/admin.css)
bg #14161a · surface #1c1f26 · surface-hover #242833 · border #2c303a · text #d4d7dd · muted #8a8f9c · dim #5c6270 · accent #4fc3f7 (текст на accent #0c2536, hover #81d4fa) · danger #ff6b6b · success #4caf50 / #66bb6a · warning #ffa726. Радиусы: 6/8/10px. Сетка 1a: grid repeat(4,1fr), gap 14px. Сайдбар 240px — существующий _Sidebar.cshtml, не трогать.

## Реализация (план из ТЗ)
1. Расширить DashboardStatusDtos/Service новыми счётчиками, кэш 2 мин.
2. Переверстать status-grid в Pages/Admin/Index.cshtml по выбранному варианту (Tailwind, пересобрать admin.min.css).
3. Добавить query-фильтры на целевых страницах, где их нет.
4. Юнит-тесты счётчиков в Swimm.Tests (пустая БД, только синтетика, ignored-пары дедупа).
5. Обновить docs/admin-pages/index.md.

## Файлы
- \`dashboard-data-health.dc.html\` — прототип; 1c (выбранный) сверху, ниже 1a и 1b для справки. Включает drag-reorder строк (⠿), панель деталей, оверлей, симуляцию проверки рекордов. Открывается в браузере как есть; данные и href — в классе Component в конце файла.
