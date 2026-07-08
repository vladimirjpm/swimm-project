namespace Swimm.API.Pages.Admin.Shared;

/// <summary>Один параметр эндпоинта (для таблицы документации и Run-формы).</summary>
public sealed record ApiParam(
    string Name,
    string Type,   // string | int | long | bool | DateTime
    string In,     // query | path | body
    string Description,
    string? DefaultValue = null,
    string? InputWidth = null,     // например "70px" — только для точного повторения прежней вёрстки
    bool BoolFalseFirst = false);  // порядок опций select для type=bool (иначе true,false)

/// <summary>
/// Один эндпоинт в каталоге. Рендерится через Shared/_ApiEndpointCard.cshtml.
/// Highlighted-карточки (Import/Clear/Enrich) показывают BodyShape/ResponseShape вместо .desc
/// и опциональную Note вместо Run-кнопки (для не запускаемых напрямую, например upload файла).
/// </summary>
public sealed record ApiEndpoint(
    string Method,
    string Path,
    string Description,
    IReadOnlyList<ApiParam>? Params = null,
    string? AuthBadge = null,       // null | "Auth" | "Admin"
    bool Highlighted = false,
    string? HighlightBorderClass = null, // например "border-l-[#66bb6a]" — только при Highlighted
    string? BodyShape = null,
    string? ResponseShape = null,
    string? Note = null,
    bool Runnable = true);

/// <summary>Секция с заголовком (h2); пустой Title — карточки без группировки.</summary>
public sealed record ApiGroup(string Title, IReadOnlyList<ApiEndpoint> Endpoints);

/// <summary>
/// Типизированный каталог API-эндпоинтов для страницы /Admin/Api.
/// Добавление эндпоинта в документацию = запись здесь, без правки .cshtml.
/// </summary>
public static class ApiCatalog
{
    public static readonly IReadOnlyList<ApiGroup> PublicGroups =
    [
        new("🔐 Auth",
        [
            new("GET", "/auth/login/google",
                "Перенаправление на Google OAuth. После авторизации редирект на returnUrl.",
                [new ApiParam("returnUrl", "string", "query", "URL для редиректа после входа (по умолчанию \"/\")", "/")]),

            new("GET", "/auth/me", "Текущий пользователь. Возвращает профиль или { isAuthenticated: false }."),

            new("GET", "/auth/logout",
                "Выход из системы. Редирект на returnUrl.",
                [new ApiParam("returnUrl", "string", "query", "URL для редиректа после выхода (по умолчанию \"/\")", "/")],
                AuthBadge: "Auth"),
        ]),

        new("📊 Results",
        [
            new("GET", "/api/results",
                "Получить результаты соревнований с фильтрацией и пагинацией.",
                [
                    new ApiParam("competition", "string", "query", "Фильтр по названию соревнования (StartsWith)"),
                    new ApiParam("name", "string", "query", "Фильтр по имени/фамилии пловца (StartsWith)"),
                    new ApiParam("club", "string", "query", "Фильтр по клубу (StartsWith)"),
                    new ApiParam("styleName", "string", "query", "Фильтр по стилю (exact match)"),
                    new ApiParam("distance", "string", "query", "Фильтр по дистанции (exact match)"),
                    new ApiParam("gender", "string", "query", "Фильтр по полу (exact match)"),
                    new ApiParam("poolType", "string", "query", "Фильтр по типу бассейна (exact match)"),
                    new ApiParam("dateFrom", "DateTime", "query", "Дата начала диапазона"),
                    new ApiParam("dateTo", "DateTime", "query", "Дата конца диапазона"),
                    new ApiParam("page", "int", "query", "Номер страницы (по умолчанию 1)", "1", "60px"),
                    new ApiParam("pageSize", "int", "query", "Размер страницы (по умолчанию 100, макс. 500)", "20", "70px"),
                ]),

            new("GET", "/api/results/{id}",
                "Получить один результат по ID.",
                [new ApiParam("id", "long", "path", "ID результата", "1", "80px")]),
        ]),
    ];

    public static readonly IReadOnlyList<ApiGroup> AdminGroups =
    [
        new("🔧 Admin",
        [
            new("GET", "/api/admin/users", "Список пользователей и их ролей.", AuthBadge: "Admin"),
            new("GET", "/api/admin/roles", "Список доступных ролей.", AuthBadge: "Admin"),

            new("POST", "/api/admin/users/{userId}/roles/{roleId}",
                "Назначить роль пользователю.",
                [
                    new ApiParam("userId", "int", "path", "ID пользователя", "1", "70px"),
                    new ApiParam("roleId", "int", "path", "ID роли", "1", "70px"),
                ],
                AuthBadge: "Admin"),

            new("DELETE", "/api/admin/users/{userId}/roles/{roleId}",
                "Снять роль с пользователя.",
                [
                    new ApiParam("userId", "int", "path", "ID пользователя", "1", "70px"),
                    new ApiParam("roleId", "int", "path", "ID роли", "1", "70px"),
                ],
                AuthBadge: "Admin"),

            new("PATCH", "/api/admin/users/{userId}/active",
                "Активировать/деактивировать пользователя.",
                [
                    new ApiParam("userId", "int", "path", "ID пользователя", "1", "70px"),
                    new ApiParam("isActive", "bool", "body", "true — активировать, false — деактивировать"),
                ],
                AuthBadge: "Admin"),

            new("GET", "/api/admin/stats",
                "Статистика для дашборда: кол-во пользователей, результатов, соревнований, пловцов, клубов.",
                AuthBadge: "Admin"),

            new("GET", "/api/admin/db-schema",
                "Структура схемы БД: таблицы, колонки, FK, индексы, CHECK, процедуры, вьюхи.",
                [new ApiParam("refresh", "bool", "query", "Пересчитать кэш схемы (по умолчанию false)", BoolFalseFirst: true)],
                AuthBadge: "Admin"),

            new("GET", "/api/admin/clearable-tables",
                "Список таблиц, очищаемых при вызове import/clear. Источник: <code>JsonImportService.ClearableTables</code>.",
                AuthBadge: "Admin"),

            new("GET", "/api/admin/settings", "Все настройки сервера.", AuthBadge: "Admin"),

            new("PUT", "/api/admin/settings/{key}",
                "Обновить значение настройки.",
                [
                    new ApiParam("key", "string", "path", "Ключ настройки"),
                    new ApiParam("value", "string", "body", "Новое значение"),
                ],
                AuthBadge: "Admin"),
        ]),

        // Отдельные карточки без общего заголовка — как в исходной вёрстке.
        new("",
        [
            new("POST", "/api/admin/import",
                "Импорт результатов из JSON-файла (multipart/form-data). Заполняет справочники и таблицу Results.",
                Highlighted: true, HighlightBorderClass: "border-l-[#66bb6a]",
                BodyShape: "<code>multipart/form-data</code> с полем <code>file</code> (.json)",
                ResponseShape: "{ totalRows, created, skipped, errors, errorMessages[], message }",
                Note: "Используйте страницу <a href=\"/Admin/Import\" class=\"text-admin-accent\">📥 Import</a> для загрузки файлов.",
                Runnable: false),

            new("DELETE", "/api/admin/import/clear",
                "Полная очистка импортированных данных. Удаляет Results, Competitions, Clubs, Swimmers, Countries, Relays, Galleries, GalleryItems. Styles и пользователи сохраняются.",
                Highlighted: true, HighlightBorderClass: "border-l-[#ef5350]",
                ResponseShape: "{ message, deleted: { total, results, competitions, clubs, swimmers, countries, relays, galleries, galleryItems } }"),

            new("POST", "/api/admin/swimmers/enrich",
                "Дополнение данных спортсменов (Gender, ClubId, CountryId) из таблицы Results. Заполняет пустые поля на основе самого свежего результата каждого спортсмена. Используется для обратного заполнения ранее импортированных данных.",
                AuthBadge: "Admin",
                Highlighted: true, HighlightBorderClass: "border-l-[#ffa726]",
                ResponseShape: "{ message, updated }"),

            new("GET", "/api/admin/competition-events",
                "Список многодневных событий (для привязки дней при импорте).",
                AuthBadge: "Admin",
                ResponseShape: "[{ id, name, startDate, endDate, dayCount, resultCount }]"),

            new("GET", "/api/admin/import-history",
                "История импортов с названиями соревнований. Возвращает массив записей, отсортированных по дате (новые первыми).",
                AuthBadge: "Admin",
                ResponseShape: "[{ id, competitionId, competitionName, competitionDate, importFileName, importDate, approved }]"),

            new("PATCH", "/api/admin/import-history/{id}/approve",
                "Обновить статус подтверждения импорта.",
                [
                    new ApiParam("id", "int", "path", "ID записи истории импорта", "1", "70px"),
                    new ApiParam("approved", "bool", "body", "true — подтвердить, false — отменить подтверждение"),
                ],
                AuthBadge: "Admin"),
        ]),
    ];
}
