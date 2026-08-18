using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт админского CRUD соревнований (эталон для будущих CRUD: Clubs, Swimmers, …).
/// Каскадное удаление живёт в <see cref="IImportService.DeleteCompetitionAsync"/> — здесь
/// только чтение/создание/обновление и управление <see cref="CompetitionResultUrlDto"/> по OrgCompId.
/// </summary>
public interface ICompetitionAdminRepository
{
    /// <summary>Список с поиском (по Name/SubName), фильтром по категории/сезону (году) и пагинацией.
    /// Многодневные соревнования свёрнуты в одну строку-событие (<see cref="CompetitionRowDto"/>).</summary>
    /// <param name="orgCompId">Если задан — список сужается до соревнования (или всего его
    /// события) с этим OrgCompId. Для точного перехода со страницы Discovery на нужную строку.</param>
    Task<PagedResult<CompetitionRowDto>> GetPagedAsync(string? search, string? categoryKey, int? year, int page, int pageSize, int? orgCompId = null);

    /// <summary>
    /// Объединённый список Competitions + Discovery (страница /Admin/Competitions):
    /// каждое соревнование одной строкой с <see cref="CompetitionStage"/> (на сайте / импортировано /
    /// только в БД / скрыто). Склейка по OrgCompId (+ fallback имя+дата). Фильтры поиск/категория/
    /// сезон применяются к БД-стороне; <paramref name="stage"/> — по стадии. Сортировка по дате (убыв.).
    /// <paramref name="showSynthetic"/>=false (по умолч.) прячет тестовые синтетические соревнования
    /// (Name начинается с «SYNTH »). <paramref name="month"/> (1–12, null — все) фильтрует по месяцу;
    /// результат несёт счётчики по всем 12 месяцам (без учёта самого month-фильтра) для кнопок.
    /// </summary>
    /// <param name="qualityFilter">T3b deep-link: discovery-error | no-org-comp-id | no-results —
    /// доп. WHERE поверх остальных фильтров (те же предикаты, что считает DashboardStatusService).
    /// Неизвестное значение/null — не фильтрует.</param>
    /// <param name="season">Сезон (сен–авг) по году окончания: 2025 = сезон 24/25. Применяется
    /// и к БД-строкам, и к строкам с сайта. null — все.</param>
    /// <param name="kind">«champ» — только чемпионаты Израиля (по названию: אליפות + ישראל).
    /// null/пусто — все.</param>
    /// <param name="discipline">Вид спорта (<c>Disciplines</c>): null/пусто — только плавание
    /// (дефолт, артистическое прячем), «all» — всё, иначе конкретная дисциплина. Скрытые не
    /// удаляются, а считаются: <c>UnifiedCompetitionList.HiddenByDiscipline</c>.</param>
    Task<UnifiedCompetitionList> GetUnifiedAsync(
        string? search, string? categoryKey, int? season, string? stage, bool showSynthetic, int? month, int page, int pageSize,
        string? qualityFilter = null, string? kind = null, string? discipline = null);

    /// <summary>Полные данные для формы Edit (включая URL-ы результатов). null — не найдено.</summary>
    Task<CompetitionEditDto?> GetByIdAsync(int id);

    /// <summary>Все категории (для чекбоксов формы), по DisplayOrder.</summary>
    Task<IReadOnlyList<CategoryTagDto>> GetAllCategoriesAsync();

    /// <summary>Годы, встречающиеся в Date соревнований, по убыванию — для фильтра /AssignRules.</summary>
    Task<IReadOnlyList<int>> GetAvailableYearsAsync();

    /// <summary>Сезоны (сен–авг, по году окончания) из Date соревнований И дат строк с сайта,
    /// по убыванию — для фильтра объединённого списка.</summary>
    Task<IReadOnlyList<int>> GetAvailableSeasonsAsync();

    /// <summary>Создать. Ошибки уникальности (Name+Date+PoolType, OrgCompId) возвращаются в результате.</summary>
    Task<CompetitionSaveResult> CreateAsync(CompetitionInputDto input);

    /// <summary>Обновить основные поля. null-возврат Success=false с текстом при конфликте/отсутствии.</summary>
    Task<CompetitionSaveResult> UpdateAsync(int id, CompetitionInputDto input);

    /// <summary>
    /// Быстрая правка из списка: общие поля (бассейн, флаги, категории, правила очков)
    /// применяются ко ВСЕМ дням события. Id в результате — число изменённых дней.
    /// </summary>
    Task<CompetitionSaveResult> QuickUpdateAsync(CompetitionQuickEditDto input);

    // ── Привязка правил очков (Э4) ─────────────────────────────────────────────

    /// <summary>
    /// Соревнования для массовой привязки правил: все дни, а не свёрнутые события —
    /// правило хранится у каждого дня отдельно. Фильтры: сезон (год), <paramref name="scope"/>
    /// (<c>all</c> | <c>masters</c> | <c>non-masters</c>) и «только без привязки»
    /// (нет ни клубного правила, ни правила пловца). Синтетика (SYNTH…) исключена.
    /// </summary>
    Task<IReadOnlyList<CompetitionRuleRowDto>> GetForRuleAssignmentAsync(int? year, string scope, bool onlyUnassigned);

    /// <summary>Проставить правила выбранным соревнованиям. Возвращает число изменённых строк
    /// либо текст ошибки (несуществующее правило).</summary>
    Task<CompetitionSaveResult> AssignRulesAsync(CompetitionRuleAssignmentDto assignment);

    /// <summary>Id всех дней события (включая «голову»), по возрастанию DayNumber.</summary>
    Task<IReadOnlyList<int>> GetEventDayIdsAsync(int eventId);

    // ── CompetitionResultUrls (связь по OrgCompId) ─────────────────────────────

    /// <summary>Добавить URL результатов. Требует заданного OrgCompId; проверяет уникальность (OrgCompId, Culture).</summary>
    Task<CompetitionSaveResult> AddResultUrlAsync(int orgCompId, string culture, string url);

    /// <summary>Удалить URL результатов по его Id, но только если он принадлежит соревнованию
    /// с этим OrgCompId (защита от удаления чужого URL по подделанному id). false — не найден/не совпал.</summary>
    Task<bool> RemoveResultUrlAsync(int urlId, int orgCompId);
}
