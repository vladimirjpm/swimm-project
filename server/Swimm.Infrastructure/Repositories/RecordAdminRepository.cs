using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Админский CRUD рекордов и нормативов (см. <see cref="IRecordAdminRepository"/>). Пишет через
/// owner-контекст <see cref="SwimmDbContext"/>; после мутаций сбрасывает публичный кэш
/// (<see cref="RecordRepository"/> кэширует выборки по региону/kind отдельно, TTL 24ч).
/// </summary>
public class RecordAdminRepository : IRecordAdminRepository
{
    private readonly SwimmDbContext _db;
    private readonly ICacheService _cache;

    public RecordAdminRepository(SwimmDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    // ── Records ──────────────────────────────────────────────────────────────

    public async Task<PagedResult<RecordDto>> GetRecordsAsync(RecordFilter filter, int page, int pageSize)
    {
        var query = _db.Records.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.RegionType)) query = query.Where(r => r.RegionType == filter.RegionType);
        if (!string.IsNullOrWhiteSpace(filter.RegionCode)) query = query.Where(r => r.RegionCode == filter.RegionCode);
        if (!string.IsNullOrWhiteSpace(filter.Category)) query = query.Where(r => r.Category == filter.Category);
        if (!string.IsNullOrWhiteSpace(filter.Gender)) query = query.Where(r => r.Gender == filter.Gender);
        if (!string.IsNullOrWhiteSpace(filter.PoolType)) query = query.Where(r => r.PoolType == filter.PoolType);
        if (!string.IsNullOrWhiteSpace(filter.Style)) query = query.Where(r => r.Style == filter.Style);

        var total = await query.CountAsync();

        var items = await query
            .OrderBy(r => r.RegionType).ThenBy(r => r.RegionCode).ThenBy(r => r.Category)
            .ThenBy(r => r.Gender).ThenBy(r => r.PoolType).ThenBy(r => r.Style).ThenBy(r => r.Distance).ThenBy(r => r.AgeKey)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => ToDto(r))
            .ToListAsync();

        return new PagedResult<RecordDto>(items, total, page, pageSize);
    }

    public async Task<RecordSaveResult> CreateRecordAsync(RecordInputDto input)
    {
        var error = ValidateRecordAxes(input.RegionType, input.Category, input.Gender, input.PoolType, input.Time);
        if (error != null) return RecordSaveResult.Fail(error);

        var record = new Record();
        ApplyRecordInput(record, input);

        _db.Records.Add(record);
        return await SaveRecordAsync(record);
    }

    public async Task<RecordSaveResult> UpdateRecordAsync(int id, RecordQuickEditDto input)
    {
        var record = await _db.Records.FindAsync(id);
        if (record == null) return RecordSaveResult.Fail($"Рекорд #{id} не найден");

        if (string.IsNullOrWhiteSpace(input.Time)) return RecordSaveResult.Fail("Time обязателен");

        record.Time = input.Time.Trim();
        record.HolderName = Norm(input.HolderName);
        record.Club = Norm(input.Club);
        record.HolderCountry = Norm(input.HolderCountry);
        record.RecordDate = Norm(input.RecordDate);

        return await SaveRecordAsync(record);
    }

    public async Task<RecordSaveResult> DeleteRecordAsync(int id)
    {
        var record = await _db.Records.FindAsync(id);
        if (record == null) return RecordSaveResult.Fail($"Рекорд #{id} не найден");

        _db.Records.Remove(record);
        await _db.SaveChangesAsync();
        await _cache.InvalidateAllAsync();
        return RecordSaveResult.Ok(id);
    }

    // ── NormativeStandards ───────────────────────────────────────────────────

    public async Task<PagedResult<NormativeStandardDto>> GetStandardsAsync(StandardFilter filter, int page, int pageSize)
    {
        var query = _db.NormativeStandards.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Kind)) query = query.Where(s => s.Kind == filter.Kind);
        if (!string.IsNullOrWhiteSpace(filter.Gender)) query = query.Where(s => s.Gender == filter.Gender);
        if (!string.IsNullOrWhiteSpace(filter.PoolType)) query = query.Where(s => s.PoolType == filter.PoolType);
        if (!string.IsNullOrWhiteSpace(filter.Style)) query = query.Where(s => s.Style == filter.Style);

        var total = await query.CountAsync();

        var items = await query
            .OrderBy(s => s.Kind).ThenBy(s => s.Gender).ThenBy(s => s.PoolType)
            .ThenBy(s => s.Style).ThenBy(s => s.Distance).ThenBy(s => s.AgeKey).ThenBy(s => s.Level)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => ToDto(s))
            .ToListAsync();

        return new PagedResult<NormativeStandardDto>(items, total, page, pageSize);
    }

    public async Task<RecordSaveResult> CreateStandardAsync(NormativeStandardInputDto input)
    {
        var error = ValidateStandardAxes(input.Kind, input.Gender, input.PoolType, input.Time);
        if (error != null) return RecordSaveResult.Fail(error);

        var standard = new NormativeStandard();
        ApplyStandardInput(standard, input);

        _db.NormativeStandards.Add(standard);
        return await SaveStandardAsync(standard);
    }

    public async Task<RecordSaveResult> UpdateStandardAsync(int id, StandardQuickEditDto input)
    {
        var standard = await _db.NormativeStandards.FindAsync(id);
        if (standard == null) return RecordSaveResult.Fail($"Норматив #{id} не найден");

        if (string.IsNullOrWhiteSpace(input.Time)) return RecordSaveResult.Fail("Time обязателен");

        standard.Time = input.Time.Trim();
        return await SaveStandardAsync(standard);
    }

    public async Task<RecordSaveResult> DeleteStandardAsync(int id)
    {
        var standard = await _db.NormativeStandards.FindAsync(id);
        if (standard == null) return RecordSaveResult.Fail($"Норматив #{id} не найден");

        _db.NormativeStandards.Remove(standard);
        await _db.SaveChangesAsync();
        await _cache.InvalidateAllAsync();
        return RecordSaveResult.Ok(id);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string? Norm(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ValidateRecordAxes(string regionType, string category, string gender, string poolType, string time)
    {
        if (!Record.RegionTypes.Contains(regionType))
            return $"region_type должен быть одним из: {string.Join(", ", Record.RegionTypes)}";
        if (!Record.Categories.Contains(category))
            return $"category должен быть одним из: {string.Join(", ", Record.Categories)}";
        if (gender != "male" && gender != "female")
            return "gender должен быть male или female";
        if (poolType != "25m" && poolType != "50m")
            return "pool_type должен быть 25m или 50m";
        if (string.IsNullOrWhiteSpace(time))
            return "time обязателен";
        return null;
    }

    private static string? ValidateStandardAxes(string kind, string gender, string poolType, string time)
    {
        if (!NormativeStandard.Kinds.Contains(kind))
            return $"kind должен быть одним из: {string.Join(", ", NormativeStandard.Kinds)}";
        if (gender != "male" && gender != "female")
            return "gender должен быть male или female";
        if (poolType != "25m" && poolType != "50m")
            return "pool_type должен быть 25m или 50m";
        if (string.IsNullOrWhiteSpace(time))
            return "time обязателен";
        return null;
    }

    private static void ApplyRecordInput(Record record, RecordInputDto input)
    {
        record.RegionType = input.RegionType.Trim();
        record.RegionCode = (input.RegionCode ?? "").Trim();
        record.Category = input.Category.Trim();
        record.AgeKey = (input.AgeKey ?? "").Trim();
        record.Gender = input.Gender.Trim();
        record.PoolType = input.PoolType.Trim();
        record.Style = input.Style.Trim();
        record.Distance = input.Distance.Trim();
        record.Time = input.Time.Trim();
        record.HolderName = Norm(input.HolderName);
        record.Club = Norm(input.Club);
        record.HolderCountry = Norm(input.HolderCountry);
        record.RecordDate = Norm(input.RecordDate);
    }

    private static void ApplyStandardInput(NormativeStandard standard, NormativeStandardInputDto input)
    {
        standard.Kind = input.Kind.Trim();
        standard.Country = (input.Country ?? "").Trim();
        standard.Gender = input.Gender.Trim();
        standard.PoolType = input.PoolType.Trim();
        standard.Style = input.Style.Trim();
        standard.Distance = input.Distance.Trim();
        standard.AgeKey = (input.AgeKey ?? "").Trim();
        standard.Level = input.Level.Trim();
        standard.Time = input.Time.Trim();
    }

    private async Task<RecordSaveResult> SaveRecordAsync(Record record)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return RecordSaveResult.Fail("Такой рекорд уже есть (совпадает территория/категория/дисциплина).");
        }
        await _cache.InvalidateAllAsync();
        return RecordSaveResult.Ok(record.Id);
    }

    private async Task<RecordSaveResult> SaveStandardAsync(NormativeStandard standard)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return RecordSaveResult.Fail("Такой норматив уже есть (совпадает система/дисциплина/уровень).");
        }
        await _cache.InvalidateAllAsync();
        return RecordSaveResult.Ok(standard.Id);
    }

    private static RecordDto ToDto(Record r) => new()
    {
        Id = r.Id,
        RegionType = r.RegionType,
        RegionCode = r.RegionCode,
        Category = r.Category,
        AgeKey = r.AgeKey,
        Gender = r.Gender,
        PoolType = r.PoolType,
        Style = r.Style,
        Distance = r.Distance,
        Time = r.Time,
        HolderName = r.HolderName,
        Club = r.Club,
        HolderCountry = r.HolderCountry,
        RecordDate = r.RecordDate
    };

    private static NormativeStandardDto ToDto(NormativeStandard s) => new()
    {
        Id = s.Id,
        Kind = s.Kind,
        Country = s.Country,
        Gender = s.Gender,
        PoolType = s.PoolType,
        Style = s.Style,
        Distance = s.Distance,
        AgeKey = s.AgeKey,
        Level = s.Level,
        Time = s.Time
    };
}
