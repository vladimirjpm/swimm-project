# Задание: страница «My media» — администрирование всего медиа пользователя

## Контекст

Медиа-фича готова (привязка к заплывам, публикации в группы с модерацией — память
media-visibility-model). Управление сейчас размазано по карточкам пловцов. Влад просил
отдельную страницу: все его ссылки по всем пловцам, статусы публикаций, управление.

## ВАЖНОЕ ПРАВИЛО

**Весь видимый UI — ТОЛЬКО на английском** (кнопки, заголовки, статусы, плейсхолдеры,
ошибки). Комментарии в коде — двуязычные как обычно (RU prose).

## API (готово, сервер НЕ менять)

- `GET /api/me/media` (без swimmerId) — ВСЁ медиа юзера; DTO (snake_case): `{id, swimmer_id,
  level, media_type, source_type, url, result_id, competition_id, created_at, swimmer_name,
  result_label}`.
- `GET /api/me/media/publications` — все публикации: `{id, user_media_id, hub_group_id,
  hub_group_name, level: members|public, status: pending|approved|rejected, created_at, decided_at}`.
- `POST /api/me/media/{id}/publications` body `{hub_group_id, level}` (antiforgery) — подать.
- `DELETE /api/me/media/{id}/publications/{hubGroupId}` — отозвать.
- `DELETE /api/me/media/{id}` — удалить запись (каскадно снимает публикации).
- `GET /api/me/hub-groups/joined` — мои группы `{id, name, status}` (кандидаты подачи — status=active).

Готовые клиентские хуки — **реиспользуй, не дублируй**: `useUserMedia(undefined)`?? — НЕТ:
хук требует swimmerId и при null отдаёт пусто. Для этой страницы напиши свой лёгкий хук
`useAllMyMedia` в НОВОМ файле фичи (fetch `/api/me/media` + delete с antiforgery по образцу
`useUserMedia`), а для публикаций реиспользуй `useMyMediaPublications` из
`client/src/hooks/useUserMedia.ts` (уже умеет list/submit/withdraw).

## Решения (зафиксированы)

1. **Новая страница** по multi-page паттерну (см. client/CLAUDE.md):
   - `client/media.html` (копия шаблона groups.html: title "My media", div id="media-page",
     script `/src/pages/media-page.tsx`);
   - `client/src/pages/media-page.tsx` — по образцу `groups-page.tsx`;
   - добавить `media: resolve(__dirname, 'media.html')` в `rollupOptions.input`
     (`client/vite.config.js`).
2. **Фича** — новая папка `client/src/projects/my-media-project/` с корневым `my-media.tsx`
   (паттерн фичи из client/CLAUDE.md). Стиль — светлая тема как у results_main
   (var(--theme-mode-*)), НЕ тёмный стиль groups.
3. Не залогинен (`GET /auth/me` → isAuthenticated=false, есть хук `useAuth` в
   `client/src/hooks/useAuth.ts` — проверь сигнатуру) → карточка «Sign in to manage your
   media» без формы логина (кнопка может просто вести на results_main.html, где есть LoginModal —
   или используй login-modal, если он легко подключается вне results-страницы; НЕ городи
   свой логин).
4. **Раскладка**: группировка по пловцу (заголовок swimmer_name, под ним сетка превью
   3-5 колонок). Каждая карточка медиа:
   - превью (`HelperMedia.resolveThumbUrl`), клик youtube/vimeo → лайтбокс
     `UI_SwimmerGallery` (паттерн `MyMediaSection` в sportsmen-details.tsx: НИКОГДА сырой
     URL в iframe); other → внешняя ссылка noopener/noreferrer/nofollow;
   - чип result_label (если есть) или level;
   - чипы публикаций этого медиа: `{hub_group_name} · pending review|published|rejected`
     (+ " (everyone)" для approved public) с «×» = withdraw;
   - кнопка «Share with a group»: селект группы (joined active) + селект уровня
     (`group members` / `public (visible to everyone)`) + Submit — как панель в
     MyMediaSection (можно вынести упрощённую копию, живут независимо);
   - Delete с inline-подтверждением (Delete? Yes/No), как в MyMediaSection.
5. Пустое состояние: «No media yet. Add links from an athlete's card on the results page.»
6. Ссылку на страницу добавить в `MyMediaSection` (sportsmen-details.tsx) под заголовком
   details: `<a href="./media.html">Manage all my media →</a>` (className в стиле секции).
   Это ЕДИНСТВЕННАЯ правка вне новой фичи + vite.config.js.

## Проверка

- `cd client && npx tsc --noEmit` — чисто.
- Живо: клиент `npm --prefix client run dev` (или client-5079 из launch.json, API на :5079
  уже может быть запущен), открыть http://localhost:5173/media.html — гостю CTA, ошибок в
  консоли нет. Залогиненный флоу проверит Влад.

## Footguns

- Build-lock: сервер не собирать, не запускать на :5078.
- Multi-page: без записи в vite.config.js страница не соберётся в прод; в dev работает и так.
- `useMyMediaPublications` шлёт antiforgery сам — не дублируй токен-логику.
- Английский UI (см. правило выше) — никаких русских строк.

## Вне скоупа

- Сервер, тесты сервера, топбар-навигация (отдельный фронт), модерация групп (есть на
  странице группы), правки results-table/groups.tsx.
