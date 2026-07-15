# План: Фаза 4 — логин на клиенте (2026-07-15)

Хендофф-план для реализации роадмап-фазы 4 (`docs/ROADMAP.md` §Фаза 4). Решение по
архитектуре принято: **cookie-based auth остаётся единственным механизмом** (httpOnly-кука +
SecurityStamp + antiforgery — уже построено на сервере, auth-фазы 0–4 закрыты). Никаких JWT
в localStorage. Клиент делает только UI; сервер — единственный владелец сессии.

UI-решение: **модал логина в `HomeHeader`** (не отдельная страница) — шапка есть на всех
новых страницах (home/competitions/groups), контекст не теряется. Клиент multi-page (без
SPA-роутера), поэтому состояние auth не шарится между страницами через store — каждая
страница сама зовёт `/auth/me` (это уже так в `useFavorites`/`useCurrentIdentity`).

## Фон (что уже есть на сервере — НЕ трогать, только вызывать)

- `GET /auth/login/google?returnUrl=` → OAuth-редирект, после колбэка кука стоит,
  браузер возвращается на `returnUrl`.
- `POST /auth/login/local` `{email, password}` → 200 (кука) | 401 invalid | 403
  email-not-confirmed | 429 lockout. Rate-limited (`[EnableRateLimiting("auth")]`).
- `POST /auth/register` `{email, password, displayName?}` — rate-limited; письмо-verify
  через `IEmailSender` (dev — логирует ссылку в консоль API).
- `POST /auth/forgot-password` `{email}` — всегда 200 (no user-enumeration).
- `GET /auth/verify-email?token=`, `GET|POST /auth/reset-password` — серверные HTML-страницы,
  остаются серверными (клик из письма).
- `GET /auth/me` → `{isAuthenticated, id, email, displayName, avatarUrl, roles[], swimmerId}`;
  503 при недоступной БД.
- `GET /auth/logout?returnUrl=` (текущая сессия), `POST /auth/logout-all` (бамп SecurityStamp).
- Клиентские заготовки: `useFavorites.ts` и `use-my-hub-groups.ts:useCurrentIdentity` уже
  дёргают `/auth/me` — это дублирование и есть повод для `useAuth`.

## Шаг 1. `useAuth`-хук + user-меню в шапке (4.1) — **Fable** ✅ выполнен 2026-07-15

Шов, на который сядет всё остальное; делает Fable как первый паттерн.

- `client/src/hooks/useAuth.ts`: обобщить `/auth/me`-логику из `useCurrentIdentity`
  (`{isAuthenticated, userId, displayName, avatarUrl, roles, isAdmin, swimmerId, loading}` +
  `refresh()`). Модульный кэш на страницу (один запрос на load, как cachedToken в
  use-my-hub-groups).
- Мигрировать `useCurrentIdentity` (hub-groups) и auth-часть `useFavorites` на `useAuth` —
  убрать дублирование, поведение не менять.
- User-меню в `HomeHeader`: гость → кнопка «Войти» (открывает модал шага 2); залогинен →
  аватар/имя + дропдаун: Sign out (`GET /auth/logout?returnUrl=<текущая>`),
  Sign out everywhere (`POST /auth/logout-all`, после — `refresh()`).
- Тесты: серверных изменений нет; клиентский typecheck + визуальная проверка.

## Шаг 2. Модал логина (4.2) — **Fable** (вместе с шагом 1, один коммит) ✅ выполнен 2026-07-15

- Компонент `LoginModal` (портал поверх страницы, стиль карточек hub-groups: `hp-card-std`,
  тёмный неон).
- Кнопка «Sign in with Google» → обычный `<a href="/auth/login/google?returnUrl=${encodeURIComponent(window.location.href)}">`.
- Форма email/пароль → `POST /auth/login/local` (fetch, `credentials:'include'`,
  Content-Type json). Обработка ответов: 401 «неверный email/пароль», 403 «подтверди email»
  (+ кнопка «выслать снова» — НЕТ такого эндпоинта, просто текст), 429 «слишком много
  попыток, позже», 200 → закрыть модал + `refresh()`.
- Ссылки «Регистрация» и «Забыл пароль» — переключают вкладку модала (шаг 3), до его
  реализации можно скрыть.

## Шаг 3. Регистрация + «забыл пароль» в модале (4.3) — **Sonnet** (после шага 2) ✅ выполнен 2026-07-15 (принят)

Механика по готовому паттерну модала.

- Вкладка «Регистрация»: `POST /auth/register` → 200: «письмо отправлено, подтверди email»
  (в dev письмо в консоли API — написать это в dev-подсказке не надо, юзер прода не увидит).
  Обработка 400 (валидация пароля/email занят) — показать `error` из ответа.
- Вкладка «Забыл пароль»: `POST /auth/forgot-password` → всегда «если такой email есть,
  письмо отправлено» (сервер не раскрывает существование).
- Verify/reset остаются серверными страницами — клиент на них НЕ ссылается, туда ведут
  ссылки из писем.
- Тесты: клиентский typecheck; ручной прогон register→verify(из консоли)→login.

## Шаг 4. Прод-`IEmailSender` (4.4) — **Fable** (секреты/прод-конфиг) ✅ выполнен 2026-07-15

Реализовано как `SmtpEmailSender` (встроенный `SmtpClient`, без новых пакетов — SMTP-endpoint
дают и Resend/Postmark/SendGrid). Включение: секция `Email:Smtp` (`Host`, `Port`=587,
`EnableSsl`=true STARTTLS, `User`, `Password`, `From`, `FromName`) через env
`Email__Smtp__*`/user-secrets; `Host` пуст → остаётся `LoggingEmailSender`.
Тесты выбора реализации: `EmailSenderRegistrationTests` (3 шт.).

- Реализация `SmtpEmailSender` (или Resend HTTP API — решить по тому, что будет у хостинга)
  в `Swimm.Infrastructure`, регистрация по конфигу: нет SMTP-настроек → остаётся
  `LoggingEmailSender` (dev как сейчас).
- Секреты — только через env/user-secrets, НЕ в appsettings.json в репо.
- Без этого email/пароль в проде не работает; Google-логин работает и так.

## Шаг 5. CTA «залогинься» в Favorites-UI (4.5) — **Sonnet** (мелкий) ✅ выполнен 2026-07-15 (принят)

- Места, где функционал избранного скрыт для гостя, показывают кнопку «Войти»
  (открывает LoginModal / ведёт в шапку) вместо полного скрытия.
- Инвентаризация мест — по `useFavorites` потребителям.

## Порядок

1+2 (Fable, один коммит) → 3 (Sonnet `/delegate`) и 5 (Sonnet, можно параллельно 3).
4 — независим, в любой момент до прода.

**Критерий приёмки фазы:** полный цикл register → verify → login → favorites → logout-all
из UI клиента без curl. ✅ Пройден 2026-07-15 (verify-ссылка из консоли API; favorites
POST 201; logout-all → шапка вернулась к «Sign in» без перезагрузки). Плюс наш локальный мотив: залогиниться в browser-preview и
прокликать панель «Мои группы» (шаги 2/4/5 media-плана были проверены только статически).

## Footguns

- Build-lock: VS/предыдущий `dotnet run` держит :5078 — см. корневой CLAUDE.md.
- `/auth/*` идёт через Vite-proxy — fetch строго относительными URL, `credentials:'include'`.
- Auth-POSTы (`login/local`, `register`, `forgot-password`) НЕ требуют antiforgery-токена
  (у гостя нет сессии), но rate-limited — при тестах не долбить, лимит залипнет (429).
- `returnUrl` для Google-логина обязателен, иначе вернёт на `/` (серверный корень, не Vite).
  В dev вернёт на :5078 — это известное неудобство, проверять логин-флоу целиком лучше
  через прод-сборку клиента, отданную сервером, либо просто руками вернуться на :5173.
- `/auth/me` может ответить 503 (БД недоступна) — `useAuth` должен трактовать как гость,
  не как ошибку рендера.
- Модал — новый общий компонент; НЕ класть его в `projects/home-project`, а в
  `projects/components/` (используется с нескольких страниц).
