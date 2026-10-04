# ПЕРЕДАЧА: дубли медиа на соревновании — «My media 15», а в табе Media 10 (30.09.2026)

Жалоба Влада (30.09.2026, `http://localhost:5173/results?tab=media&swimmerId=6066&eventId=7`):
1. в персональной полосе шапки соревнования «MY MEDIA — 15», а в табе Media — 10;
2. «не могу добавить вторую фотку на соревнование (не на заплыв)».

Разбор сделан. **30.09.2026 Влад выбрал из §4 только п.1** (запрет дубля; сравнение — trim +
Drive по id файла) — **сделано**, см. [media-page.md §9](../media-page.md) «Одна ссылка к одной
цели». **П.3 сделан 01.10.2026** по «да» Влада: удалены 46, 47, 48, 49, 54 (оставлена 55 — день 1487,
видна и на /my-media); публикаций, жалоб и лайков у них не было. Второе фото Влад добавил сам
(id 59, другой файл). Причина «не добавить» подтверждена: вставлялась ссылка уже загруженного
файла, превью честно показывало его. Заодно починены карточка /my-media (медиа дня без заплывов)
и прозрачный модал на странице результатов — см. media-page.md §9. П.2 (полоса считает как таб)
не делался — после чистки дублей числа совпадают.

## 1. Что в базе (аккаунт Влада, UserId = 1; событие 7 = дни 1486 и 1487, пловчиха 6066)

15 строк `Sys_UserMedia`:
- **9 уровня `result`** (к заплывам 6031257 и 6031921) — все разные ссылки: id 38, 39, 40, 41, 43,
  44, 45, 51, 53;
- **6 уровня `competition`** — ОДНА И ТА ЖЕ ссылка
  `https://drive.google.com/file/d/1RH1YV22pp1ubIjvDlbxwSNsJIWOQVYOL/view`:

| id | MediaType | CompetitionId | создана |
|---|---|---|---|
| 46 | image | 1486 | 20:02:34 |
| 47 | **video** | 1486 | 20:06:56 |
| 48 | image | 1486 | 20:08:35 |
| 49 | **video** | 1486 | 20:08:40 |
| 54 | image | 1486 | 20:16:06 |
| 55 | image | 1487 | 20:16:36 |

47 и 49 — фото, сохранённое как «видео» (переключатель Video/Photo у ссылки `OTHER`).

Запрос для проверки:
```sql
select "Id","Level","CompetitionId","ResultId","MediaType","Url","CreatedAt"
from "Sys_UserMedia" where "UserId"=1
  and "CompetitionId" in (select "Id" from "Competitions" where "EventId"=7) order by "Id";
```

## 2. Почему числа расходятся

- **Полоса «MY MEDIA — N»** — `client/src/projects/results-main-project/components/competition-header/competition-personal-strip.tsx`:
  `GET /api/me/media`, фильтр `competition_id ∈ дни события`, **без склейки** → 15.
- **Таб Media** — `client/src/hooks/useCompetitionMedia.ts` → `GET /api/media/results?eventId=7` →
  `UserMediaPublicationService.GetVisibleForResultsAsync` (`server/Swimm.Infrastructure/Services/UserMediaPublicationService.cs`, ~стр. 483):
  **склейка `GroupBy(ResultId, Url)`** → шесть одинаковых competition-ссылок = 1 плитка → 9 + 1 = 10.
  Ключ склейки без `CompetitionId` — поэтому и 55 (другой день) слипается с 46.

## 3. Почему «не добавить вторую фотку»

Форма (`my-media-project/components/add-link-modal.tsx`, из шапки — `use-competition-add-media.tsx`,
single-step с `fixedCompetitionId` = первый день события) **открывается пустой**: модал
размонтируется при закрытии, `initialUrl` из шапки не передаётся. Проверено на стенде 5183+5082
персонажем `utest-parent`: две РАЗНЫЕ ссылки «ко всему соревнованию» подряд — обе сохранились и обе
видны в `/api/media/results?eventId=7` (тестовые строки удалены).

Вывод: каждый раз уходила та же ссылка (скорее всего, в буфере оставалась первая — в Drive легко
скопировать ссылку не того файла), а сайт молча заводил дубль, и в табе Media он «исчезал» при
склейке. Ошибки не видно ни на одном шаге.

## 4. Предложение — ждёт «да» Влада

1. **Запрет дубля на сервере**: `POST /api/me/media` — та же ссылка у того же юзера к той же цели
   (тот же `ResultId`, либо тот же `CompetitionId` при уровне `competition`, либо уровень `swimmer`
   того же пловца) → 409 с текстом «This link is already added here». Модал сейчас при неуспехе
   молча остаётся открытым (`add-link-modal.tsx`, `save` → `onSave` false) — нужна строка ошибки.
   Место проверки — `UserMediaRepository.AddAsync` / `MediaController` (`POST /api/me/media`);
   сравнивать по нормализованному URL (trim; для Drive — по id файла? решить).
2. ✅ **Сделано 04.10.2026** — полоса склеивает по `(result_id, url)`, как таб
   (`competition-personal-strip.tsx`); проверено вживую на трёх записях `utest-parent` (2 одинаковые +
   1 другая → полоса 2, таб 2). **Полоса считает как таб**: склейка по `(result_id, url)` и в `competition-personal-strip.tsx`
   (или отдавать полосе тот же список, что табу), чтобы 15/10 не повторилось на старых дублях.
3. ✅ **Уже не нужно (сверено 04.10.2026)** — из записей 46, 47, 48, 49, 54, 55 в базе осталась
   только 55; дубли удалены раньше. **Чистка дублей Влада** — ТОЛЬКО после его «да» (это его данные): оставить одну запись
   (кандидат — 46, image, день 1486), удалить 47, 48, 49, 54, 55. Проверить, нет ли у них
   публикаций (`Sys_UserMediaPublications` — уйдут каскадом).

Плюс тесты: сервис (дубль → отказ, разные ссылки → обе), клиент — вживую на стенде.

## 5. Как поднять и проверить

- Стенд `stack-5183-api5082-personas` (API :5082 + Vite :5183) — не трогает API Влада на :5078.
  Первая навигация после старта часто «denied or failed» — повторить.
- Персонаж: `http://localhost:5183/api/dev/persona?as=utest-parent` (есть избранные — пикер пловца
  не пустой). Аккаунт Влада (`default`) — только смотреть, данные не менять.
- Страница: `http://localhost:5183/results?tab=media&eventId=7`; кнопка «＋ Add media» в шапке
  (полоса «My media» видна, только если у зрителя есть медиа/избранные на соревновании).
- После проверки — удалить тестовые строки `Sys_UserMedia` и остановить стенд (`preview_stop`),
  иначе Debug-сборка заблокирована.

## 6. Контекст

- Справочник медиа — [media-page.md](../media-page.md) (§3 модель, §4 API, §5 Add link, §9 footguns).
- Ветка `entity-hero-roles`, PR #100 открыт; последний коммит сессии `25a12570`. Остальная работа
  сессии — [entity-hero-roles-handoff.md](entity-hero-roles-handoff.md).
- В рабочей копии файлы Влада (`docs/plans/lane-plans-plan.md`, `!design_handoff/…`) — коммитить
  только свои пути, `git add -A` нельзя.
