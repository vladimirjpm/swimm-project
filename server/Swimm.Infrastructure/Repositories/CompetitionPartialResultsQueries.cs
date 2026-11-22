using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// «Отметка “результаты неполные” видна» (Р114) — одно условие запроса для всех, кто ищет такие
/// дни: «Требует внимания» / счётчик «ждёт тебя» (<c>ResultsAutoWaitingQuery</c>) и чип списка
/// <c>/api/competitions</c> (<c>ResultRepository.LoadSourcesAsync</c>).
///
/// ⚠ Под него заведён частичный индекс <c>IX_Competitions_PartialResults_Shown</c>
/// (<c>CompetitionConfiguration</c>, фильтр <c>"PartialResults" IS NOT NULL AND
/// "PartialResultsAcceptedAt" IS NULL</c>). Планировщик берёт частичный индекс, только если
/// условие запроса его фильтр подразумевает, поэтому оба сравнения идут в SQL и ровно в такой
/// форме. Меняешь условие — меняй фильтр индекса (новой миграцией), и наоборот;
/// <c>PartialResultsQueryTests</c> сверяет SQL. Тот же предикат в памяти —
/// <c>PartialResultsState.IsShownFor</c>.
/// </summary>
internal static class CompetitionPartialResultsQueries
{
    public static IQueryable<Competition> WherePartialShown(this IQueryable<Competition> query) =>
        query.Where(c => c.PartialResults != null && c.PartialResultsAcceptedAt == null);
}
