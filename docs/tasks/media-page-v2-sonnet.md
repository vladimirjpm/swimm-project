# Задание: страница My media v2 — вёрстка по дизайн-хендоффу

## Контекст

Есть готовый высокодетальный дизайн-хендофф:
`!design_handoff/design_handoff_media_page/` — **README.md там = основная спецификация,
следуй ему буквально** (структура, токены, размеры, поведение). Эталоны-прототипы:
`My Media Page.dc.html` (десктоп) и `My Media Mobile.dc.html` (390px) — открой их в
браузере и сверяйся визуально. Продуктовый бриф — `media-page-design-brief.md` там же.

Текущая реализация страницы — `client/src/projects/my-media-project/` (my-media.tsx,
use-all-my-media.ts) + `client/media.html` + `client/src/pages/media-page.tsx` —
это времянка, **заменяется целиком** новой вёрсткой (хук use-all-my-media можно
реиспользовать/расширить).

## ВАЖНЫЕ ПРАВИЛА

- **Весь UI — только English.** Данные (имена, группы, соревнования) — иврит, RTL
  per-element (`dir="rtl"`/`dir="auto"`), как в README.
- Видео — НИКОГДА сырой URL в iframe: только `UI_SwimmerGallery` (youtube-nocookie/vimeo).
- Тёмный стиль groups.html (README §Design Tokens) — прототип dark-only, темизацию
  через --theme-mode-* НЕ делать (осознанное решение: страница в семействе groups/home).

## API (всё готово на сервере, менять нельзя)

- `GET /api/me/media` — все медиа юзера: `{id, swimmer_id, swimmer_name, level,
  media_type, source_type, url, result_id, result_label, competition_id,
  competition_name, competition_date (dd/MM/yyyy), club_name, created_at}`.
- `GET /api/me/media/publications` — публикации: `{id, user_media_id, hub_group_id,
  hub_group_name, level: members|public, status: pending|approved|rejected, created_at, decided_at}`.
- `GET /api/me/media/{id}/publish-targets` — `{id, name}[]` групп, куда МОЖНО подать.
- `POST /api/me/media/{id}/publications` `{hub_group_id, level}`; `DELETE
  /api/me/media/{id}/publications/{groupId}`; `DELETE /api/me/media/{id}`;
  `POST /api/me/media` `{swimmer_id, media_type, source_type, url, result_id?}` (Add link).
- `GET /api/me/moderation/media` — сводный inbox (все статусы, pending первыми):
  строки как inbox группы + `hub_group_id, hub_group_name, owner_email, swimmer_name,
  result_label, url, media_type, source_type, level, status, created_at`.
  Решение: `POST /api/hub-groups/{hub_group_id}/media/publications/{id}/decision`
  `{approve: bool}` (antiforgery; паттерн apiFetch см. use-my-hub-groups.ts).
- Пикер заплывов: `GET /api/swimmers/{id}/competitions-brief` → `{id, name, date}[]`;
  `GET /api/swimmers/{id}/results-brief?competitionId=` → `{result_id, distance, style,
  time, date}[]`.
- Пловцы для Add link шаг 2: primary + favorites из `useFavoritesContext`/useFavorites
  (посмотри hooks/useFavorites.ts — там id и имена избранных) + уникальные swimmer_id
  из уже загруженных медиа.
- Признак «показывать Moderation»: `GET /api/me/moderation/media` вернул ≥1 строку ИЛИ
  `GET /api/me/hub-groups` (мои группы) непуст ИЛИ useAuth().isAdmin. Проще: показать
  таб, если непуст список моих групп (`useMyHubGroups(true)`) или isAdmin; бейдж —
  pending count из moderation feed.

## Решения

- Сезон для фильтра Season: год из `competition_date` (dd/MM/yyyy → yyyy). Без даты —
  элемент попадает только в "All".
- Производный статус карточки (для сегментов): нет публикаций → private; есть pending →
  pending; иначе есть approved → published; иначе rejected (README §State Management).
- More filters (Season/Competition/Club/Date range) — реализовать сразу, dropdowns в
  стиле chips (значения — уникальные из загруженных медиа).
- Фильтрация целиком на клиенте.
- Оптимистичные апдейты по README §Interactions; анимация ухода строки ~200мс — можно
  простым CSS transition (без библиотек).
- Файлы: перепиши `my-media.tsx` (можно разбить на компоненты в
  `my-media-project/components/`), `media.html`/`media-page.tsx` не трогай кроме
  необходимого. Хук use-all-my-media расширь под новые поля.

## Проверка

- `cd client && npx tsc --noEmit`.
- Живо: `npm --prefix client run dev` (или client-5079), открыть /media.html гостем —
  Sign in CTA, ошибок консоли нет. Сверить десктоп с эталоном `My Media Page.dc.html`
  и мобильный (resize 390px) с `My Media Mobile.dc.html`. Залогиненный флоу проверит Влад.

## Footguns

- Сервер не собирать/не трогать (build-lock).
- `tsc` из папки client.
- РТЛ: названия соревнований/групп на иврите — `dir` per-element, не на контейнере.
- Прототипы .dc.html — референс, не источник кода: не копируй их JS, пиши React.

## Вне скоупа

- Сервер; страницы групп/результатов; топбар (используй AppTopbar как есть);
  Settings-чип — disabled «soon» как в дизайне.
