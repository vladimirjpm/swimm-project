using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Импорт результатов из JSON (формат CommonModels / ResultWrap).
/// Заполняет справочники (Competition, Club, Swimmer, Style, Relay, Gallery) и таблицу Results.
/// </summary>
public class JsonImportService : IImportService
{
    private readonly SwimmDbContext _db;
    private readonly ICacheService _cache;

    private static readonly string[] ClearableTables =
        ["Results", "GalleryItems", "Galleries", "Relays", "Swimmers", "Clubs", "Sys_ImportHistory", "Competitions", "CompetitionEvents", "Countries"];

    public JsonImportService(SwimmDbContext db, ICacheService cache)
    {
        _db    = db;
        _cache = cache;
    }

    public string[] GetClearableTables() => ClearableTables;

    /// <summary>
    /// Импортирует JSON-файл и возвращает статистику.
    /// Поддерживает два формата: ResultWrap { results: [...] } и простой массив Result[].
    /// </summary>
    /// <summary>
    /// Опции чтения results-JSON парсера: строковые "true"/"44" вместо bool/int и т.п.
    /// Используются также SwimmerNameSyncService — форматы должны читаться одинаково.
    /// </summary>
    internal static JsonSerializerOptions CreateLenientOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        options.Converters.Add(new LenientBoolConverter());
        options.Converters.Add(new LenientNullableBoolConverter());
        options.Converters.Add(new LenientNullableIntConverter());
        options.Converters.Add(new LenientStringConverter());
        return options;
    }

    public async Task<ImportResult> ImportAsync(Stream jsonStream, string? fileName = null, IReadOnlyCollection<string>? categoryKeys = null, ImportEventOptions? eventOptions = null, int? orgCompId = null)
    {
        var options = CreateLenientOptions();

        // Читаем весь поток в память для возможности двойной десериализации
        using var ms = new MemoryStream();
        await jsonStream.CopyToAsync(ms);
        ms.Position = 0;

        List<ResultJsonItem> items;
        bool isMasters = false, isAward = false, showCombine = false;
        List<string> wrapCategoryKeys = [];
        var diagnosticLog = new List<string>();

        // Пробуем ResultWrap { results: [...] }, затем простой массив
        try
        {
            var wrap = await JsonSerializer.DeserializeAsync<ResultWrap>(ms, options);
            if (wrap?.Results is { Count: > 0 })
            {
                items = wrap.Results;
                isMasters = wrap.IsMasters ?? false;
                isAward = wrap.IsAward ?? false;
                showCombine = wrap.ShowCombineAllResults ?? false;
                wrapCategoryKeys = wrap.Categories ?? [];
                diagnosticLog.Add("Format: ResultWrap");
            }
            else
            {
                ms.Position = 0;
                items = await JsonSerializer.DeserializeAsync<List<ResultJsonItem>>(ms, options) ?? [];
                diagnosticLog.Add("Format: Array");
            }
        }
        catch (JsonException ex1)
        {
            diagnosticLog.Add($"ResultWrap attempt failed: {ex1.Message}");
            ms.Position = 0;
            try
            {
                items = await JsonSerializer.DeserializeAsync<List<ResultJsonItem>>(ms, options) ?? [];
                diagnosticLog.Add("Format: Array (fallback)");
            }
            catch (JsonException ex2)
            {
                diagnosticLog.Add($"Array attempt failed: {ex2.Message}");
                ms.Position = 0;
                diagnosticLog.AddRange(await DiagnoseJsonFieldsAsync(ms));
                return new ImportResult
                {
                    Message = "JSON deserialization failed",
                    ErrorMessages = [$"{ex2.Message}"],
                    DiagnosticLog = diagnosticLog
                };
            }
        }

        // Категории из UI имеют приоритет над JSON-обёрткой; применяются ко всем соревнованиям файла.
        var effectiveCategoryKeys = categoryKeys is { Count: > 0 } ? categoryKeys.ToList() : wrapCategoryKeys;

        // Флаг Masters производен от категории: выбрана категория Masters ⇒ соревнование мастерское.
        isMasters = isMasters || effectiveCategoryKeys.Contains(Category.MastersKey);

        diagnosticLog.Add($"Items: {items.Count}, IsMasters(wrap): {isMasters}, IsAward(wrap): {isAward}, Categories: [{string.Join(", ", effectiveCategoryKeys)}]");

        if (items.Count == 0)
            return new ImportResult { Message = "JSON contains no results", DiagnosticLog = diagnosticLog };

        // === Pre-load all reference caches to avoid per-row DB queries ===
        // Styles: tracked (seed data, rarely new)
        var styleCache = await _db.Styles.ToDictionaryAsync(s => s.Name);

        // Countries: AsNoTracking — we only read their IDs, don't modify them.
        // Второй индекс по имени — распознавание псевдоклубов (страна в графе клуба).
        var countryCache = new Dictionary<string, Country>(StringComparer.OrdinalIgnoreCase);
        var countryByName = new Dictionary<string, Country>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in await _db.Countries.AsNoTracking().ToListAsync())
        {
            countryCache.TryAdd(c.CountryCode, c);
            countryByName.TryAdd(c.CountryName, c);
        }

        // Competitions: AsNoTracking — we read IDs and check for duplicates. Индекс строится тем же
        // правилом (Name-или-SubName+Date+PoolType), что и превью (ImportCompetitionMatcher) —
        // раньше здесь был строгий Name-only ключ, из-за чего импорт промахивался мимо дней,
        // переименованных при привязке к CompetitionEvent (Name=имя события, SubName=заголовок
        // файла), и создавал дубль там, где превью репортило совпадение (инцидент Maccabiah).
        var competitionCache = ImportCompetitionMatcher.BuildIndex(
            await _db.Competitions.AsNoTracking().ToListAsync(),
            c => c.Name, c => c.SubName, c => c.Date, c => c.PoolType);

        // Clubs: AsNoTracking — we only read their IDs
        var clubCache = new Dictionary<string, Club>();
        foreach (var c in await _db.Clubs.AsNoTracking().ToListAsync())
            clubCache.TryAdd($"{c.Name}|{c.NameEn}", c);

        // Swimmers: tracked — EnrichSwimmerFromResult modifies them, changes saved in batch.
        // Второй индекс по EN-имени — фоллбек-матчинг для двуязычных/EN-протоколов
        // (Maccabiah-кейс: пловец известен под английским именем).
        var swimmerCache = new Dictionary<string, Swimmer>();
        var swimmerCacheEn = new Dictionary<string, Swimmer>();
        foreach (var s in await _db.Swimmers.ToListAsync())
        {
            swimmerCache.TryAdd(SwimmerMatchKey(s.LastName, s.FirstName, s.BirthYear), s);
            if (!string.IsNullOrWhiteSpace(s.LastNameEn))
                swimmerCacheEn.TryAdd(SwimmerMatchKey(s.LastNameEn, s.FirstNameEn, s.BirthYear), s);
        }

        // Categories: Key → Category (для привязки соревнований к категориям на импорте).
        var categoryCache = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in await _db.Categories.AsNoTracking().ToListAsync())
            categoryCache.TryAdd(c.Key, c);

        // Следующий DisplayOrder в каждой категории (новые членства дописываем в конец).
        var categoryNextOrder = await _db.CategoryCompetitions
            .GroupBy(cc => cc.CategoryId)
            .Select(g => new { CategoryId = g.Key, Max = g.Max(x => x.DisplayOrder) })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Max + 1);

        // Competition IDs we've verified as having no existing results in this session
        var verifiedNewCompetitions = new HashSet<int>();
        // Ordered list of competition keys touched in this import (for ImportHistory)
        var touchedCompetitionKeys = new List<string>();
        // Соревнования, у которых уже были результаты в БД и импорт продолжен в upsert-режиме
        // (ImportEventOptions.OverwriteExisting=true) — по ним после основного цикла запускаем
        // матчинг/UPDATE/DELETE-с-защитой (import-upsert-plan.md, шаг 2).
        var upsertCompetitionIds = new HashSet<int>();
        var overwriteExisting = eventOptions?.OverwriteExisting ?? false;
        // Удаление исчезнувших результатов — опт-ин поверх overwriteExisting (см. ImportModels.cs,
        // DeleteMissing). Без него upsert только обновляет/вставляет и НИКОГДА не удаляет.
        var deleteMissing = overwriteExisting && (eventOptions?.DeleteMissing ?? false);

        int created = 0, skipped = 0, errors = 0;
        var errorMessages = new List<string>();
        // Batch of ResultRecords to insert at once at the end
        var resultBatch = new List<ResultRecord>(items.Count);

        // Весь импорт выполняется в одной транзакции — при любой ошибке откатываем целиком.
        // BeginTransactionAsync нельзя вызывать напрямую при включённом EnableRetryOnFailure —
        // ручной транзакцией должна управлять execution strategy, иначе EF бросает исключение
        // на каждой операции внутри транзакции.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
        // === Привязка к многодневному событию (создать новое / дописать к существующему) ===
        // targetEvent != null → все дни-соревнования этого импорта цепляются к событию:
        // Competition.Name = event.Name (общее имя), SubName = заголовок файла, DayNumber растёт.
        CompetitionEvent? targetEvent = null;
        var nextDayNumber = 1;
        if (eventOptions?.EventId is int existingEventId)
        {
            targetEvent = await _db.CompetitionEvents.FindAsync(existingEventId);
            if (targetEvent == null)
                throw new InvalidOperationException($"Событие {existingEventId} не найдено");

            var maxDay = await _db.Competitions
                .Where(c => c.EventId == targetEvent.Id)
                .MaxAsync(c => (int?)c.DayNumber) ?? 0;
            nextDayNumber = maxDay + 1;
            diagnosticLog.Add($"Event: attach to #{targetEvent.Id} \"{targetEvent.Name}\", starting day {nextDayNumber}");
        }
        else if (!string.IsNullOrWhiteSpace(eventOptions?.NewEventName))
        {
            targetEvent = new CompetitionEvent { Name = eventOptions.NewEventName.Trim() };
            _db.CompetitionEvents.Add(targetEvent);
            await _db.SaveChangesAsync(); // нужен Id для привязки дней
            diagnosticLog.Add($"Event: created new #{targetEvent.Id} \"{targetEvent.Name}\"");
        }

        foreach (var (item, idx) in items.Select((r, i) => (r, i)))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.EventStyleName))
                    throw new InvalidOperationException("Обязательное поле event_style_name отсутствует или пустое");
                if (string.IsNullOrWhiteSpace(item.EventStyleLen))
                    throw new InvalidOperationException("Обязательное поле event_style_len отсутствует или пустое");

                // 1. Style
                var styleName = NormalizeStyleName(item.EventStyleName);
                if (!styleCache.TryGetValue(styleName, out var style))
                {
                    style = new Style { Name = styleName };
                    _db.Styles.Add(style);
                    await _db.SaveChangesAsync(); // need ID immediately for ResultRecord.StyleId
                    styleCache[styleName] = style;
                }

                // 2. Country
                Country? country = null;
                var countryCode = (item.Country ?? "").Trim().ToUpperInvariant();
                if (!string.IsNullOrEmpty(countryCode))
                {
                    if (!countryCache.TryGetValue(countryCode, out country))
                    {
                        country = await _db.Countries.FirstOrDefaultAsync(c => c.CountryCode == countryCode);
                        if (country == null)
                        {
                            country = new Country { CountryCode = countryCode, CountryName = countryCode };
                            _db.Countries.Add(country);
                            await _db.SaveChangesAsync();
                        }
                        countryCache[countryCode] = country;
                    }
                }

                // 3. Competition
                // При привязке к событию дедуп и хранимое имя идут по ИМЕНИ СОБЫТИЯ (общее имя),
                // чтобы дни одного события не плодили дубликаты и совпадали с unique-индексом
                // (Name, Date, PoolType). Оригинальный заголовок файла уходит в SubName.
                var displayName = targetEvent != null ? targetEvent.Name : (item.Competition ?? string.Empty);
                // NormalizePoolType — как и в FindExistingCompetitionsAsync/ImportCompetitionMatcher:
                // индекс построен по уже нормализованному Competition.PoolType из БД, а item.PoolType
                // приходит из файла в сыром виде ("25"/"25m") — сравнивать нужно нормализованное с
                // нормализованным, иначе тот же промах мимо кэша, что и с Name/SubName.
                var compKey = ImportCompetitionMatcher.Key(displayName, item.Date ?? string.Empty, NormalizePoolType(item.PoolType));
                if (!competitionCache.TryGetValue(compKey, out var competition))
                {
                    competition = new Competition
                    {
                        Name = displayName,
                        CountryId = country?.Id,
                        Date = item.Date ?? string.Empty,
                        PoolType = NormalizePoolType(item.PoolType),
                        IsMasters = isMasters || (item.IsMasters ?? false),
                        IsAward = isAward || (item.IsAward ?? false),
                        ShowCombineAllResults = showCombine,
                        EventId = targetEvent?.Id,
                        SubName = targetEvent != null ? (item.Competition ?? string.Empty) : null,
                        DayNumber = targetEvent != null ? nextDayNumber++ : null
                    };
                    _db.Competitions.Add(competition);
                    await _db.SaveChangesAsync();
                    competitionCache[compKey] = competition;
                    // Индексируем и по SubName (тем же правилом, что ImportCompetitionMatcher.BuildIndex),
                    // чтобы последующие строки этого же импорта и повторный импорт того же файла
                    // находили соревнование по обоим ключам.
                    if (!string.IsNullOrWhiteSpace(competition.SubName))
                        competitionCache.TryAdd(
                            ImportCompetitionMatcher.Key(competition.SubName, competition.Date, competition.PoolType),
                            competition);
                    verifiedNewCompetitions.Add(competition.Id);
                    touchedCompetitionKeys.Add(compKey);

                    // Привязка нового соревнования к категориям (membership) из файла.
                    // Дубли ключей игнорируем; неизвестные ключи пропускаем с заметкой в лог.
                    foreach (var key in effectiveCategoryKeys.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!categoryCache.TryGetValue(key, out var cat))
                        {
                            diagnosticLog.Add($"Category key not found, skipped: {key}");
                            continue;
                        }
                        var order = categoryNextOrder.TryGetValue(cat.Id, out var o) ? o : 0;
                        _db.CategoryCompetitions.Add(new CategoryCompetition
                        {
                            CategoryId = cat.Id,
                            CompetitionId = competition.Id,
                            DisplayOrder = order
                        });
                        categoryNextOrder[cat.Id] = order + 1;
                    }
                }
                else
                {
                    if (!touchedCompetitionKeys.Contains(compKey))
                        touchedCompetitionKeys.Add(compKey);

                    if (!verifiedNewCompetitions.Contains(competition.Id))
                    {
                        var hasResults = await _db.Results.AnyAsync(r => r.CompetitionId == competition.Id);
                        if (hasResults)
                        {
                            if (!overwriteExisting)
                            {
                                await tx.RollbackAsync();
                                return new ImportResult
                                {
                                    Message = $"Дубль: соревнование «{competition.Name}» ({competition.Date}) уже содержит результаты в БД. Удалите его через Admin → История импортов, затем импортируйте снова.",
                                    ErrorMessages = [$"Competition ID {competition.Id} already has results"],
                                    DiagnosticLog = diagnosticLog
                                };
                            }

                            // Upsert-режим: не отбиваем, помечаем соревнование на пост-обработку
                            // (матчинг старых/новых результатов) после основного цикла.
                            upsertCompetitionIds.Add(competition.Id);
                            diagnosticLog.Add($"Overwrite: competition ID {competition.Id} «{competition.Name}» already has results — will upsert");
                        }
                        verifiedNewCompetitions.Add(competition.Id);
                    }
                }

                // 4. Club
                var clubName = (item.Club ?? string.Empty).Trim();
                var clubNameEn = (item.ClubEn ?? string.Empty).Trim();
                // Пустая графа клуба в протоколе → единый псевдоклуб «No club»
                // (клуб у Results NOT NULL; пловцам он, как и сборные, не проставляется).
                if (clubName.Length == 0 && clubNameEn.Length == 0)
                {
                    clubName = NoClubName;
                    clubNameEn = NoClubName;
                }
                var clubKey = $"{clubName}|{clubNameEn}";
                if (!clubCache.TryGetValue(clubKey, out var club))
                {
                    club = await _db.Clubs.FirstOrDefaultAsync(c => c.Name == clubName && c.NameEn == clubNameEn);
                    if (club == null)
                    {
                        club = new Club { Name = clubName, NameEn = clubNameEn, CountryId = country?.Id };

                        // Псевдоклуб: в графе клуба страна/сборная (Maccabiah: "USA",
                        // "Israel", "M25"). Метим флагом и привязываем страну по
                        // справочнику Countries (имя или alpha-3 код).
                        var pseudoCountry =
                            countryByName.GetValueOrDefault(clubName.Trim())
                            ?? countryByName.GetValueOrDefault(clubNameEn.Trim())
                            ?? countryCache.GetValueOrDefault(clubName.Trim());
                        if (pseudoCountry != null || PseudoTeamNames.Contains(clubName.Trim()) || clubName == NoClubName)
                        {
                            club.IsPseudo = true;
                            club.CountryId ??= pseudoCountry?.Id;
                            diagnosticLog.Add($"Pseudo club (country/team in club column): '{clubName}'");
                        }

                        _db.Clubs.Add(club);
                        await _db.SaveChangesAsync();
                    }
                    clubCache[clubKey] = club;
                }

                // 5. Swimmer
                var swimmerKey = SwimmerMatchKey(item.LastName, item.FirstName, item.BirthYear);
                // EN-ключ значим, только если EN-имя реально отличается от основного
                // (двуязычный парсер фоллбечит EN←HE при пустой английской строке).
                var swimmerKeyEn = !string.IsNullOrWhiteSpace(item.LastNameEn)
                    ? SwimmerMatchKey(item.LastNameEn, item.FirstNameEn, item.BirthYear)
                    : null;
                if (swimmerKeyEn == swimmerKey) swimmerKeyEn = null;

                // Анонимные relay-леги (без имени/фамилии и без года рождения — например,
                // DQ/NS-леги из протоколов, где парсер не смог восстановить личность) все
                // нормализуются в один и тот же пустой ключ. Если пускать их через обычный
                // dedup/кэш, несвязанные леги разных команд/стран схлопываются в один
                // Swimmer (баг: swimmer_id 8243 переиспользовался для легов Israel, USA,
                // Germany, Brazil и "Maccabiah MIX" в одном заезде). Поэтому для таких строк
                // матчинг и кэш полностью пропускаем — каждая строка получает свою запись.
                var isAnonymousSwimmer =
                    SwimmerDedupService.Normalize(item.LastName ?? "") == string.Empty &&
                    SwimmerDedupService.Normalize(item.FirstName ?? "") == string.Empty;

                Swimmer swimmer;
                if (isAnonymousSwimmer)
                {
                    swimmer = new Swimmer
                    {
                        LastName = item.LastName ?? string.Empty,
                        FirstName = item.FirstName ?? string.Empty,
                        LastNameEn = item.LastNameEn ?? string.Empty,
                        FirstNameEn = item.FirstNameEn ?? string.Empty,
                        BirthYear = item.BirthYear ?? 0
                    };
                    _db.Swimmers.Add(swimmer);
                    await _db.SaveChangesAsync();
                }
                else if (!swimmerCache.TryGetValue(swimmerKey, out swimmer!))
                {
                    // Фоллбек по EN-имени: пловец с заполненными *En-полями либо созданный
                    // из EN-протокола (английское имя в основных полях) — второй случай
                    // канонизируем: HE в основные поля, EN в *En.
                    if (swimmerKeyEn != null
                        && (swimmerCacheEn.TryGetValue(swimmerKeyEn, out swimmer)
                            || swimmerCache.TryGetValue(swimmerKeyEn, out swimmer)))
                    {
                        if (SwimmerMatchKey(swimmer.LastName, swimmer.FirstName, swimmer.BirthYear) == swimmerKeyEn)
                        {
                            swimmer.LastName = item.LastName ?? string.Empty;
                            swimmer.FirstName = item.FirstName ?? string.Empty;
                            swimmer.LastNameEn = item.LastNameEn ?? string.Empty;
                            swimmer.FirstNameEn = item.FirstNameEn ?? string.Empty;
                        }
                    }
                    else
                    {
                        // Not in pre-loaded cache — may still exist if duplicate key collision was silenced
                        swimmer = await _db.Swimmers.FirstOrDefaultAsync(s =>
                            s.LastName == (item.LastName ?? "") &&
                            s.FirstName == (item.FirstName ?? "") &&
                            s.BirthYear == (item.BirthYear ?? 0));
                    }

                    if (swimmer == null)
                    {
                        swimmer = new Swimmer
                        {
                            LastName = item.LastName ?? string.Empty,
                            FirstName = item.FirstName ?? string.Empty,
                            LastNameEn = item.LastNameEn ?? string.Empty,
                            FirstNameEn = item.FirstNameEn ?? string.Empty,
                            BirthYear = item.BirthYear ?? 0
                        };
                        _db.Swimmers.Add(swimmer);
                        await _db.SaveChangesAsync();
                    }

                    swimmerCache[swimmerKey] = swimmer;
                }

                // Дополнение данных спортсмена из результата (Gender, ClubId, CountryId)
                EnrichSwimmerFromResult(swimmer, item.EventStyleGender, club, country);

                // Дозаполнение пустых EN-имён у существующего пловца (двуязычный импорт)
                if (swimmerKeyEn != null && string.IsNullOrWhiteSpace(swimmer.LastNameEn))
                {
                    swimmer.LastNameEn = item.LastNameEn ?? string.Empty;
                    swimmer.FirstNameEn = item.FirstNameEn ?? string.Empty;
                    swimmerCacheEn.TryAdd(swimmerKeyEn, swimmer);
                }

                // 6. Relay — created but NOT saved yet; will be inserted via ResultRecord.Relay navigation.
                // Структурный состав ног (RelayMembers) резолвим по именам легов из парсера —
                // тем же матчингом, что и обычных пловцов, — чтобы эстафета была видна каждому
                // участнику, а не только «владельцу» строки (первой ноге).
                Relay? relay = null;
                if (item.IsRelay == true)
                {
                    relay = new Relay
                    {
                        TeamName = item.RelayTeamName,
                        SwimmersName = item.RelaySwimmersName
                    };
                    relay.Members = await ResolveRelayMembersAsync(item.RelaySwimmers, swimmerCache);
                }

                // 7. Gallery — created but NOT saved yet; will be inserted via ResultRecord.Gallery navigation
                Gallery? gallery = null;
                if (item.Gallery != null && item.Gallery.Count > 0)
                {
                    gallery = new Gallery();
                    foreach (var gi in item.Gallery)
                    {
                        gallery.Items.Add(new GalleryItem
                        {
                            Type = gi.Type ?? "image",
                            SourceType = gi.SourceType,
                            Url = gi.Url,
                            Code = gi.Code
                        });
                    }
                }

                // 8. ResultRecord — added to batch; Relay and Gallery inserted via navigation on batch save
                var record = new ResultRecord
                {
                    CompetitionId = competition.Id,
                    SwimmerId = swimmer.Id,
                    ClubId = club.Id,
                    StyleId = style.Id,
                    // Страна результата: явная из протокола, иначе — от псевдоклуба-сборной.
                    CountryId = country?.Id ?? (club.IsPseudo ? club.CountryId : null),
                    Relay = relay,      // EF Core inserts Relay and sets RelayId
                    Gallery = gallery,  // EF Core inserts Gallery + GalleryItems and sets GalleryId
                    CompetitionDate = ParseDate(item.Date),
                    Distance = item.EventStyleLen ?? string.Empty,
                    Gender = item.EventStyleGender ?? string.Empty,
                    AgeGroup = item.AgeGroup ?? string.Empty,
                    EventStyleAge = item.EventStyleAge ?? string.Empty,
                    Position = item.Position,
                    PositionAgeGroup = item.PositionAgeGroup,
                    Heat = item.Heat ?? 0,
                    Lane = item.Lane ?? 0,
                    TimeMillisecond = ParseTimeToMs(item.Time),
                    TimeOriginal = item.Time ?? string.Empty,
                    TimeSplit = item.TimeSplit ?? string.Empty,
                    TimeFail = item.TimeFail,
                    TimeFailNote = item.TimeFailNote,
                    InternationalPoints = item.InternationalPoints ?? 0,
                    Note = item.Note
                };

                resultBatch.Add(record);
                created++;
            }
            catch (Exception ex)
            {
                errors++;
                if (errorMessages.Count < 20)
                {
                    var ctx = $"competition='{item.Competition}', swimmer='{item.LastName} {item.FirstName}', date='{item.Date}', style='{item.EventStyleName}'";
                    errorMessages.Add($"Row {idx} ({ctx}): {ex.Message}");
                    if (ex.InnerException != null)
                        errorMessages.Add($"  Inner: {ex.InnerException.Message}");
                }
            }
        }

        // При наличии ошибок — откатываем всё соревнование целиком.
        if (errors > 0)
        {
            await tx.RollbackAsync();
            return new ImportResult
            {
                TotalRows = items.Count,
                Created = 0,
                Skipped = skipped,
                Errors = errors,
                ErrorMessages = errorMessages,
                DiagnosticLog = diagnosticLog,
                Message = $"Import rolled back: {errors} errors out of {items.Count} rows — no data was saved"
            };
        }

        // === Upsert: матчинг старых/новых результатов по соревнованиям, помеченным выше ===
        // (import-upsert-plan.md, шаг 2). Для каждого upsert-соревнования: сматченные строки
        // обновляются на месте (Id сохранён — UserMedia/HubGroupMedia не рвутся), новые
        // добавляются как обычно, исчезнувшие удаляются, если на них не навешано медиа.
        int updatedCount = 0, deletedCount = 0, skippedWithMediaCount = 0;
        var orphanCandidateSwimmerIds = new HashSet<int>();

        foreach (var competitionId in upsertCompetitionIds)
        {
            var newRowsForComp = resultBatch.Where(r => r.CompetitionId == competitionId).ToList();

            var oldRows = await _db.Results
                .Where(r => r.CompetitionId == competitionId)
                .Include(r => r.Relay)
                    .ThenInclude(rl => rl!.Members)
                .Include(r => r.Gallery)
                    .ThenInclude(g => g!.Items)
                .ToListAsync();

            var match = ResultMatcher.Match(
                oldRows, newRowsForComp,
                ResultMatcher.KeyOfPersisted, ResultMatcher.KeyOfTransient,
                ResultMatcher.SwimmerIdOfPersisted, ResultMatcher.SwimmerIdOfTransient);

            foreach (var (old, incoming) in match.Matched)
            {
                if (old.SwimmerId != incoming.SwimmerId)
                    orphanCandidateSwimmerIds.Add(old.SwimmerId); // может осиротеть анонимная заглушка (Р5, Маккабиада-кейс)

                ApplyPayloadUpdate(old, incoming);
                ApplyRelayUpdate(_db, old, incoming);
                ApplyGalleryUpdate(_db, old, incoming);

                // Не вставляем строку-«двойник» отдельным INSERT — она смёржена в old.
                resultBatch.Remove(incoming);
                updatedCount++;
            }

            if (!deleteMissing)
            {
                // DeleteMissing=false (default, ImportModels.cs): исчезнувшие из файла результаты
                // остаются нетронутыми — никакого удаления и, соответственно, никакой чистки
                // Relay/Gallery/пловцов-заглушек по ним (инцидент 2026-07-20: partial-переимпорт
                // с OverwriteExisting без явного согласия на удаление снёс 661 из 915 результатов).
                if (match.Deleted.Count > 0)
                    diagnosticLog.Add($"Upsert: {match.Deleted.Count} результат(ов) в comp {competitionId} отсутствуют в файле, но DeleteMissing=false — оставлены как есть");
            }
            else
            {
                foreach (var old in match.Deleted)
                {
                    var hasMedia = await _db.UserMedia.AnyAsync(m => m.ResultId == old.Id)
                        || await _db.HubGroupMedia.AnyAsync(m => m.ResultId == old.Id);
                    if (hasMedia)
                    {
                        skippedWithMediaCount++;
                        diagnosticLog.Add($"Upsert: result {old.Id} (comp {competitionId}) исчез из файла, но на нём есть UserMedia/HubGroupMedia — не удалён, разберитесь руками");
                        continue;
                    }

                    orphanCandidateSwimmerIds.Add(old.SwimmerId);

                    // Relay/Gallery — приватные 1:1-довески результата (Р4): при удалении результата
                    // становятся сиротами, подчищаем вместе с ним.
                    if (old.Relay != null)
                        _db.Relays.Remove(old.Relay);
                    if (old.Gallery != null)
                    {
                        if (old.Gallery.Items.Count > 0)
                            _db.GalleryItems.RemoveRange(old.Gallery.Items);
                        _db.Galleries.Remove(old.Gallery);
                    }

                    _db.Results.Remove(old);
                    deletedCount++;
                }
            }
        }

        // Batch insert: saves modified Swimmers + Relays + Galleries + GalleryItems + Results in one round trip
        if (resultBatch.Count > 0)
            _db.Results.AddRange(resultBatch);

        await _db.SaveChangesAsync();

        // Чистка сиротевших пловцов-заглушек после upsert (Р5) — тот же фильтр сиротства,
        // что и при удалении соревнования целиком (DeleteCompetitionCoreAsync). ExecuteDeleteAsync
        // здесь не годится (InMemory-провайдер тестов его не поддерживает, а сирот в один
        // upsert-запуск обычно единицы) — грузим и удаляем через tracked Remove.
        if (orphanCandidateSwimmerIds.Count > 0)
        {
            var orphanSwimmers = await _db.Swimmers
                .Where(s => orphanCandidateSwimmerIds.Contains(s.Id))
                .Where(s => !_db.Results.Any(r => r.SwimmerId == s.Id)
                    && !_db.RelayMembers.Any(m => m.SwimmerId == s.Id)  // нога эстафеты (FK Restrict) — не сирота
                    && !_db.HubGroupMembers.Any(m => m.SwimmerId == s.Id)
                    && !_db.HubGroupUserMembers.Any(m => m.SwimmerId == s.Id)
                    && !_db.UserFavorites.Any(f => f.SwimmerId == s.Id)
                    && !_db.UserMedia.Any(m => m.SwimmerId == s.Id)
                    && !_db.HubGroupMedia.Any(m => m.SwimmerId == s.Id)
                    && !_db.TrainingResults.Any(t => t.SwimmerId == s.Id)
                    && !_db.AppUsers.Any(u => u.SwimmerId == s.Id))
                .ToListAsync();
            if (orphanSwimmers.Count > 0)
            {
                _db.Swimmers.RemoveRange(orphanSwimmers);
                await _db.SaveChangesAsync();
            }
        }

        // Пересчёт диапазона дат события по всем его дням (min/max distinct дат).
        if (targetEvent != null)
        {
            var dayDates = await _db.Competitions
                .Where(c => c.EventId == targetEvent.Id)
                .Select(c => c.Date)
                .ToListAsync();

            var parsed = dayDates
                .Select(ParseDate)
                .Where(d => d != DateTime.MinValue)
                .Select(DateOnly.FromDateTime)
                .ToList();

            if (parsed.Count > 0)
            {
                targetEvent.StartDate = parsed.Min();
                targetEvent.EndDate = parsed.Max();
                await _db.SaveChangesAsync();
            }
        }

        // Штамп OrgCompId (compID сайта) на «первичное» соревнование этого импорта — приходит
        // только из Discovery-импорта. OrgCompId уникален (partial UNIQUE), поэтому держит его
        // один ряд (для многодневных — первый затронутый; кросс-линк резолвит его к событию).
        if (orgCompId is int oc && touchedCompetitionKeys.Count > 0)
        {
            var primary = competitionCache[touchedCompetitionKeys[0]];
            if (primary.OrgCompId != oc)
            {
                var takenByOther = await _db.Competitions.AnyAsync(c => c.OrgCompId == oc && c.Id != primary.Id);
                if (takenByOther)
                    diagnosticLog.Add($"OrgCompId {oc} уже занят другим соревнованием — штамп пропущен");
                else
                {
                    primary.OrgCompId = oc;
                    await _db.SaveChangesAsync();
                }
            }
        }

        // Запись в историю импортов — одна запись на весь импорт файла
        if (created > 0 && touchedCompetitionKeys.Count > 0)
        {
            var firstComp = competitionCache[touchedCompetitionKeys[0]];
            _db.ImportHistory.Add(new ImportHistory
            {
                CompetitionId = firstComp.Id,
                ImportFileName = fileName ?? "unknown.json",
                ImportDate = DateTime.UtcNow,
                Approved = false
            });
            await _db.SaveChangesAsync();
        }

        await tx.CommitAsync();
        await _cache.InvalidateAllAsync();

        // insertedCount — реально вставленные строки resultBatch (после того как сматченные
        // upsert-строки были из него удалены выше); created включает и их (полный успешный проход).
        var insertedCount = resultBatch.Count;
        var message = upsertCompetitionIds.Count > 0
            ? $"Import complete (upsert): {updatedCount} updated, {insertedCount} inserted"
                + (deleteMissing
                    ? $", {deletedCount} deleted" + (skippedWithMediaCount > 0 ? $", {skippedWithMediaCount} skipped (медиа)" : "")
                    : " (DeleteMissing off — исчезнувшие из файла результаты не удалялись)")
            : $"Import complete: {created} created, {skipped} skipped, {errors} errors";

        return new ImportResult
        {
            TotalRows = items.Count,
            Created = created,
            Skipped = skipped,
            Errors = errors,
            ErrorMessages = errorMessages,
            DiagnosticLog = diagnosticLog,
            Updated = updatedCount,
            Inserted = insertedCount,
            Deleted = deletedCount,
            SkippedWithMedia = skippedWithMediaCount,
            Message = message
        };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            // Разворачиваем цепочку inner-исключений: у EF/Npgsql реальная причина (нарушение
            // constraint, null в NOT NULL и т.п.) лежит во вложенном исключении, а не в ex.Message.
            var chain = new List<string>();
            for (var e = ex; e != null; e = e.InnerException)
                chain.Add($"{e.GetType().Name}: {e.Message}");
            return new ImportResult
            {
                TotalRows = items.Count,
                Created = 0,
                Errors = 1,
                ErrorMessages = ["Unexpected error, import rolled back: " + string.Join(" → ", chain)],
                DiagnosticLog = diagnosticLog,
                Message = "Import failed and was rolled back"
            };
        }
        });
    }

    /// <summary>
    /// Upsert (Р3): копирует payload-поля новой (несохранённой) строки результата в старую
    /// (уже в БД, tracked). Identity-поля ключа матчинга (CompetitionId/StyleId/Distance/Gender/
    /// Heat/Lane) не трогаем — они и так совпали. SwimmerId/ClubId/CountryId — тоже payload:
    /// переимпорт может заменить анонимную заглушку именованным пловцом (Р5).
    /// </summary>
    private static void ApplyPayloadUpdate(ResultRecord old, ResultRecord incoming)
    {
        old.SwimmerId = incoming.SwimmerId;
        old.ClubId = incoming.ClubId;
        old.CountryId = incoming.CountryId;
        old.CompetitionDate = incoming.CompetitionDate;
        old.AgeGroup = incoming.AgeGroup;
        old.EventStyleAge = incoming.EventStyleAge;
        old.Position = incoming.Position;
        old.PositionAgeGroup = incoming.PositionAgeGroup;
        old.TimeMillisecond = incoming.TimeMillisecond;
        old.TimeOriginal = incoming.TimeOriginal;
        old.TimeSplit = incoming.TimeSplit;
        old.TimeFail = incoming.TimeFail;
        old.TimeFailNote = incoming.TimeFailNote;
        old.InternationalPoints = incoming.InternationalPoints;
        old.Note = incoming.Note;
    }

    /// <summary>
    /// Резолвит ноги эстафеты (леги из парсера) в <see cref="RelayMember"/>: каждое имя —
    /// в Swimmer тем же матчингом, что и обычный пловец (кэш → БД → заглушка). Анонимные
    /// леги (без имени) пропускаем — членство им не атрибутировать. Дубли SwimmerId внутри
    /// одной эстафеты схлопываем (уникальный индекс RelayId+SwimmerId).
    /// </summary>
    private async Task<List<RelayMember>> ResolveRelayMembersAsync(
        List<RelaySwimmerJson>? legs, Dictionary<string, Swimmer> swimmerCache)
    {
        var members = new List<RelayMember>();
        if (legs == null || legs.Count == 0) return members;

        var seen = new HashSet<int>();
        foreach (var leg in legs)
        {
            var normLast = SwimmerDedupService.Normalize(leg.LastName ?? "");
            var normFirst = SwimmerDedupService.Normalize(leg.FirstName ?? "");
            if (normLast == string.Empty && normFirst == string.Empty)
                continue; // анонимная нога

            var key = SwimmerMatchKey(leg.LastName, leg.FirstName, leg.BirthYear);
            if (!swimmerCache.TryGetValue(key, out var swimmer))
            {
                swimmer = await _db.Swimmers.FirstOrDefaultAsync(s =>
                    s.LastName == (leg.LastName ?? "") &&
                    s.FirstName == (leg.FirstName ?? "") &&
                    s.BirthYear == (leg.BirthYear ?? 0));

                if (swimmer == null)
                {
                    swimmer = new Swimmer
                    {
                        LastName = leg.LastName ?? string.Empty,
                        FirstName = leg.FirstName ?? string.Empty,
                        LastNameEn = string.Empty,
                        FirstNameEn = string.Empty,
                        BirthYear = leg.BirthYear ?? 0
                    };
                    _db.Swimmers.Add(swimmer);
                    await _db.SaveChangesAsync();
                }
                swimmerCache[key] = swimmer;
            }

            if (!seen.Add(swimmer.Id)) continue;
            members.Add(new RelayMember
            {
                SwimmerId = swimmer.Id,
                LegOrder = leg.Order ?? 0,
                SplitTime = leg.SplitTime,
            });
        }
        return members;
    }

    /// <summary>
    /// Upsert (Р4): обновляет Relay существующего результата на месте (сохраняя Relay.Id), либо
    /// привязывает новую Relay-навигацию, если у старого результата её ещё не было.
    /// </summary>
    private static void ApplyRelayUpdate(SwimmDbContext db, ResultRecord old, ResultRecord incoming)
    {
        if (incoming.Relay == null) return;

        if (old.Relay != null)
        {
            old.Relay.TeamName = incoming.Relay.TeamName;
            old.Relay.SwimmersName = incoming.Relay.SwimmersName;
            // Состав ног обновляем ДИФФОМ по SwimmerId, а не «снести+добавить». Иначе для
            // пловца, оставшегося в составе, в одном SaveChanges попадают DELETE и INSERT
            // одного (RelayId, SwimmerId) — а на нём уникальный индекс, и EF не гарантирует
            // порядок → нарушение уникальности (проявляется только на реальном PG, InMemory
            // индекс игнорирует). Дифф: убрать ушедших, добавить новых, общих обновить на месте.
            if (old.Relay.Members.Count > 0)
                db.RelayMembers.RemoveRange(old.Relay.Members);
            old.Relay.Members.Clear();
            foreach (var m in incoming.Relay.Members)
                old.Relay.Members.Add(m);
        }
        else
        {
            old.Relay = incoming.Relay; // EF вставит новую Relay + Members и проставит RelayId при SaveChanges
        }
    }

    /// <summary>
    /// Upsert (Р4): обновляет состав Gallery существующего результата (Gallery.Id сохраняется,
    /// старые GalleryItems удаляются явно и заменяются новыми), либо привязывает новую Gallery,
    /// если у старого результата её ещё не было.
    /// </summary>
    private static void ApplyGalleryUpdate(SwimmDbContext db, ResultRecord old, ResultRecord incoming)
    {
        if (incoming.Gallery == null) return;

        if (old.Gallery != null)
        {
            if (old.Gallery.Items.Count > 0)
                db.GalleryItems.RemoveRange(old.Gallery.Items);
            old.Gallery.Items.Clear();
            foreach (var item in incoming.Gallery.Items)
                old.Gallery.Items.Add(item);
        }
        else
        {
            old.Gallery = incoming.Gallery; // EF вставит новую Gallery + GalleryItems и проставит GalleryId
        }
    }

    /// <summary>
    /// Для превью перед импортом (import-upsert-plan.md, шаг 3): по каждому дню файла ищет
    /// существующее соревнование по ключу (Name|SubName)|Date|PoolType — тому же, что использует
    /// ImportAsync при дедупе, плюс матч по SubName. Дни, привязанные к CompetitionEvent, получают
    /// Name = имя события, а исходно распарсенный заголовок уходит в SubName — поэтому матчим и по
    /// нему тоже, иначе превью не находит уже существующие дни переименованного события (см. кейс
    /// "Maccabiah 2025 -" в SubName при Name="Maccabiah 2026"). Пустой/неизвестный PoolType дня
    /// пропускается (ключ неполон — матч ненадёжен).
    /// </summary>
    public async Task<List<ExistingCompetitionMatch>> FindExistingCompetitionsAsync(IReadOnlyList<ParsedCompetitionSummary> competitions)
    {
        var result = new List<ExistingCompetitionMatch>();
        if (competitions.Count == 0) return result;

        var existing = await _db.Competitions.AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.SubName, c.Date, c.PoolType })
            .ToListAsync();
        var resultCounts = await _db.Results.AsNoTracking()
            .GroupBy(r => r.CompetitionId)
            .Select(g => new { CompetitionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CompetitionId, x => x.Count);

        // Общее правило матчинга «эта строка уже импортирована» (Name-или-SubName+Date+PoolType) —
        // см. ImportCompetitionMatcher. То же самое правило использует competitionCache в
        // ImportAsync ниже: раньше это были два разных правила, и импорт создавал дубль там, где
        // превью репортило совпадение (инцидент Maccabiah, docs/plans/import-upsert-plan.md).
        var byKey = ImportCompetitionMatcher.BuildIndex(
            existing, c => c.Name, c => c.SubName, c => c.Date, c => c.PoolType);

        foreach (var day in competitions)
        {
            (int Id, string Name)? found = null;
            if (!string.IsNullOrWhiteSpace(day.PoolType))
            {
                var key = ImportCompetitionMatcher.Key(day.Competition, day.Date, NormalizePoolType(day.PoolType));
                if (byKey.TryGetValue(key, out var m))
                    found = (m.Id, m.Name);
            }
            int? existingResultCount = found.HasValue
                ? resultCounts.GetValueOrDefault(found.Value.Id, 0)
                : null;
            result.Add(new ExistingCompetitionMatch(day.Competition, day.Date, found?.Id, found?.Name, existingResultCount));
        }

        return result;
    }

    /// <summary>
    /// Ключ матчинга пловца для кэша/поиска — нормализует фамилию и имя по правилам
    /// <see cref="SwimmerDedupService.Normalize"/> (гереш/апостроф, регистр, пробелы),
    /// чтобы не плодить дублей из-за непоследовательной пунктуации в протоколах isr.org.il.
    /// Хранимые Swimmer.LastName/FirstName нормализацию НЕ проходят.
    /// </summary>
    private static string SwimmerMatchKey(string? last, string? first, int? year) =>
        $"{SwimmerDedupService.Normalize(last ?? "")}|{SwimmerDedupService.Normalize(first ?? "")}|{year ?? 0}";

    /// <summary>
    /// Дополняет данные спортсмена полями из результатов:
    /// Gender (из event_style_gender), ClubId (FK на клуб) и CountryId (FK на страну).
    /// Обновляет только пустые поля — не перезаписывает уже заполненные.
    /// </summary>
    private static void EnrichSwimmerFromResult(Swimmer swimmer, string? gender, Club? club, Country? country)
    {
        if (string.IsNullOrEmpty(swimmer.Gender) && !string.IsNullOrWhiteSpace(gender))
            swimmer.Gender = gender;

        // Псевдоклуб (страна/сборная) — не «клуб пловца»; страна уходит в CountryId ниже.
        if (swimmer.ClubId == null && club != null && !club.IsPseudo)
            swimmer.ClubId = club.Id;

        if (swimmer.CountryId == null && country != null)
            swimmer.CountryId = country.Id;

        if (swimmer.CountryId == null && club is { IsPseudo: true, CountryId: not null })
            swimmer.CountryId = club.CountryId;
    }

    /// <summary>Псевдо-«клубы»-сборные Maccabiah, не являющиеся странами.</summary>
    private static readonly HashSet<string> PseudoTeamNames =
        new(StringComparer.OrdinalIgnoreCase) { "M25", "Maccabiah MIX" };

    /// <summary>Единый псевдоклуб для результатов с пустой графой клуба (Results.ClubId NOT NULL).</summary>
    private const string NoClubName = "No club";

    /// <summary>
    /// Пакетное дополнение спортсменов данными из таблицы Results.
    /// Заполняет Gender, ClubId и CountryId для записей, где эти поля ещё пусты,
    /// на основе самого свежего результата каждого спортсмена.
    /// </summary>
    public async Task<int> EnrichSwimmersFromResultsAsync()
    {
        var updated = 0;

        var swimmers = await _db.Swimmers
            .Where(s => s.Gender == null || s.ClubId == null || s.CountryId == null)
            .ToListAsync();

        if (swimmers.Count == 0)
            return 0;

        var swimmerIds = swimmers.Select(s => s.Id).ToHashSet();

        // Псевдоклубы (сборные) не считаются клубом пловца — исключаем их результаты
        // из выбора «самого свежего» источника ClubId.
        var latestResults = await _db.Results
            .Where(r => swimmerIds.Contains(r.SwimmerId) && !r.Club.IsPseudo)
            .GroupBy(r => r.SwimmerId)
            .Select(g => g.OrderByDescending(r => r.CompetitionDate).First())
            .Select(r => new { r.SwimmerId, r.Gender, r.ClubId, r.CountryId })
            .ToListAsync();

        var resultMap = latestResults.ToDictionary(r => r.SwimmerId);

        foreach (var swimmer in swimmers)
        {
            if (!resultMap.TryGetValue(swimmer.Id, out var latest))
                continue;

            var changed = false;

            if (string.IsNullOrEmpty(swimmer.Gender) && !string.IsNullOrWhiteSpace(latest.Gender))
            {
                swimmer.Gender = latest.Gender;
                changed = true;
            }

            if (swimmer.ClubId == null && latest.ClubId > 0)
            {
                swimmer.ClubId = latest.ClubId;
                changed = true;
            }

            if (swimmer.CountryId == null && latest.CountryId != null)
            {
                swimmer.CountryId = latest.CountryId;
                changed = true;
            }

            if (changed)
                updated++;
        }

        if (updated > 0)
            await _db.SaveChangesAsync();

        return updated;
    }

    /// <summary>
    /// Очищает все таблицы с импортированными данными.
    /// Сохраняет справочник Styles (seed-данные), AppUsers, AppRoles и пр.
    /// </summary>
    public async Task<ClearResult> ClearDataAsync()
    {
        var result = new ClearResult();

        // Порядок удаления — учитываем FK-зависимости: сначала дочерние таблицы
        result.Results = await _db.Results.ExecuteDeleteAsync();
        result.GalleryItems = await _db.GalleryItems.ExecuteDeleteAsync();
        result.Galleries = await _db.Galleries.ExecuteDeleteAsync();
        await _db.RelayMembers.ExecuteDeleteAsync(); // до Relays (FK Cascade подстрахует, но чистим явно)
        result.Relays = await _db.Relays.ExecuteDeleteAsync();
        result.Swimmers = await _db.Swimmers.ExecuteDeleteAsync();
        result.Clubs = await _db.Clubs.ExecuteDeleteAsync();
        result.ImportHistory = await _db.ImportHistory.ExecuteDeleteAsync();
        result.Competitions = await _db.Competitions.ExecuteDeleteAsync();
        result.CompetitionEvents = await _db.CompetitionEvents.ExecuteDeleteAsync();
        result.Countries = await _db.Countries.ExecuteDeleteAsync();

        result.Total = result.Results + result.GalleryItems + result.Galleries
            + result.Relays + result.Swimmers + result.Clubs + result.ImportHistory
            + result.Competitions + result.CompetitionEvents + result.Countries;

        // Сброс identity-счётчиков (только метаданные, на FK не влияет).
        //
        // TRUNCATE ... RESTART IDENTITY CASCADE здесь применять нельзя: Swimmers связана с
        // Sys_AppUsers (FK SwimmerId, ON DELETE SET NULL), CASCADE прошёл бы до AppUsers.
        var clearedTables = new[]
        {
            "Results", "GalleryItems", "Galleries", "RelayMembers", "Relays",
            "Swimmers", "Clubs", "Sys_ImportHistory", "Competitions", "CompetitionEvents", "Countries"
        };

        foreach (var table in clearedTables)
            await _db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE \"{table}\" ALTER COLUMN \"Id\" RESTART WITH 1;");

        await _cache.InvalidateAllAsync();
        return result;
    }

    /// <summary>
    /// Удаляет одно соревнование и все связанные с ним Results, Relays, Galleries, GalleryItems, ImportHistory.
    /// Swimmers, Clubs, Countries не трогает — они общие для всех соревнований.
    /// Порядок удаления учитывает FK Restrict на Results → Competition.
    /// </summary>
    public async Task<DeleteCompetitionResult?> DeleteCompetitionAsync(int competitionId)
    {
        var competition = await _db.Competitions.FindAsync(competitionId);
        if (competition == null) return null;

        // Атомарность: несколько ExecuteDeleteAsync (каждый авто-коммитит свой statement) + финальный
        // SaveChanges иначе не откатываются вместе — сбой в середине оставил бы полу-удалённые данные.
        // Retrying-стратегия Npgsql не допускает «голый» BeginTransaction, поэтому всю пачку
        // оборачиваем в execution strategy (при ретрае делегат целиком повторяется в новой транзакции).
        var strategy = _db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var r = await DeleteCompetitionCoreAsync(competition);

            // Удалили последний день события — само событие больше не нужно (пустые
            // события не отдаются селекторам и только мусорят; см. GetCompetitionEventsAsync).
            // В DeleteCompetitionEventAsync этой ветки нет: там событие удаляется явно.
            if (competition.EventId is int evId
                && !await _db.Competitions.AnyAsync(c => c.EventId == evId))
            {
                var emptyEvent = await _db.CompetitionEvents.FindAsync(evId);
                if (emptyEvent != null)
                {
                    _db.CompetitionEvents.Remove(emptyEvent);
                    await _db.SaveChangesAsync();
                }
            }

            await tx.CommitAsync();
            return r;
        });

        await _cache.InvalidateAllAsync();
        return result;
    }

    /// <summary>
    /// Удалить многодневное событие целиком: каскадно все дни (результаты, эстафеты, галереи,
    /// URL, история импорта) + сам <see cref="CompetitionEvent"/>. FK Competition→Event = SetNull,
    /// поэтому дни удаляем явно. Возвращает агрегированные счётчики; null — событие не найдено.
    /// </summary>
    public async Task<DeleteCompetitionResult?> DeleteCompetitionEventAsync(int eventId)
    {
        var ev = await _db.CompetitionEvents
            .Include(e => e.Days)
            .FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev == null) return null;

        var days = ev.Days.ToList();

        var strategy = _db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            var agg = new DeleteCompetitionResult
            {
                CompetitionId = eventId,
                CompetitionName = ev.Name
            };

            foreach (var day in days)
            {
                var r = await DeleteCompetitionCoreAsync(day);
                agg.Results += r.Results;
                agg.Relays += r.Relays;
                agg.GalleryItems += r.GalleryItems;
                agg.Galleries += r.Galleries;
                agg.ImportHistory += r.ImportHistory;
                agg.ResultUrls += r.ResultUrls;
                agg.Swimmers += r.Swimmers;
            }

            _db.CompetitionEvents.Remove(ev);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return agg;
        });

        await _cache.InvalidateAllAsync();
        return result;
    }

    /// <summary>
    /// Каскадное удаление ОДНОГО соревнования (дня) БЕЗ собственной транзакции и сброса кэша —
    /// вызывается внутри транзакции из <see cref="DeleteCompetitionAsync"/> и
    /// <see cref="DeleteCompetitionEventAsync"/>. Сущность <paramref name="competition"/> должна
    /// отслеживаться контекстом (Find/Include).
    /// </summary>
    private async Task<DeleteCompetitionResult> DeleteCompetitionCoreAsync(Domain.Entities.Competition competition)
    {
        int competitionId = competition.Id;

        // Собираем ID связанных Relays и Galleries до удаления Results
        var relayIds = await _db.Results
            .Where(r => r.CompetitionId == competitionId && r.RelayId != null)
            .Select(r => r.RelayId!.Value)
            .Distinct()
            .ToListAsync();

        var galleryIds = await _db.Results
            .Where(r => r.CompetitionId == competitionId && r.GalleryId != null)
            .Select(r => r.GalleryId!.Value)
            .Distinct()
            .ToListAsync();

        // Пловцы этого соревнования — после удаления результатов проверим их на сиротство
        // (дедуп A4: раньше удаление соревнования оставляло Swimmer-ов без единого результата).
        var swimmerIds = await _db.Results
            .Where(r => r.CompetitionId == competitionId)
            .Select(r => r.SwimmerId)
            .Distinct()
            .ToListAsync();

        var deletedGalleryItems = galleryIds.Count > 0
            ? await _db.GalleryItems.Where(gi => galleryIds.Contains(gi.GalleryId)).ExecuteDeleteAsync()
            : 0;

        // Results: FK Restrict → Competition, поэтому удаляем до Competition
        var deletedResults = await _db.Results
            .Where(r => r.CompetitionId == competitionId)
            .ExecuteDeleteAsync();

        // Ноги эстафет — до Relays (FK Cascade подстраховал бы, но чистим явно, как GalleryItems)
        if (relayIds.Count > 0)
            await _db.RelayMembers.Where(m => relayIds.Contains(m.RelayId)).ExecuteDeleteAsync();

        var deletedRelays = relayIds.Count > 0
            ? await _db.Relays.Where(r => relayIds.Contains(r.Id)).ExecuteDeleteAsync()
            : 0;

        var deletedGalleries = galleryIds.Count > 0
            ? await _db.Galleries.Where(g => galleryIds.Contains(g.Id)).ExecuteDeleteAsync()
            : 0;

        // ImportHistory → Competition: Cascade — удалится вместе с Competition, считаем заранее
        var deletedImportHistory = await _db.ImportHistory.CountAsync(h => h.CompetitionId == competitionId);

        // CompetitionResultUrls связаны по OrgCompId (не по Id). FK имеет ON DELETE CASCADE, поэтому
        // при удалении Competition они удалились бы сами — удаляем явно только ради точного счётчика.
        // OrgCompId уникален (AK_Competitions_OrgCompId), так что эти URL заведомо ничьи больше.
        var deletedResultUrls = 0;
        if (competition.OrgCompId is int orgCompId)
        {
            deletedResultUrls = await _db.CompetitionResultUrls
                .Where(u => u.OrgCompId == orgCompId)
                .ExecuteDeleteAsync();
        }

        _db.Competitions.Remove(competition);
        await _db.SaveChangesAsync();

        // Чистка сирот: пловец удаляется, только если после этого соревнования у него не
        // осталось НИЧЕГО — ни результатов, ни членств/избранного/медиа/тренировок/аккаунта.
        var deletedSwimmers = 0;
        if (swimmerIds.Count > 0)
        {
            deletedSwimmers = await _db.Swimmers
                .Where(s => swimmerIds.Contains(s.Id))
                .Where(s => !_db.Results.Any(r => r.SwimmerId == s.Id)
                    && !_db.RelayMembers.Any(m => m.SwimmerId == s.Id)  // нога эстафеты др. соревнования (FK Restrict)
                    && !_db.HubGroupMembers.Any(m => m.SwimmerId == s.Id)
                    && !_db.HubGroupUserMembers.Any(m => m.SwimmerId == s.Id)
                    && !_db.UserFavorites.Any(f => f.SwimmerId == s.Id)
                    && !_db.UserMedia.Any(m => m.SwimmerId == s.Id)
                    && !_db.HubGroupMedia.Any(m => m.SwimmerId == s.Id)
                    && !_db.TrainingResults.Any(t => t.SwimmerId == s.Id)
                    && !_db.AppUsers.Any(u => u.SwimmerId == s.Id))
                .ExecuteDeleteAsync();
        }

        return new DeleteCompetitionResult
        {
            CompetitionId = competitionId,
            CompetitionName = competition.Name,
            Results = deletedResults,
            Relays = deletedRelays,
            GalleryItems = deletedGalleryItems,
            Galleries = deletedGalleries,
            ImportHistory = deletedImportHistory,
            ResultUrls = deletedResultUrls,
            Swimmers = deletedSwimmers
        };
    }

    private static async Task<List<string>> DiagnoseJsonFieldsAsync(Stream stream)
    {
        var log = new List<string>();
        try
        {
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                log.Add($"JSON root: Array, length={root.GetArrayLength()}");
                if (root.GetArrayLength() > 0)
                {
                    log.Add("Fields of first element:");
                    foreach (var prop in root[0].EnumerateObject())
                        log.Add($"  {prop.Name}: {prop.Value.ValueKind} = {Truncate(prop.Value.GetRawText(), 120)}");
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                log.Add("JSON root: Object");
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        log.Add($"  {prop.Name}: Array[{prop.Value.GetArrayLength()}]");
                        if (prop.Value.GetArrayLength() > 0)
                        {
                            log.Add($"  Fields of {prop.Name}[0]:");
                            foreach (var p in prop.Value[0].EnumerateObject())
                                log.Add($"    {p.Name}: {p.Value.ValueKind} = {Truncate(p.Value.GetRawText(), 120)}");
                        }
                    }
                    else
                    {
                        log.Add($"  {prop.Name}: {prop.Value.ValueKind} = {Truncate(prop.Value.GetRawText(), 120)}");
                    }
                }
            }
            else
            {
                log.Add($"JSON root: {root.ValueKind}");
            }
        }
        catch (Exception ex)
        {
            log.Add($"Diagnostic parse failed: {ex.Message}");
        }
        return log;
    }

    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..maxLen] + "...";

    private static string NormalizePoolType(string? pt)
    {
        if (string.IsNullOrEmpty(pt)) return "";
        pt = pt.Trim().ToLowerInvariant().Replace("m", "");
        return pt switch
        {
            "25" => "25m",
            "50" => "50m",
            _ => pt + "m"
        };
    }

    internal static string NormalizeStyleName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "unknown";
        var norm = name.Trim().ToLowerInvariant().Replace(" ", "_");

        // Синонимы → канонический стиль (чтобы не плодить дубли в справочнике Styles).
        // Комплекс: источники иногда пишут просто "medley" — это individual_medley.
        return norm switch
        {
            "medley" or "im" or "individual" => "individual_medley",
            _ => norm
        };
    }

    private static DateTime ParseDate(string? date)
    {
        if (string.IsNullOrEmpty(date)) return DateTime.MinValue;

        string[] formats = [
            ParserConstants.DateFormat,
            "yyyy-MM-dd",
            "dd.MM.yyyy",
            "MM/dd/yyyy",
            "dd-MM-yyyy"
        ];

        if (DateTime.TryParseExact(date, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;

        if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt;

        return DateTime.MinValue;
    }

    /// <summary>
    /// Парсит строку времени вида "1:23.45" / "59.12" / "29.3" в миллисекунды.
    /// </summary>
    private static int? ParseTimeToMs(string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return null;

        time = time.Trim().Replace(',', '.');

        var match = Regex.Match(time, @"^(?:(\d+):)?(\d+)\.(\d+)$");
        if (!match.Success) return null;

        int minutes = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        int seconds = int.Parse(match.Groups[2].Value);
        string frac = match.Groups[3].Value;

        frac = frac.Length switch
        {
            1 => frac + "00",
            2 => frac + "0",
            3 => frac,
            _ => frac[..3]
        };
        int ms = int.Parse(frac);

        return (minutes * 60 + seconds) * 1000 + ms;
    }

    private class LenientBoolConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Null => false,
                JsonTokenType.Number => reader.TryGetInt64(out var n) ? n != 0 : reader.GetDouble() != 0,
                JsonTokenType.String => reader.GetString()?.Trim().ToLowerInvariant() is "true" or "1" or "yes",
                _ => throw new JsonException($"Cannot convert {reader.TokenType} to bool")
            };
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
            => writer.WriteBooleanValue(value);
    }

    private class LenientNullableBoolConverter : JsonConverter<bool?>
    {
        public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Null => null,
                JsonTokenType.Number => reader.TryGetInt64(out var n) ? n != 0 : reader.GetDouble() != 0,
                JsonTokenType.String => reader.GetString()?.Trim().ToLowerInvariant() is "true" or "1" or "yes",
                _ => throw new JsonException($"Cannot convert {reader.TokenType} to bool?")
            };
        }

        public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteBooleanValue(value.Value);
            else writer.WriteNullValue();
        }
    }

    private class LenientNullableIntConverter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.Number => reader.TryGetInt32(out var n) ? n : (int)reader.GetDouble(),
                JsonTokenType.String => int.TryParse(reader.GetString(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : null,
                _ => throw new JsonException($"Cannot convert {reader.TokenType} to int?")
            };
        }

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteNumberValue(value.Value);
            else writer.WriteNullValue();
        }
    }

    /// <summary>
    /// Терпимый конвертер строк: входные данные неоднородны — числовые поля
    /// (event_style_age, age_group, position_age_group, event_style_len) приходят то строкой
    /// ("44"), то числом (44). Приводим число/bool к строке, чтобы импорт не падал.
    /// </summary>
    private class LenientStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Null => null,
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Number => reader.TryGetInt64(out var n)
                    ? n.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
                _ => throw new JsonException($"Cannot convert {reader.TokenType} to string")
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }
}

/// <summary>
/// Константы формата дат, общие для парсера и импорта.
/// </summary>
public static class ParserConstants
{
    public const string DateFormat = "dd/MM/yyyy";
}

// ─── Internal JSON DTOs (десериализация входных данных) ───────────────────────

public class ResultWrap
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("results")]
    public List<ResultJsonItem>? Results { get; set; }

    [JsonPropertyName("is_masters")]
    public bool? IsMasters { get; set; }

    [JsonPropertyName("is_award")]
    public bool? IsAward { get; set; }

    [JsonPropertyName("show_combine_all_results")]
    public bool? ShowCombineAllResults { get; set; }

    /// <summary>
    /// Ключи категорий, к которым относится соревнование(я) этого файла
    /// (напр. ["results-main", "results-youth-team"]). Membership пишется на импорте;
    /// потом правится вручную через админку.
    /// </summary>
    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }
}

public class ResultJsonItem
{
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("competition")]
    public string? Competition { get; set; }

    [JsonPropertyName("is_masters")]
    public bool? IsMasters { get; set; }

    [JsonPropertyName("is_award")]
    public bool? IsAward { get; set; }

    [JsonPropertyName("age_group")]
    public string? AgeGroup { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("event_style_name")]
    public string? EventStyleName { get; set; }

    [JsonPropertyName("event_style_len")]
    public string? EventStyleLen { get; set; }

    [JsonPropertyName("event_style_gender")]
    public string? EventStyleGender { get; set; }

    [JsonPropertyName("event_style_age")]
    public string? EventStyleAge { get; set; }

    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    [JsonPropertyName("position")]
    public int? Position { get; set; }

    [JsonPropertyName("position_age_group")]
    public int? PositionAgeGroup { get; set; }

    [JsonPropertyName("heat")]
    public int? Heat { get; set; }

    [JsonPropertyName("lane")]
    public int? Lane { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name_en")]
    public string? LastNameEn { get; set; }

    [JsonPropertyName("first_name_en")]
    public string? FirstNameEn { get; set; }

    [JsonPropertyName("birth_year")]
    public int? BirthYear { get; set; }

    [JsonPropertyName("club")]
    public string? Club { get; set; }

    [JsonPropertyName("club_en")]
    public string? ClubEn { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("time_split")]
    public string? TimeSplit { get; set; }

    [JsonPropertyName("time_fail")]
    public bool TimeFail { get; set; }

    [JsonPropertyName("time_fail_note")]
    public string? TimeFailNote { get; set; }

    [JsonPropertyName("international_points")]
    public int? InternationalPoints { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("is_relay")]
    public bool? IsRelay { get; set; }

    [JsonPropertyName("relay_team_name")]
    public string? RelayTeamName { get; set; }

    [JsonPropertyName("relay_swimmers_name")]
    public string? RelaySwimmersName { get; set; }

    [JsonPropertyName("relay_swimmers")]
    public List<RelaySwimmerJson>? RelaySwimmers { get; set; }

    [JsonPropertyName("gallery")]
    public List<GalleryItemJson>? Gallery { get; set; }
}

public class RelaySwimmerJson
{
    [JsonPropertyName("order")]
    public int? Order { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("birth_year")]
    public int? BirthYear { get; set; }

    [JsonPropertyName("club")]
    public string? Club { get; set; }

    [JsonPropertyName("split_time")]
    public string? SplitTime { get; set; }
}

public class GalleryItemJson
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}
