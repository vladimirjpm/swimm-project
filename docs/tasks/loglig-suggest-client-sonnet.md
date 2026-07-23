# Задание (Sonnet): Loglig ID — кнопка «предложить профиль» в публичном клиенте (хвост шага 6)

Контекст: план `docs/loglig-id-plan.md`. Серверная часть шагов 1–6 готова: залогиненный
пользователь может предложить пловцу его loglig-профиль через
`POST /api/swimmers/{id}/loglig-suggest` (тело `{"logligId": <int>}`, `[Authorize]` +
antiforgery + rate-limit; ответы: 200 `{accepted:true}` или 400 `{error:"…"}`), ночной
джоб верифицирует. Не хватает UI в React-клиенте на странице пловца и лёгкого GET-статуса,
чтобы не показывать кнопку уже привязанным. Это твоя работа.

## Решения (зафиксированы, не пересматривать)

- **Анти-SSRF**: на сервер уходит ТОЛЬКО число. Из вставленной пользователем строки клиент
  извлекает ID regex'ом `Players/Details/(\d+)` либо принимает голое число `^\d+$`;
  иначе — ошибка «не похоже на ссылку карточки loglig» без запроса на сервер.
- Новый серверный эндпоинт статуса: `GET /api/swimmers/{id:int}/loglig-status` →
  `{ "status": null | "Suggested" | "Verified" | "Rejected" }` (только статус, без
  LogligId и аудита). Анонимный доступ (`[AllowAnonymous]` не нужен — просто без
  `[Authorize]`), без кэша (запрос точечный и дешёвый). Положи его в существующий
  `Controllers/LogligSuggestController.cs` (сними атрибут `[Authorize]` с класса и
  повесь на POST-метод).
- UI — на странице пловца `client/src/projects/sportsmen-details/sportsmen-details.tsx`,
  рядом с существующими действиями пловца (избранное/«это я» — найди блок, где
  используются `isFav`/`canMark`, и поставь элемент в том же ряду/стиле):
  - статус `Verified` → маленький бейдж «loglig ✓» (title: «Профиль на loglig.com
    подтверждён»), без ссылки на loglig (LogligId клиенту не отдаём);
  - статус `Suggested` → серый бейдж «loglig: на проверке», кнопки нет;
  - статуса нет / `Rejected` → для залогиненных кнопка «Предложить loglig-профиль»,
    открывающая инлайн-поле (input + «Отправить» + «Отмена»); для гостей ничего не
    показывать (не добавлять CTA логина — не плодить шум);
  - после успешной отправки — заменить на бейдж «loglig: на проверке»; ошибку сервера
    (400 `{error}`) показать текстом под полем.
- Запрос — тем же паттерном, что `useUserMedia`: antiforgery-токен с
  `/api/antiforgery/token` (см. `client/src/hooks/useUserMedia.ts`, механика cachedToken),
  `credentials: 'include'`, заголовок `X-XSRF-TOKEN`.
- Новый хук `client/src/hooks/useLogligStatus.ts`: `useLogligStatus(swimmerId)` →
  `{ status, refresh, suggest(input: string): Promise<{ok: boolean; error?: string}> }`,
  где `suggest` сам извлекает ID из строки. Логика извлечения — экспортируемая чистая
  функция `extractLogligId(input: string): number | null` (для теста/переиспользования).
- `isAuthenticated` бери из `useFavoritesContext()` (уже используется в
  sportsmen-details.tsx).
- Тексты UI — русские, в стиле соседних (посмотри формулировки рядом).

## Что уже готово (не переделывать)

- Серверные: `ILogligSuggestionService`, `LogligSuggestController` (POST), гарды и джоб.
  Для GET-статуса можно инжектить `SwimmDbContext`? НЕТ — контроллеры инжектят только
  интерфейсы Swimm.Application: добавь метод в `ILogligSuggestionService`:
  `Task<string?> GetStatusAsync(int swimmerId, CancellationToken ct)` (null и для
  несуществующего пловца — не палим существование) + реализация одним запросом
  `AsNoTracking().Select(s => s.LogligIdStatus)`.
- Паттерн antiforgery на клиенте: `client/src/hooks/useUserMedia.ts`.
- Серверные тесты сервисов: `Swimm.Tests/LogligSuggestionServiceTests.cs` — добавь туда
  1–2 теста на `GetStatusAsync` (есть статус / нет пловца → null).

## Шаги

1. Сервер: `ILogligSuggestionService.GetStatusAsync` + реализация + GET-эндпоинт в
   `LogligSuggestController` (`[Authorize]` переносится с класса на POST-метод!).
2. Клиент: `extractLogligId` + хук `useLogligStatus` (GET при маунте, если swimmerId есть).
3. Клиент: интеграция в `sportsmen-details.tsx` по правилам из «Решений».
4. Тесты: серверные на `GetStatusAsync`. Клиентских юнит-тестов в проекте нет — не заводить
   инфраструктуру, `extractLogligId` достаточно ручной проверки.

## Проверка

- `dotnet build server/Swimm.sln` — 0 ошибок; `dotnet test server/Swimm.Tests` — зелёные.
- `npx tsc --noEmit` в `client/` — чисто.
- Живо: страница пловца в клиенте (`client-5079` из .claude/launch.json репозитория:
  `SWIMM_API_TARGET=http://localhost:5079 npm --prefix client run dev`; API на :5079,
  скорее всего, уже запущен — проверь `curl http://localhost:5079/api/admin/loglig/config`,
  если нет — `dotnet run --project server/Swimm.API --no-build --configuration Release
  --urls http://localhost:5079` в фоне). DevAdminBypass даёт залогиненного пользователя.
  Проверь: у пловца ברנצב סבינה (#6066, Verified) — бейдж; у непривязанного — кнопка,
  отправка мусора → клиентская ошибка, отправка `https://loglig.com:2053/Players/Details/999999?seasonId=1715`
  → принято (Suggested). После проверки сними тестовое предложение:
  POST `/api/admin/loglig/unlink` body `{"swimmerId":<id>}` (из админки :5079, там же токен).

## Footguns

- Visual Studio может держать Debug-bin (MSB3027) — Release. При пересборке сервера сначала
  останови процесс, держащий :5079 (иначе dll залочены), потом собери и подними снова.
- В рабочем дереве чужие незакоммиченные правки: `client/src/projects/my-media-project`,
  `HubGroupsController.cs`, `MediaController.cs` — НЕ трогать (sportsmen-details.tsx можно).
- Не ходи на loglig.com/serper.dev; предложение с несуществующим loglig ID валидно
  (проверит ночной джоб).
- Снятие `[Authorize]` с класса контроллера: перепроверь, что POST остался под
  `[Authorize]` (это критично).

## Вне скоупа (не делать)

- Отображение loglig-данных/обогащение, шаг 7 (батч), админка (готова).
- Никаких изменений в LogligLinkService/джобе/провайдере.
- Не коммитить.
