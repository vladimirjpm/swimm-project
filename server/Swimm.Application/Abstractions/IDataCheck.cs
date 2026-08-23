using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Одна проверка данных (docs/data-integrity.md, фаза Д3). Единая абстракция вместо россыпи:
/// до неё проверки жили в семи разных сервисах, у каждого свой UI, свой запуск и никакой
/// истории.
///
/// Существующие сервисы НЕ переписываются — поверх них пишутся адаптеры. Реестр даёт то,
/// чего не было ни у одного: единый прогон, severity, история и одно место для человека.
///
/// Проверка обязана быть ЧИТАЮЩЕЙ: она ставит диагноз, лечение — отдельное осознанное
/// действие (переимпорт, merge, кнопка в админке).
/// </summary>
public interface IDataCheck
{
    /// <summary>Стабильный идентификатор вида <c>results.exact-duplicate</c>. По нему
    /// склеиваются находки между прогонами, поэтому переименование = потеря истории.</summary>
    string Id { get; }

    /// <summary>Заголовок для человека.</summary>
    string Title { get; }

    /// <summary>Что именно ищет и почему это плохо — показывается рядом со списком.</summary>
    string Description { get; }

    /// <summary>Насколько это серьёзно: Error — испорченные данные, Warning — подозрительно,
    /// Info — есть что почистить.</summary>
    DataCheckSeverity Severity { get; }

    /// <summary>Запуск. Возвращает найденное; пустой список = всё в порядке.</summary>
    Task<DataCheckOutcome> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Прогон всех проверок и работа с находками (docs/data-integrity.md, фаза Д3).
/// </summary>
public interface IDataCheckRunner
{
    /// <summary>Прогнать все зарегистрированные проверки и записать результат.</summary>
    Task<DataCheckRunDto> RunAllAsync(string trigger, CancellationToken ct = default);

    /// <summary>Текущие находки (незакрытые + принятые), сгруппированные по проверке.</summary>
    Task<IReadOnlyList<DataCheckGroupDto>> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>История прогонов, свежие первыми.</summary>
    Task<IReadOnlyList<DataCheckRunDto>> GetHistoryAsync(int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Итог последнего прогона по каждой проверке (полные числа, без среза списка) + сам
    /// последний прогон. Дашборд берёт свои счётчики отсюда, чтобы не считать то же самое
    /// второй раз и не расходиться в цифрах с /Admin/Health. Пусто — реестр ни разу не гонялся.
    /// </summary>
    Task<(DataCheckRunDto? LastRun, IReadOnlyList<DataCheckStateDto> States)> GetStateAsync(
        CancellationToken ct = default);

    /// <summary>
    /// «Принято как есть»: находка неустранима (ошибка в протоколе федерации, особенность
    /// данных). Переживает следующие прогоны — иначе решение пришлось бы принимать заново
    /// каждый раз, как это уже сделано для ручных пометок качества результатов.
    /// </summary>
    Task<bool> AcceptAsync(int findingId, string? note, CancellationToken ct = default);

    /// <summary>
    /// Точечное исправление находки прямо из списка: проставить пол пловцу и его строкам,
    /// у которых пола нет. Возвращает, сколько строк результата поправлено, либо null,
    /// если находка не найдена или у неё нет такого исправления.
    ///
    /// Почему заодно правим и результаты: проверка смотрит на `Results.Gender`, а он
    /// заполняется НА ИМПОРТЕ из пловца. Поставить пол только пловцу — значит оставить
    /// находку висеть до переимпорта, то есть кнопка выглядела бы сломанной.
    /// </summary>
    Task<int?> FixSwimmerGenderAsync(int findingId, string gender, CancellationToken ct = default);

    /// <summary>
    /// Выровнять пол пловца по находке `results.gender-vs-card`: ставит выбранный пол в
    /// карточку И приводит к нему ВСЕ его личные строки. Эстафеты не трогает — там пол
    /// команды, а не человека. Возвращает число поправленных строк; null — находка не
    /// найдена, не поддерживает исправление или пол задан неверно.
    /// </summary>
    Task<int?> AlignSwimmerGenderAsync(int findingId, string gender, CancellationToken ct = default);

    /// <summary>
    /// Привязать правило клубных очков к соревнованию находки (fixKind
    /// <c>competition-club-rule</c>) и пересчитать его зачёт. false — находки нет,
    /// у неё другое исправление или правило неизвестно.
    ///
    /// Правило ставится всем дням события: регламент у многодневного старта один, и
    /// привязка «через день» дала бы разный зачёт у дней одного чемпионата.
    /// </summary>
    Task<bool> FixCompetitionClubRuleAsync(int findingId, int ruleId, CancellationToken ct = default);

    /// <summary>
    /// Массовое исправление той же находки: у пловца пол уже известен (приехал из другого
    /// протокола), а спорная строка пустая — тогда «правильный» ответ уже есть в базе и
    /// кликать по каждой находке нечего. Правит ТОЛЬКО такие: где пол пловца задан.
    /// Находки, где пол неизвестен и у пловца, остаются человеку.
    /// </summary>
    Task<(int Findings, int Rows)> FixAllKnownSwimmerGendersAsync(CancellationToken ct = default);

    /// <summary>Вернуть принятую находку в работу.</summary>
    Task<bool> ReopenAsync(int findingId, CancellationToken ct = default);
}
