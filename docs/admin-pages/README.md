# docs/admin-pages — карта админки

**Конвенция:** один MD на страницу админки. Имя файла = маршрут без `/Admin/`,
в нижнем регистре, `/` → `-`: `/Admin/Swimmers/Loglig` → `swimmers-loglig.md`,
дашборд `/Admin` → `index.md`. Новая страница в `Pages/Admin/**` = новый MD здесь
(и строка в списке ниже) — это часть definition of done.

Каждый MD отвечает на: что делает страница, какие файлы (cshtml/контроллер/сервис),
какие API дергает, ключевые особенности/грабли.

| Маршрут | MD | Суть |
|---|---|---|
| /Admin | [index.md](index.md) | Дашборд: пользователи, роли, статистика, «требует внимания» |
| /Admin/Swimmers | [swimmers.md](swimmers.md) | Дубли пловцов: кандидаты, dry-run, склейка, сироты |
| /Admin/Swimmers/Loglig | [swimmers-loglig.md](swimmers-loglig.md) | Привязка пловцов к loglig.com |
| /Admin/Clubs | [clubs.md](clubs.md) | Дубли клубов: кандидаты, dry-run, склейка |
| /Admin/Competitions | [competitions.md](competitions.md) | Справочник соревнований — эталонный CRUD |
| /Admin/CompetitionsHub | [competitionshub.md](competitionshub.md) | β: Competitions + Discovery одним списком со стадией |
| /Admin/Categories | [categories.md](categories.md) | Категории соревнований (бейджи) |
| /Admin/Records | [records.md](records.md) | Рекорды и нормативы уровней |
| /Admin/HubGroups | [hubgroups.md](hubgroups.md) | Группы SwimHub |
| /Admin/HubGroupClubRequests | [hubgroupclubrequests.md](hubgroupclubrequests.md) | Заявки групп на статус клуба |
| /Admin/Import | [import.md](import.md) | Импорт соревнований (JSON/PDF) и рекордов |
| /Admin/ImportHistory | [importhistory.md](importhistory.md) | История импортов |
| /Admin/Discovery | [discovery.md](discovery.md) | Автозабор соревнований с isr.org.il |
| /Admin/Users | [users.md](users.md) | Пользователи и сессии |
| /Admin/Db | [db.md](db.md) | Схема БД |
| /Admin/Api | [api.md](api.md) | Каталог эндпоинтов + браузер данных |
| /Admin/Settings | [settings.md](settings.md) | Параметры приложения (in-memory settings) |
| /Admin/UiPreview | [uipreview.md](uipreview.md) | Витрина компонентов админки |

Общее для всех страниц: Razor Pages в `server/Swimm.API/Pages/Admin/**`,
layout `_Layout.cshtml`, сайдбар — `Shared/AdminUi.cs` (массив `AdminNavItem`).
Все admin-API — `[Authorize(Roles="Admin")]` + `[AutoValidateAntiforgeryToken]`
(JS шлёт заголовок `X-XSRF-TOKEN` из `_csrfToken`). В Development работает
DevAdminBypass (синтетический админ без логина). CSS — общий Tailwind-бандл
`wwwroot/css/admin.min.css` (`npm run css:build` при новых классах).
