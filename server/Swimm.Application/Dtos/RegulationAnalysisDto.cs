namespace Swimm.Application.Dtos;

/// <summary>
/// Одна находка в регламенте: почему мы предлагаем поставить (или снять) галочку.
/// </summary>
/// <param name="Flag">Что нашли: <c>medals</c> | <c>clubStanding</c> | <c>championship</c>.</param>
/// <param name="Matched">Слово регламента, по которому опознали (иврит).</param>
/// <param name="Quote">
/// Строка регламента целиком — чтобы админ прочитал сам и не верил нам на слово. Иврит в
/// PDF извлекается задом наперёд, поэтому строка уже развёрнута в читаемый вид.
/// </param>
public sealed record RegulationFindingDto(string Flag, string Matched, string Quote);

/// <summary>
/// Итог разбора регламента. Флаги — ПРЕДЛОЖЕНИЕ для галочек в панели затягивания, а не
/// решение: последнее слово за админом, поэтому рядом всегда идут цитаты.
/// </summary>
/// <param name="HasMedals">Регламент упоминает медали (מדליות) → чекбокс «Awards».</param>
/// <param name="HasClubStanding">
/// Регламент описывает клубный зачёт (דירוג מועדונים / ניקוד אגודות) → снять галочку
/// «Клубный зачёт не ведётся».
/// </param>
/// <param name="IsChampionship">Регламент чемпионата Израиля (אליפות ישראל) → 🏆.</param>
/// <param name="Findings">Находки с цитатами — по одной на каждое сработавшее слово.</param>
/// <param name="Error">Файл не удалось прочитать; остальные поля тогда пустые.</param>
public sealed record RegulationAnalysisDto(
    bool HasMedals,
    bool HasClubStanding,
    bool IsChampionship,
    IReadOnlyList<RegulationFindingDto> Findings,
    string? Error = null);

/// <summary>
/// Итог САМОСТОЯТЕЛЬНОГО забора регламента с loglig (в отличие от разбора файла, который
/// приложил админ).
/// </summary>
/// <param name="Found">Регламент найден и прочитан; иначе <paramref name="Analysis"/> пуст.</param>
/// <param name="Url">Адрес PDF на loglig — основание, которое видит админ и хранит аудит.</param>
/// <param name="Analysis">Находки анализатора; null — регламента нет или он не прочитался.</param>
/// <param name="Error">
/// Почему не получилось: «ссылки на регламент нет» — обычное дело (её ставят не всем), это
/// НЕ сбой, который стоит повторять.
/// </param>
public sealed record RegulationFetchDto(
    bool Found,
    string? Url,
    RegulationAnalysisDto? Analysis,
    string? Error = null);
