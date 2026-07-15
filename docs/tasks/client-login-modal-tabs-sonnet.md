# Задание Sonnet: вкладки «Регистрация» и «Забыл пароль» в LoginModal (фаза 4.3)

## Контекст

Роадмап-фаза 4 «логин на клиенте» (`docs/ROADMAP.md`, план `docs/tasks/client-login-plan.md`).
Шаги 1–2 уже сделаны: есть общий хук `client/src/hooks/useAuth.ts` и модал логина
`client/src/projects/components/login-modal/login-modal.tsx` (Google + email/пароль),
открывается из шапки `client/src/projects/home-project/components/home-header.tsx`.
Твоя задача — шаг 3: добавить в этот модал вкладки «Регистрация» и «Забыл пароль».

## Решения (зафиксированы, не пересматривать)

- Auth остаётся cookie-based; клиент только UI, никаких токенов в localStorage.
- Всё внутри существующего `login-modal.tsx` — режимы одного модала, НЕ отдельные
  компоненты-порталы и НЕ отдельные страницы.
- Режимы: `'login' | 'register' | 'forgot'` — локальный `useState<Mode>` в LoginModal,
  сброс в `'login'` при каждом открытии (в существующем `useEffect` по `open`).
- Переключение — текстовые ссылки-кнопки внизу формы логина: «Create account» и
  «Forgot password?»; из register/forgot — ссылка «← Back to sign in». Google-кнопка и
  разделитель «OR» показываются ТОЛЬКО в режиме login.
- Язык UI-текстов: заголовки/кнопки — EN (как «Sign in» сейчас), тексты ошибок/статусов —
  RU (как «Неверный email или пароль.» сейчас). Сохрани этот стиль.
- Verify-email и reset-password остаются серверными HTML-страницами — клиент на них
  НЕ ссылается (туда ведут ссылки из писем). Кнопки «выслать письмо снова» НЕТ —
  такого эндпоинта не существует.

## Что уже готово (не переделывать)

- `login-modal.tsx`: портал, стили (`hp-card-std`, константа `inputClass`, классы
  кнопок), обработка Esc/клика-мимо, сброс состояния при открытии, форма логина
  с обработкой 401/403/429. Расширяй его, стиль полей/кнопок переиспользуй.
- Серверные эндпоинты (НЕ трогать сервер вообще):
  - `POST /auth/register` `{email, password, displayName?}` →
    200 `{message}` всегда при валидном вводе (anti-enumeration: занятый email тоже 200);
    400 `{error}` — «Password must be at least 8 characters.» / «Invalid email address.»;
    429 — rate limit.
  - `POST /auth/forgot-password` `{email}` → всегда 200 `{message}`; 429 — rate limit.
- Оба POST — fetch относительным URL, `credentials: 'include'`,
  `Content-Type: application/json`, БЕЗ antiforgery-заголовка (гостевые эндпоинты).

## Шаги

1. В `login-modal.tsx` добавь `type Mode = 'login' | 'register' | 'forgot'` и state.
2. Режим `register`: поля Display name (опционально), Email, Password (required,
   `minLength={8}`, `autoComplete="new-password"`); submit → `POST /auth/register`;
   на 200 — заменить форму статус-блоком «Письмо отправлено — подтверди email по ссылке
   из письма.» (зелёный/нейтральный стиль, по аналогии с блоком ошибки) + ссылка
   «← Back to sign in»; на 400 — показать `error` из тела ответа как есть; на 429 —
   «Слишком много попыток. Попробуй позже.»; сеть — «Сеть недоступна. Попробуй ещё раз.».
3. Режим `forgot`: поле Email; submit → `POST /auth/forgot-password`; на 200 — статус
   «Если такой email зарегистрирован, письмо со ссылкой отправлено.»; 429/сеть — как выше.
4. В форме логина внизу добавь строку со ссылками-кнопками «Create account» и
   «Forgot password?» (стиль: мелкий текст `text-[#7dd3fc]`, hover-подчёркивание).
5. Заголовок модала меняется по режиму: Sign in / Create account / Reset password.
6. При смене режима сбрасывай `error`/статус (email можно сохранять между режимами —
   удобно, но не обязательно).

## Тесты (обязательно)

Клиентских unit-тестов в репо нет (проверь `client/package.json` — test-скрипт от CRA
не настроен под vitest) — НЕ заводи новый тест-фреймворк. Обязательная проверка:
`npx tsc --noEmit` в `client/` без ошибок.

## Проверка

1. `npx tsc --noEmit` в `client/`.
2. Живьём: API уже может быть запущен кем-то на :5078 (Vite proxy туда и смотрит) —
   тогда просто `npm --prefix client run dev`. Если :5078 свободен — запусти
   `dotnet run --project server/Swimm.API --urls http://localhost:5078` в фоне.
3. Открой `http://localhost:<vite-port>/home.html` → Sign in → прогоняй:
   - переключение всех трёх режимов и «← Back to sign in»;
   - register с паролем короче 8 символов → видна ошибка с сервера (400);
   - register валидный (уникальный email вида `test+<random>@example.com`) → статус
     «письмо отправлено» (само письмо в dev пишется в консоль API — открывать не нужно);
   - forgot с любым email → статус «если email зарегистрирован…».
   ⚠️ Не долби эндпоинты подряд — rate limit «auth» залипнет (429).
4. Останови запущенные тобой процессы (dotnet run), чтобы не держать build-lock.

## Footguns

- Build-lock: если `dotnet build` падает с MSB3027 «Swimm.API.dll is locked» — предыдущий
  `dotnet run`/VS живы; для клиентской задачи сервер пересобирать вообще не нужно.
- `/auth/*` ходит через Vite-proxy — только относительные URL + `credentials:'include'`.
- Rate limiter на auth-POSTах: между живыми пробами делай паузы, 429 — это лимит, не баг.

## Вне скоупа (не делать)

- Сервер: никаких изменений в `server/**`, включая тексты ответов.
- Шаг 5 плана (CTA «залогинься» в Favorites-UI) — отдельная задача.
- Кнопка «выслать подтверждение снова», смена пароля из UI, страницы verify/reset.
- Рефакторинг useAuth/useFavorites/шапки; коммиты (не коммить — примет ревьюер).
