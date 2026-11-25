using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>Пловец глазами шага «пол с карточки loglig»: то, что нужно правилу, одной проекцией.</summary>
internal sealed record GenderCandidate(int Id, string FirstName, string LastName, int BirthYear, string? Gender,
    int? LogligId, string? LogligIdStatus)
{
    public string Name => $"{LastName} {FirstName}".Trim();

    public CardGenderSwimmer Subject => new(Gender, LogligId, LogligIdStatus,
        LogligClient.NameTokens($"{FirstName} {LastName}"), BirthYear);

    public static IQueryable<GenderCandidate> From(IQueryable<Swimm.Domain.Entities.Swimmer> scope) => scope.AsNoTracking()
        .Select(s => new GenderCandidate(s.Id, s.FirstName, s.LastName, s.BirthYear, s.Gender, s.LogligId, s.LogligIdStatus));

    public static CardGenderAnswer Answer(StoredLogligClub a) =>
        new(LogligClient.NameTokens(a.CardName ?? ""), a.CardBirthYear, a.Gender);
}

/// <summary>
/// Р126: кто ждёт ручной проверки пола с loglig — без сети, по сохранённым ответам карточек и
/// текущей базе. Отдельный класс, а не метод <see cref="LogligCardGenderService"/>: его читает
/// проверка Health, а сервис зависит от реестра проверок — иначе круг зависимостей.
/// </summary>
public sealed class LogligGenderReviewFinder(SwimmDbContext db, IGenderImpactService impact)
{
    /// <summary>Р122: у пловца есть строка, чей игрок loglig со страницы дисциплины — этот же id.</summary>
    public Task<bool> RowPlayerMatchedAsync(int swimmerId, int logligId, CancellationToken ct) =>
        db.Results.AsNoTracking().AnyAsync(r => r.SwimmerId == swimmerId && r.LogligPlayerId == logligId, ct);

    public async Task<IReadOnlyList<LogligGenderReview>> GetPendingAsync(CancellationToken ct = default)
    {
        var swimmers = await GenderCandidate.From(db.Swimmers.Where(s =>
                (s.Gender == null || s.Gender == "")
                && s.LogligIdStatus == "Verified" && s.LogligId != null
                && db.SwimmerLogligClubs.Any(c => c.SwimmerId == s.Id && c.LogligId == s.LogligId
                                                  && c.Gender != null && c.CardName != null))
                // Сортировка ДО проекции: по свойству записи-проекции SQL не строится (LogligCardGenderPgTests).
                .OrderBy(s => s.Id))
            .ToListAsync(ct);
        var stored = await SwimmerLogligClubStore.LoadAsync(db, swimmers.Select(s => s.Id).ToArray(), ct);

        var reviews = new List<LogligGenderReview>();
        foreach (var s in swimmers)
        {
            var answer = stored[s.Id];
            var (verdict, gender) = LogligCardGenderRule.Decide(s.Subject, GenderCandidate.Answer(answer));
            if (verdict != CardGenderVerdict.Write) continue;

            var rowPlayer = await RowPlayerMatchedAsync(s.Id, answer.LogligId, ct);
            // Про заплывы на карточке ещё не знаем (ответ записала сверка клуба) — это не «ждёт
            // человека», а «ждёт следующего прогона»: он откроет карточку и сверит.
            if (!rowPlayer && answer.ResultsMatched is null) continue;

            var reasons = LogligCardGenderRule.ReviewReasons(answer.ResultsMatched == true, rowPlayer,
                await impact.AssessAsync(s.Id, gender!, ct));
            if (reasons.Count > 0)
                reviews.Add(new LogligGenderReview(s.Id, s.Name, s.BirthYear, answer.LogligId, gender!, answer.CardName, reasons));
        }
        return reviews;
    }
}
