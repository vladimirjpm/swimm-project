# Задание Sonnet: CTA «залогинься» в Favorites-UI (фаза 4.5)

## Контекст

Фаза 4 «логин на клиенте» (план `docs/tasks/client-login-plan.md`), последний шаг.
Сейчас функционал избранного для гостя просто скрыт: сердечки в таблице результатов не
рендерятся, звезда «это я» и «Мои ссылки» в попапе спортсмена не показываются. Нужно,
чтобы гость видел, что фича есть, и мог залогиниться на месте — через уже готовый
`LoginModal` (`client/src/projects/components/login-modal/login-modal.tsx`).

## Решения (зафиксированы, не пересматривать)

- Страница `results_main.html` НЕ использует `HomeHeader` — модал монтируем локально
  через новый контекст `LoginModalProvider` (см. шаг 1), а не тащим шапку.
- Гостевое сердечко в строке таблицы: рендерим те же иконки избранного в «пустом»
  состоянии, клик открывает LoginModal (вместо текущего `undefined`-колбэка). Никаких
  отдельных кнопок «Войти» в каждой строке.
- Попап спортсмена (гость): вместо скрытых звёзд/«Моих ссылок» — один компактный
  CTA-блок «Sign in to save favorites and links» с кнопкой Sign in (открывает модал).
- После успешного логина избранное должно подгрузиться БЕЗ перезагрузки страницы —
  для этого `useFavorites` перевешиваем на реактивный `useAuth` (шаг 2).
- Тексты: кнопки EN, пояснения RU или EN — выбери по месту, но единообразно
  (в модале кнопки EN, ошибки RU).

## Что уже готово (не переделывать)

- `LoginModal` — готовый компонент с пропсами `{open, onClose, onLoggedIn}`; менять его НЕ надо.
- `client/src/hooks/useAuth.ts` — `useAuth()` (реактивный, `refresh()` оповещает всех
  подписчиков через useSyncExternalStore) и `getCurrentUser(force?)`.
- `client/src/hooks/useFavorites.ts` — init сейчас однократный через `getCurrentUser()`.
- `client/src/hooks/favorites-context.tsx` — `FavoritesProvider` на `results-main-page.tsx`.
- Потребители: `client/src/projects/results-table/results-table.tsx` (строки ~34-40,
  ~358-372 — favoriteProps) и `client/src/projects/sportsmen-details/sportsmen-details.tsx`
  (~55, ~90-93 звезда/сердечко, ~300 MyMediaSection).
- В шапке (`home-header.tsx`) модал уже подключён — там ничего не менять.

## Шаги

1. `client/src/projects/components/login-modal/login-modal-context.tsx`:
   `LoginModalProvider` (рендерит `LoginModal` + держит open-state; `onLoggedIn` зовёт
   `getCurrentUser(true)`) и хук `useLoginModal(): { openLoginModal(): void }`.
   Смонтируй провайдер в `client/src/pages/results-main-page.tsx` (внутри
   `FavoritesProvider` или снаружи — не важно, зависимости между ними нет).
2. `useFavorites.ts`: сделай загрузку реактивной к логину — используй `useAuth()` и
   `useEffect` с зависимостью от `isAuthenticated` (+ `loading`): гость → сброс в пустое
   состояние; залогинен → грузим `/api/me/favorites`. Поведение при первом рендере
   не должно измениться (тот же один запрос, `loading` пока не готово).
3. `results-table.tsx`: для гостя (`!isAuthenticated`) в favoriteProps передавай
   `isFavorite:false`, `isPrimaryFavorite:false`, а `onToggleFavorite` (для не-эстафет,
   `swimmerId != null`) → `openLoginModal()`. `onTogglePrimary` гостю не нужен.
4. `sportsmen-details.tsx`: для гостя вместо звезды/сердечка и `MyMediaSection` —
   CTA-блок (стиль карточек попапа, кнопка в стиле кнопок LoginModal), клик →
   `openLoginModal()`. Залогиненному — всё как сейчас.
5. `home-header.tsx` НЕ трогать.

## Тесты (обязательно)

Тест-фреймворка на клиенте нет — обязателен `npx tsc --noEmit` в `client/` без ошибок.

## Проверка

1. `npx tsc --noEmit`.
2. Живьём (`npm --prefix client run dev`, API скорее всего уже жив на :5078):
   `http://localhost:<vite-port>/results_main.html` как гость:
   - в строках видны пустые сердечки, клик открывает LoginModal;
   - попап спортсмена показывает CTA-блок, клик открывает модал;
   - залогинься тестовым аккаунтом из модала (если есть подтверждённый; если нет —
     проверь хотя бы, что модал открывается и 401 обрабатывается) — сердечки должны
     стать кликабельными без перезагрузки страницы.
3. Останови поднятые тобой процессы.

## Footguns

- Rate limiter на auth-эндпоинтах — не долби логин (429 залипает).
- `results-table.tsx` большой и горячий — правь точечно favoriteProps, не рефактори.
- Иконки сердечка/звезды — существующие компоненты строк; посмотри, как favoriteProps
  используется ниже по дереву, прежде чем менять сигнатуры (лучше их не менять вовсе).
- Vite proxy: относительные URL + `credentials:'include'` (уже так везде).

## Вне скоупа (не делать)

- Сервер (`server/**`) — без изменений.
- `LoginModal`, `useAuth`, `home-header.tsx` — без изменений (только использовать).
- Групповые страницы (groups/home/competitions) — там CTA уже есть через шапку.
- Коммиты — не делать, примет ревьюер.
