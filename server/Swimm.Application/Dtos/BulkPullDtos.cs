using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Вердикт по одной строке пакетного забора: можно ли импортировать её без человека.
///
/// Наружу уходит СТРОКОЙ (JsonStringEnumConverter): панель различает вердикты по имени, а
/// глобального конвертера enum'ов у API нет — числовой «2» в разметке ничего не значит и
/// молча ломается при вставке нового значения в середину.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BulkPullVerdict>))]
public enum BulkPullVerdict
{
    /// <summary>Ничего решать не надо — импортируется автоматически.</summary>
    Clean,

    /// <summary>Импортируется, но регламент не нашёлся: медали/зачёт проставить нечем.</summary>
    NoRegulation,

    /// <summary>Нужно решение человека (рекорды, перезапись, событие, правило очков, warnings).</summary>
    NeedsReview,

    /// <summary>Тянуть нечего: протокол пуст.</summary>
    Empty,

    /// <summary>Забор или разбор упал — повод вернуться, а не закрытый вопрос.</summary>
    Failed
}

/// <summary>Строка пачки: что затянули и что с этим делать.</summary>
/// <param name="DiscoveredId">Строка «входящих».</param>
/// <param name="OrgCompId">compID на isr.org.il — по нему открывают первоисточник.</param>
/// <param name="Name">Имя соревнования (иврит) — как в списке.</param>
/// <param name="Date">Дата первого дня, dd/MM/yyyy.</param>
/// <param name="Verdict">Вердикт классификатора.</param>
/// <param name="Reasons">Почему так — по фразе на причину, показываются в панели.</param>
/// <param name="PreviewId">Ключ отложенного разбора; null — импортировать нечего.</param>
/// <param name="ResultCount">Сколько результатов в протоколе.</param>
/// <param name="RecordCount">Сколько рекордов побьёт файл.</param>
/// <param name="DayCount">Сколько дней (соревнований) в файле.</param>
/// <param name="ExistingCompetitionId">Уже есть в БД — импорт потребовал бы перезаписи.</param>
/// <param name="RegulationUrl">Найденный регламент (תקנון) — основание для галочек.</param>
/// <param name="RegulationFindings">Цитаты из регламента: в пачке галочки ставятся сами, и основание должно быть видно.</param>
/// <param name="HasMedals">Регламент говорит про медали → Awards.</param>
/// <param name="HasClubStanding">Регламент говорит про командный зачёт → клубные очки ведутся.</param>
/// <param name="IsChampionship">Чемпионат Израиля (по имени или регламенту).</param>
/// <param name="PointRuleClubsId">Правило клубных очков, подобранное по фактической шкале loglig.</param>
/// <param name="PoolType">Длина бассейна, распознанная парсером («25m»/«50m»).</param>
public sealed record BulkPullRowDto(
    int DiscoveredId,
    int OrgCompId,
    string Name,
    string Date,
    BulkPullVerdict Verdict,
    IReadOnlyList<string> Reasons,
    Guid? PreviewId,
    int ResultCount,
    int RecordCount,
    int DayCount,
    int? ExistingCompetitionId,
    string? RegulationUrl,
    IReadOnlyList<RegulationFindingDto> RegulationFindings,
    bool HasMedals,
    bool HasClubStanding,
    bool IsChampionship,
    int? PointRuleClubsId,
    string? PoolType);

/// <summary>Состояние пачки: сколько сделано и что получилось.</summary>
/// <param name="BatchId">Ключ пачки; переживает перезагрузку страницы (sessionStorage).</param>
/// <param name="Total">Сколько строк взяли в работу.</param>
/// <param name="Done">Сколько обработано.</param>
/// <param name="Finished">Работа закончена (или отменена).</param>
/// <param name="Rows">Готовые строки — в порядке обработки.</param>
/// <param name="SkippedChampionships">Имена чемпионатов, исключённых из пачки по умолчанию.</param>
/// <param name="Error">Пачка не стартовала (например, пустой список).</param>
public sealed record BulkPullBatchDto(
    Guid BatchId,
    int Total,
    int Done,
    bool Finished,
    IReadOnlyList<BulkPullRowDto> Rows,
    IReadOnlyList<string> SkippedChampionships,
    string? Error = null);

/// <summary>Итог пакетного импорта: сколько строк ушло в очередь и что не поехало.</summary>
/// <param name="Queued">Сколько заданий поставлено в очередь импорта.</param>
/// <param name="Skipped">Строки, которые импортировать не удалось, с причиной.</param>
public sealed record BulkImportResultDto(
    int Queued,
    IReadOnlyList<string> Skipped);
