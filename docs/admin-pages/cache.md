# /Admin/Cache — как устроен кэш

Файлы: `Pages/Admin/Cache.cshtml` (тексты), `Pages/Admin/Cache.cshtml.cs` (`CacheModel`,
`CachePolicyCatalog` — сборка таблицы из кода и карта «где на сайте»). Кнопка сброса —
`POST /api/admin/cache/invalidate` (`AdminController.InvalidateCache`, та же, что на Settings).

## Что показывает

- **Браузер: кто сколько держит** — таблица по кэширующим контроллерам: где это на сайте,
  значения `Cache-Control`, TTL ответа на сервере, GET-эндпоинты. Собирается **рефлексией при
  открытии страницы**: строковые константы контроллера с `max-age`/`no-cache`/`no-store` —
  политики браузера, `static readonly TimeSpan` — TTL на сервере, `[HttpGet]` — эндпоинты.
  Поэтому цифры не устаревают сами.
- **Сервер: как сбрасывается** — сейчас весь кэш целиком (`InvalidateAllAsync`), склейка
  параллельных промахов (`GetOrCreateAsync`); цель — сброс по меткам
  ([cache-tags-plan.md](../plans/cache-tags-plan.md)).
- **Правила под Redis** и **чего кэш не решает** (производные данные в базе пересчитываются,
  а не сбрасываются).

## Как поддерживать (решение Влада 11.09.2026)

Страница — документация, живущая в админке, и она обязана совпадать с кодом:

- таблица обновляется сама; **руками** — тексты `Cache.cshtml`, карта `WhereOnSite` в
  `Cache.cshtml.cs` и этот MD;
- их правят в том же изменении, что трогает кэш: `Cache-Control`/`CacheControlValue`,
  `PayloadTtl` и прочие TTL, `ICacheService`/`MemoryCacheService`, `CachedJsonExtensions`,
  сброс (`InvalidateAllAsync`/`InvalidateTagsAsync`), `CacheTags`;
- перед push это сверяется по [pre-push-rules.md](../pre-push-rules.md).

## Грабли

- Контроллер, которого нет в `WhereOnSite`, показывается с жёлтой пометкой «не описан» —
  это напоминание, а не ошибка. Добавь строку в карту.
- Контроллер, который ставит `Cache-Control` не константой (строкой по месту), в таблицу не
  попадёт. Держи политику в `private const string CacheControlValue`, как все.
- У группы две политики: общая (`public, no-cache`) и приватная (`private, no-store`); какая
  ушла, решает доступ зрителя — таблица показывает обе.
