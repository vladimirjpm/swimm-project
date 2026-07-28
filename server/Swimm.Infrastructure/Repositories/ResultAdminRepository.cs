using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Ручная правка одного результата (см. <see cref="IResultAdminRepository"/>). Пишет через
/// owner-контекст; данные результата денормализованы в публичных выдачах → после правки
/// сбрасывает кэш целиком. Эстафетные строки (RelayId != null) не редактируются: их состав
/// живёт в RelayMembers, а переназначение пловца тут разорвало бы связь.
/// </summary>
/// <param name="recalc">
/// Пересчёт объединённых мест соревнования: правка времени/дисквалификации меняет порядок
/// в общем зачёте. Необязателен — null пропускает пересчёт (тесты).
/// </param>
public class ResultAdminRepository(
    SwimmDbContext db,
    ICacheService cache,
    ICompetitionRecalculationService? recalc = null) : IResultAdminRepository
{
    public async Task<ResultEditDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var r = await db.Results.AsNoTracking()
            .Include(x => x.Competition)
            .Include(x => x.Swimmer)
            .Include(x => x.Club)
            .Include(x => x.Style)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (r == null || r.RelayId != null) return null;

        return new ResultEditDto
        {
            Id = r.Id,
            CompetitionId = r.CompetitionId,
            CompetitionName = r.Competition.Name,
            CompetitionDate = r.CompetitionDate,
            StyleName = r.Style.Name,
            SwimmerId = r.SwimmerId,
            SwimmerName = $"{r.Swimmer.LastName} {r.Swimmer.FirstName}".Trim(),
            ClubId = r.ClubId,
            ClubName = r.Club.Name,
            Distance = r.Distance,
            Gender = r.Gender,
            AgeGroup = r.AgeGroup,
            EventStyleAge = r.EventStyleAge,
            Position = r.Position,
            PositionAgeGroup = r.PositionAgeGroup,
            Heat = r.Heat,
            Lane = r.Lane,
            TimeText = SwimTime.FormatMs(r.TimeMillisecond),
            TimeFail = r.TimeFail,
            TimeFailNote = r.TimeFailNote,
            InternationalPoints = r.InternationalPoints,
            Note = r.Note
        };
    }

    public async Task<ResultSaveResult> UpdateAsync(long id, ResultEditInputDto input, CancellationToken ct = default)
    {
        var r = await db.Results.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r == null) return ResultSaveResult.Fail($"Результат #{id} не найден");
        if (r.RelayId != null)
            return ResultSaveResult.Fail("Эстафетные результаты этой формой не правятся (состав — в RelayMembers).");

        // Время: пусто → снять; заполнено, но не парсится → ошибка (не тихо обнулять).
        int? timeMs;
        if (string.IsNullOrWhiteSpace(input.TimeText))
        {
            timeMs = null;
        }
        else
        {
            timeMs = SwimTime.ParseToMs(input.TimeText);
            if (timeMs is null)
                return ResultSaveResult.Fail($"Неверный формат времени «{input.TimeText}» — ожидается м:сс.дд или сс.дд.");
        }

        // Валидация числовых полей под check-констрейнты БД (даём понятную ошибку заранее).
        if (input.Heat < 0) return ResultSaveResult.Fail("Heat не может быть отрицательным");
        if (input.Lane < 0) return ResultSaveResult.Fail("Lane не может быть отрицательным");
        if (input.InternationalPoints < 0) return ResultSaveResult.Fail("Очки не могут быть отрицательными");
        if (input.Position is <= 0) return ResultSaveResult.Fail("Место должно быть положительным (или пустым)");
        if (input.PositionAgeGroup is <= 0) return ResultSaveResult.Fail("Место в возрастной группе должно быть положительным (или пустым)");

        // Переназначаемые ссылки: цель должна существовать (FK всё равно Restrict).
        if (!await db.Swimmers.AnyAsync(s => s.Id == input.SwimmerId, ct))
            return ResultSaveResult.Fail($"Пловец #{input.SwimmerId} не найден");
        if (!await db.Clubs.AnyAsync(c => c.Id == input.ClubId, ct))
            return ResultSaveResult.Fail($"Клуб #{input.ClubId} не найден");

        r.SwimmerId = input.SwimmerId;
        r.ClubId = input.ClubId;
        r.Distance = (input.Distance ?? "").Trim();
        r.Gender = (input.Gender ?? "").Trim();
        r.AgeGroup = (input.AgeGroup ?? "").Trim();
        r.EventStyleAge = (input.EventStyleAge ?? "").Trim();
        r.Position = input.Position;
        r.PositionAgeGroup = input.PositionAgeGroup;
        r.Heat = input.Heat;
        r.Lane = input.Lane;
        r.TimeMillisecond = timeMs;
        r.TimeOriginal = string.IsNullOrWhiteSpace(input.TimeText) ? string.Empty : input.TimeText.Trim();
        r.TimeFail = input.TimeFail;
        r.TimeFailNote = string.IsNullOrWhiteSpace(input.TimeFailNote) ? null : input.TimeFailNote.Trim();
        r.InternationalPoints = input.InternationalPoints;
        r.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return ResultSaveResult.Fail($"Не удалось сохранить: {ex.InnerException?.Message ?? ex.Message}");
        }

        // Объединённое место — производная от времени: исправили опечатку в протоколе, а
        // порядок в общем зачёте дисциплины остался бы от старого значения. Правка уже
        // сохранена, поэтому сбой пересчёта её не отменяет (аварийно — `--recalc-combined`).
        if (recalc is not null)
        {
            try { await recalc.RecalculateCompetitionAsync(r.CompetitionId, ct); }
            catch (Exception) { /* починка — прогоном CLI */ }
        }

        await cache.InvalidateAllAsync();
        return ResultSaveResult.Ok();
    }
}
