# /Admin/Media — здоровье ссылок UserMedia

Файлы: `Pages/Admin/Media/Index.cshtml(.cs)`; проверка/список —
`IUserMediaLinkChecker` (`UserMediaLinkChecker`, `Swimm.Infrastructure/Services`).
Эндпоинт запуска проверки: `POST /api/admin/media/check-links` (`AdminController`).
Таблица: `Sys_UserMedia` (приватная Sys_-таблица, БЕЗ grant swimm_ro) — 4 новых
nullable-поля: `LinkCheckedAt`, `LinkOk`, `LinkStatusCode`, `LinkError`
(миграция `AddUserMediaLinkHealth`).

## Что делает

`Sys_UserMedia` хранит внешние ссылки на медиа (YouTube/Vimeo/картинки), которые
со временем умирают (видео удалено → 404). Страница показывает список ссылок,
помеченных битыми при последней проверке (`LinkOk == false`), и кнопку
**Check links now**, запускающую полный синхронный прогон всех строк таблицы.
«Битое» = реально мёртвая ссылка (404/недоступно), не формат — формат
валидируется отдельно при вставке (`MediaUrlValidator`).

Проверка — **по кнопке, on-demand**. Фоновой/периодической проверки нет
(UserMedia сейчас мало; если разрастётся — вынести в `BackgroundService`
отдельной задачей).

## Правила проверки живости (по `SourceType`)

- `youtube` → GET `https://www.youtube.com/oembed?format=json&url=<urlencoded>`:
  200 = жив, 404/400 = битый.
- `vimeo` → GET `https://vimeo.com/api/oembed.json?url=<urlencoded>`: 200 = жив,
  404 = битый.
- `other` (в т.ч. `image`) → GET самой ссылки с
  `HttpCompletionOption.ResponseHeadersRead` (тело не качаем): 2xx/3xx = жив,
  4xx = битый, 5xx/сетевая ошибка/таймаут = битый с причиной в `LinkError`.

Named `HttpClient` `"media-link-check"`: `Timeout` 10 c, `User-Agent
SwimmLinkCheck/1.0`, `AllowAutoRedirect = true` (дефолт). Параллелизм HTTP
ограничен `SemaphoreSlim(6)`; каждая строка — свой `try/catch` (падение одной
не рушит прогон). Запись в `SwimmDbContext` — последовательно, после того как
все HTTP-запросы отработали (EF DbContext не потокобезопасен), один
`SaveChangesAsync`. `LinkCheckedAt` проставляется у КАЖДОЙ проверенной строки
(даже живой); у живой `LinkError = null`.

## Дашборд

`GET /api/admin/dashboard/status` (`DashboardStatusService`) отдаёт
`DashboardMediaStatus(Total, Broken, Unchecked)`. Карточка на `/Admin` —
«Broken media links» (оранжевая при `Broken > 0`, зелёная иначе), с подписью
`total: N` и (если есть) `unchecked: N`, ссылка ведёт на `/Admin/Media`.

## Что НЕ делает (вне скоупа)

Фоновая/периодическая/инкрементальная проверка; удаление/правка медиа с этой
страницы (только показ битых + ссылка наружу); ретраи/backoff/кэш результатов
проверки.

## Вкладки / deep-link фильтры (T3b)

Три вкладки сверху страницы, переключаются query-параметром `?filter=` (обычная
SSR-навигация, без JS-fetch): **broken-links** (по умолчанию — прежний вид + кнопка
Check links now), **unchecked** (`LinkCheckedAt == null`, `IUserMediaLinkChecker.
GetUncheckedAsync`, топ-200 + total), **moderation-pending** (`UserMediaPublication.
Status == "pending"`, `IDataQualityService.GetModerationPendingAsync`, join
Media/HubGroup/владелец, топ-200 + total). Все read-only; решения по публикациям
принимают админы конкретных групп на странице группы — здесь approve/reject нет.
