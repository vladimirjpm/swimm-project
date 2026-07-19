using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Краудсорс-привязка Loglig ID (docs/loglig-id-plan.md, шаг 6). Предложение пишется как
/// Suggested без похода на loglig; ночная верификация сверяет имя + год рождения карточки
/// (клуб — бонус-сигнал в логе), спорные добиваются полной сверкой результатов
/// (ILogligMatchService). Rejected хранит отклонённый ID, чтобы тот же пользователь/ID не
/// спамился повторно; при легитимной привязке этого же ID к ДРУГОМУ пловцу отклонённый
/// держатель освобождается (см. FreeRejectedHolderAsync — уникальный индекс один на колонку).
/// </summary>
public class LogligSuggestionService(
    SwimmDbContext db,
    ILogligClient logligClient,
    ILogligMatchService matchService,
    ILogger<LogligSuggestionService> logger) : ILogligSuggestionService
{
    public async Task<LogligSuggestResult> SuggestAsync(int swimmerId, int logligId, int userId, CancellationToken ct = default)
    {
        if (logligId <= 0)
            return new LogligSuggestResult(false, "Некорректный loglig ID");

        var swimmer = await db.Swimmers.FindAsync([swimmerId], ct);
        if (swimmer is null)
            return new LogligSuggestResult(false, "Пловец не найден");

        switch (swimmer.LogligIdStatus)
        {
            case "Verified":
                return new LogligSuggestResult(false, "Пловец уже привязан");
            case "Suggested":
                return new LogligSuggestResult(false, "Предложение уже ожидает проверки");
            case "Rejected" when swimmer.LogligId == logligId:
                return new LogligSuggestResult(false, "Этот loglig ID уже был отклонён для этого пловца");
        }

        var holderError = await FreeRejectedHolderAsync(logligId, swimmerId, ct);
        if (holderError is not null)
            return new LogligSuggestResult(false, holderError);

        swimmer.LogligId = logligId;
        swimmer.LogligIdStatus = "Suggested";
        swimmer.LogligIdSource = "user-claim";
        swimmer.LogligIdSuggestedByUserId = userId;
        swimmer.LogligIdSuggestedAt = DateTime.UtcNow;
        swimmer.LogligIdVerifiedAt = null;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Гонка на уникальном индексе LogligId.
            db.Entry(swimmer).State = EntityState.Unchanged;
            return new LogligSuggestResult(false, $"loglig ID {logligId} уже привязан к другому пловцу");
        }

        logger.LogInformation(
            "Loglig suggest: swimmer #{SwimmerId} ← loglig #{LogligId}, user #{UserId}",
            swimmerId, logligId, userId);
        return new LogligSuggestResult(true, null);
    }

    public async Task<LogligSuggestionVerifyReport> VerifySuggestedAsync(CancellationToken ct = default)
    {
        var pending = await db.Swimmers
            .Where(s => s.LogligIdStatus == "Suggested" && s.LogligId != null)
            .ToListAsync(ct);

        int verified = 0, rejected = 0, skipped = 0;
        foreach (var swimmer in pending)
        {
            ct.ThrowIfCancellationRequested();

            var card = await logligClient.GetPlayerCardAsync(swimmer.LogligId!.Value, ct);
            if (card is null)
            {
                // Карточка недоступна (сеть/вёрстка/чужой сезон) — не решаем, проверим в следующий раз.
                skipped++;
                continue;
            }

            var clubName = swimmer.ClubId is null
                ? null
                : await db.Clubs.AsNoTracking()
                    .Where(c => c.Id == swimmer.ClubId).Select(c => c.Name).FirstOrDefaultAsync(ct);

            var ok = IsNameAndYearMatch(card, swimmer);
            if (!ok)
            {
                // Спорный случай (например, разночтения имени) — полная сверка по результатам.
                var localResults = await db.Results.AsNoTracking()
                    .Where(r => r.SwimmerId == swimmer.Id && r.RelayId == null)
                    .Select(r => new LocalResultKey(r.CompetitionDate, r.Distance, r.Style.Name, r.TimeMillisecond))
                    .ToListAsync(ct);
                ok = matchService.Match(card, swimmer.BirthYear, clubName, localResults).Decision
                    == LogligMatchDecision.AutoVerify;
            }

            if (ok)
            {
                swimmer.LogligIdStatus = "Verified";
                swimmer.LogligIdVerifiedAt = DateTime.UtcNow;
                verified++;
                var clubMatch = clubName is not null && string.Equals(card.ClubName?.Trim(), clubName.Trim(), StringComparison.Ordinal);
                logger.LogWarning(
                    "Loglig verify: swimmer #{SwimmerId} ← loglig #{LogligId} подтверждён (клуб совпал: {ClubMatch})",
                    swimmer.Id, swimmer.LogligId, clubMatch);
            }
            else
            {
                // Rejected сохраняет отклонённый ID (анти-спам тем же ID; виден админу).
                swimmer.LogligIdStatus = "Rejected";
                swimmer.LogligIdVerifiedAt = null;
                rejected++;
                logger.LogWarning(
                    "Loglig verify: swimmer #{SwimmerId} ← loglig #{LogligId} отклонён (карточка: {CardName}, {CardYear})",
                    swimmer.Id, swimmer.LogligId, card.FullName, card.BirthYear);
            }

            await db.SaveChangesAsync(ct);
        }

        return new LogligSuggestionVerifyReport(pending.Count, verified, rejected, skipped);
    }

    /// <summary>Имя (оба порядка, нормализованные пробелы) + год рождения совпадают с карточкой.</summary>
    internal static bool IsNameAndYearMatch(Application.Dtos.LogligPlayerCard card, Swimmer swimmer)
    {
        if (card.BirthYear is null || card.BirthYear.Value != swimmer.BirthYear)
            return false;

        var cardName = Normalize(card.FullName);
        return cardName == Normalize($"{swimmer.LastName} {swimmer.FirstName}")
            || cardName == Normalize($"{swimmer.FirstName} {swimmer.LastName}");
    }

    private static string Normalize(string s) =>
        string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// Уникальный индекс LogligId один на колонку, независимо от статуса. Держатель со статусом
    /// Rejected — это ошибочное отклонённое предложение, оно не должно блокировать легитимную
    /// привязку того же ID к другому пловцу: освобождаем слот. Держатель с любым другим статусом —
    /// честная занятость, возвращаем текст ошибки.
    /// </summary>
    private async Task<string?> FreeRejectedHolderAsync(int logligId, int exceptSwimmerId, CancellationToken ct)
    {
        var holder = await db.Swimmers
            .Where(s => s.LogligId == logligId && s.Id != exceptSwimmerId)
            .FirstOrDefaultAsync(ct);
        if (holder is null)
            return null;

        if (holder.LogligIdStatus != "Rejected")
            return $"loglig ID {logligId} уже привязан к другому пловцу";

        holder.LogligId = null;
        holder.LogligIdStatus = null;
        holder.LogligIdSource = null;
        holder.LogligIdSuggestedByUserId = null;
        holder.LogligIdSuggestedAt = null;
        holder.LogligIdVerifiedAt = null;
        logger.LogInformation(
            "Loglig suggest: отклонённый держатель #{HolderId} освобождён от loglig #{LogligId}",
            holder.Id, logligId);
        return null;
    }
}
