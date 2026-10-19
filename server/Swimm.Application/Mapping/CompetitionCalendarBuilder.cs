using System.Globalization;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>День соревнования из справочника — плоская проекция для календаря (заполняет репозиторий).</summary>
public sealed class CalendarCompetitionRow
{
    public int Id { get; init; }
    /// <summary>Многодневное событие; null — старт однодневный.</summary>
    public int? EventId { get; init; }
    /// <summary>Общее имя события (у дня события <see cref="Name"/> то же, но берём из события).</summary>
    public string? EventName { get; init; }
    /// <summary>compID события на федерации (<c>CompetitionEvents.OrgCompId</c>).</summary>
    public int? EventOrgCompId { get; init; }
    public string Name { get; init; } = "";
    public DateOnly? Date { get; init; }
    public string PoolType { get; init; } = "";
    public bool IsChampionship { get; init; }
    public bool IsMasters { get; init; }
    public string Discipline { get; init; } = Disciplines.Swimming;
    public string? StandingKindOverride { get; init; }
    public int? OrgCompId { get; init; }
    /// <summary>У дня есть результаты — день «загружен».</summary>
    public bool HasResults { get; init; }
    /// <summary>Членство в категориях (сырые <c>Category.Key</c>).</summary>
    public IReadOnlyCollection<string> CategoryKeys { get; init; } = [];
    /// <summary>Источники стартового протокола дня (<c>CompetitionSources.OrgCompId</c>).</summary>
    public IReadOnlyCollection<int> SourceOrgCompIds { get; init; } = [];
}

/// <summary>День заявок стартового протокола (<c>CompetitionEntries</c>, без повторов по дню).</summary>
public sealed class CalendarEntryRow
{
    public int OrgCompId { get; init; }
    /// <summary>День справочника, если протокол уже импортирован (бэкфилл по compID).</summary>
    public int? CompetitionId { get; init; }
    public DateOnly Date { get; init; }
    public string CompName { get; init; } = "";
}

/// <summary>Строка «Входящих» (<c>Sys_DiscoveredCompetitions</c>) — только безопасные поля.</summary>
public sealed class CalendarDiscoveredRow
{
    public int OrgCompId { get; init; }
    /// <summary>Название с сайта федерации (иврит) — имя будущего старта и матч с затянутым.</summary>
    public string Name { get; init; } = "";
    public string? Venue { get; init; }
    public DateOnly Start { get; init; }
    public DateOnly End { get; init; }
    public string Discipline { get; init; } = Disciplines.Swimming;
    /// <summary>Первая объявленная дата перенесённого старта (<c>PreviousDateStart</c>); null — переноса нет.</summary>
    public DateOnly? PreviousStart { get; init; }
    public DateOnly? PreviousEnd { get; init; }
    /// <summary>Пропал из списка сайта (<c>MissingSince</c> не пуст) — отменён.</summary>
    public bool Missing { get; init; }
    /// <summary>new | imported | ignored; ignored (скрыт админом) в календарь не идёт.</summary>
    public string Status { get; init; } = DiscoveredCompetitionStatus.New;
}

/// <summary>Возрастная полоса категории (<c>Categories.MinAge/MaxAge</c>).</summary>
public sealed record CalendarCategoryBand(int? MinAge, int? MaxAge);

/// <summary>Всё, что репозиторий достал из базы для одного сезона.</summary>
public sealed class CalendarInputs
{
    public IReadOnlyList<CalendarCompetitionRow> Competitions { get; init; } = [];
    public IReadOnlyList<CalendarEntryRow> Entries { get; init; } = [];
    public IReadOnlyList<CalendarDiscoveredRow> Discovered { get; init; } = [];
    /// <summary>Флаг чемпионата из справки о старте (<c>CompetitionMeetInfos</c>, с ручной правкой) — по compID.</summary>
    public IReadOnlyDictionary<int, bool> MeetInfoChampionship { get; init; } = new Dictionary<int, bool>();
    /// <summary>Полосы возраста по <c>Category.Key</c>.</summary>
    public IReadOnlyDictionary<string, CalendarCategoryBand> CategoryBands { get; init; } =
        new Dictionary<string, CalendarCategoryBand>();
}

/// <summary>
/// Календарь сезона главной <c>/competitions</c> (docs/plans/competitions-home-plan.md, Б1).
/// Чистая функция без базы: запросы — в <c>CompetitionCalendarRepository</c>.
///
/// Как собирается старт:
/// <list type="bullet">
/// <item>Дни одного <c>EventId</c> — одна запись (kind=event), однодневный — своя (kind=competition).</item>
/// <item>Заявки, привязанные к дню справочника (<c>CompetitionId</c>) или к его compID (самого дня,
///   события или источника стартового протокола), принадлежат этому старту: у него
///   <c>has_start_list</c>, а у события их дни дополняют полосу дней.</item>
/// <item>Остальные заявки — предстоящий старт (kind=upcoming) на каждый compID (решение В2).</item>
/// <item>Строка «Входящих» сезона, которую никто не забрал (ни по compID, ни по имени+дате дня
///   справочника — <see cref="DiscoveryNameMatch"/>), — тоже предстоящий старт (решение В9), с
///   <c>source_url</c> на страницу федерации. Скрытые админом (ignored) не показываются.</item>
/// <item>Перенос (В4): у строки есть первая объявленная дата — дополнительный «призрак»
///   <c>postponed:{compID}</c> на прежних датах. Отмена: строка пропала из списка сайта — старт
///   kind=upcoming получает <c>cancelled</c>. Затянутые отменёнными не бывают.</item>
/// <item>Многодневка: дни справочника с результатами «загружены», дни программы (заявки) и
///   промежуток дат из «Входящих» — нет. Так событие, у которого затянут один день из трёх,
///   получает <c>in_progress</c> (решение В8: загружено всё — <c>complete</c>).</item>
/// </list>
/// Однодневный (kind=competition) промежутком из «Входящих» НЕ растягивается: его протокол уже
/// целиком у нас, а многодневка, сложенная в один день (memory: multiday-split), иначе вечно
/// висела бы «в процессе».
/// </summary>
public static class CompetitionCalendarBuilder
{
    /// <summary>Промежуток дат из «Входящих» длиннее этого — не программа, а серия/ошибка; не раскрываем.</summary>
    public const int MaxSpanDays = 7;

    /// <summary>Мастерс в справочнике категорий без полосы возраста — подпись из макета.</summary>
    public const string MastersAgesFallback = "25+";

    /// <summary>Страница старта на сайте федерации — <c>source_url</c> у kind=upcoming.</summary>
    public static string SourceUrlFor(int orgCompId) =>
        $"https://isr.org.il/comp.asp?compID={orgCompId.ToString(CultureInfo.InvariantCulture)}";

    public static CompetitionCalendarDto Build(
        int season, DateOnly today, IReadOnlyList<int> seasons, CalendarInputs inputs)
    {
        var discovered = inputs.Discovered
            .GroupBy(d => d.OrgCompId)
            .ToDictionary(g => g.Key, g => g.First());

        var meets = new List<MeetDraft>();

        // ── Справочник: событие = одна запись, однодневный — своя ───────────────────
        foreach (var g in inputs.Competitions.Where(c => c.EventId is not null).GroupBy(c => c.EventId!.Value))
        {
            var days = g.ToList();
            var first = days.OrderBy(d => d.Date ?? DateOnly.MaxValue).First();
            meets.Add(new MeetDraft(CalendarMeetKinds.Event, g.Key)
            {
                Name = first.EventName ?? first.Name,
                Rows = days,
                SpanFromDiscovered = true,
                PrimaryOrgCompId = first.EventOrgCompId ?? first.OrgCompId,
            });
        }

        foreach (var c in inputs.Competitions.Where(c => c.EventId is null))
        {
            meets.Add(new MeetDraft(CalendarMeetKinds.Competition, c.Id)
            {
                Name = c.Name,
                Rows = [c],
                SpanFromDiscovered = false,
                PrimaryOrgCompId = c.OrgCompId,
            });
        }

        // ── Чьи заявки: по дню справочника и по compID ────────────────────────────
        var meetByCompetitionId = new Dictionary<int, MeetDraft>();
        var meetByOrgCompId = new Dictionary<int, MeetDraft>();
        foreach (var m in meets)
        {
            foreach (var r in m.Rows)
            {
                meetByCompetitionId[r.Id] = m;
                foreach (var oc in RowOrgCompIds(r)) m.OrgCompIds.Add(oc);
            }
            foreach (var oc in m.OrgCompIds) meetByOrgCompId.TryAdd(oc, m);
        }

        var upcoming = new Dictionary<int, MeetDraft>();
        foreach (var e in inputs.Entries)
        {
            var owner = e.CompetitionId is int cid && meetByCompetitionId.TryGetValue(cid, out var byDay) ? byDay
                : meetByOrgCompId.GetValueOrDefault(e.OrgCompId);

            if (owner is null)
            {
                if (!upcoming.TryGetValue(e.OrgCompId, out owner))
                {
                    owner = new MeetDraft(CalendarMeetKinds.Upcoming, e.OrgCompId)
                    {
                        Name = e.CompName,
                        SpanFromDiscovered = true,
                        PrimaryOrgCompId = e.OrgCompId,
                    };
                    owner.OrgCompIds.Add(e.OrgCompId);
                    upcoming[e.OrgCompId] = owner;
                }
            }
            else
            {
                owner.OrgCompIds.Add(e.OrgCompId);
            }

            owner.HasStartList = true;
            owner.EntryDates.Add(e.Date);
        }
        meets.AddRange(upcoming.Values);

        // ── «Входящие» без затянутого и без заявок — будущий старт (В9) ─────────────
        // Затянута = compID уже у какого-то старта ИЛИ имя+дата совпали с днём справочника
        // (то же правило, что «уже импортировано» в /Admin/Competitions). Совпавшая по имени строка
        // становится источником этого старта: площадка и промежуток дат — от неё.
        var claimed = meets.SelectMany(m => m.OrgCompIds).ToHashSet();
        var dated = inputs.Competitions.Where(c => c.Date is not null).ToList();
        foreach (var dc in discovered.Values.OrderBy(d => d.OrgCompId))
        {
            if (claimed.Contains(dc.OrgCompId)) continue;

            var day = dated.FirstOrDefault(c => DiscoveryNameMatch.Matches(dc.Name, dc.Start, dc.End, c.Name, c.Date!.Value));
            if (day is not null && meetByCompetitionId.TryGetValue(day.Id, out var byName))
            {
                byName.OrgCompIds.Add(dc.OrgCompId);
                claimed.Add(dc.OrgCompId);
                continue;
            }

            if (dc.Status == IgnoredStatus) continue; // скрыт админом — не наш старт

            var draft = new MeetDraft(CalendarMeetKinds.Upcoming, dc.OrgCompId)
            {
                Name = dc.Name,
                SpanFromDiscovered = true,
                PrimaryOrgCompId = dc.OrgCompId,
                FromDiscovered = true,
            };
            draft.OrgCompIds.Add(dc.OrgCompId);
            meets.Add(draft);
            claimed.Add(dc.OrgCompId);
        }

        // ── Сборка DTO ───────────────────────────────────────────────────────────
        var result = new List<CalendarMeetDto>(meets.Count);
        var dtoByOrgCompId = new Dictionary<int, CalendarMeetDto>();
        foreach (var m in meets)
        {
            var dto = BuildMeet(m, today, discovered, inputs);
            if (dto is null) continue;
            foreach (var oc in m.PrimaryOrgCompId is int p ? m.OrgCompIds.Prepend(p) : m.OrgCompIds)
                dtoByOrgCompId.TryAdd(oc, dto);
            if (InSeason(dto.Start, season)) result.Add(dto);
        }

        // ── Перенос (В4): «призрак» на прежних датах, поля — как у самого старта ─────
        foreach (var dc in discovered.Values)
        {
            if (dc.PreviousStart is not DateOnly prevStart || dc.Status == IgnoredStatus) continue;
            if (!dtoByOrgCompId.TryGetValue(dc.OrgCompId, out var moved)) continue;

            var prevEnd = dc.PreviousEnd is DateOnly pe && pe >= prevStart ? pe : prevStart;
            var ghostDays = SpanDays(prevStart, prevEnd);
            var ghost = new CalendarMeetDto
            {
                Key = $"postponed:{dc.OrgCompId}",
                Kind = CalendarMeetKinds.Upcoming,
                Id = dc.OrgCompId,
                Name = moved.Name,
                Start = Iso(prevStart),
                End = Iso(prevEnd),
                Days = ghostDays.Select(d => new CalendarMeetDayDto { Date = Iso(d), Loaded = false }).ToList(),
                DaysLoaded = 0,
                DaysTotal = ghostDays.Count,
                Pool = moved.Pool,
                Venue = moved.Venue,
                Type = moved.Type,
                IsChampionship = moved.IsChampionship,
                Group = moved.Group,
                Status = CalendarMeetStatuses.Postponed,
                PostponedTo = Iso(dc.Start),
                HasStartList = moved.HasStartList,
                SourceUrl = SourceUrlFor(dc.OrgCompId),
            };
            if (InSeason(ghost.Start, season)) result.Add(ghost);
        }

        return new CompetitionCalendarDto
        {
            Season = season,
            SeasonLabel = SeasonMath.Label(season),
            Today = Iso(today),
            Seasons = seasons,
            Meets = result
                .OrderBy(m => m.Start, StringComparer.Ordinal)
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private static CalendarMeetDto? BuildMeet(
        MeetDraft m, DateOnly today,
        IReadOnlyDictionary<int, CalendarDiscoveredRow> discovered, CalendarInputs inputs)
    {
        // Дни: справочник (загружен, если есть результаты) ∪ заявки ∪ промежуток «Входящих».
        var days = new SortedDictionary<DateOnly, bool>();
        foreach (var r in m.Rows)
        {
            if (r.Date is not DateOnly d) continue;
            days[d] = days.GetValueOrDefault(d) || r.HasResults;
        }

        if (m.Kind != CalendarMeetKinds.Competition)
        {
            foreach (var d in m.EntryDates) days.TryAdd(d, false);

            if (m.SpanFromDiscovered)
            {
                foreach (var oc in m.OrgCompIds)
                {
                    if (!discovered.TryGetValue(oc, out var dc)) continue;
                    if (dc.End < dc.Start || dc.End.DayNumber - dc.Start.DayNumber >= MaxSpanDays) continue;
                    for (var d = dc.Start; d <= dc.End; d = d.AddDays(1)) days.TryAdd(d, false);
                }
            }

            // Старт только из «Входящих» с промежутком длиннее недели: не программа, но старт и
            // конец — факт с сайта, без них старта в календаре не было бы вовсе.
            if (days.Count == 0 && m.FromDiscovered && m.PrimaryOrgCompId is int ownId
                && discovered.TryGetValue(ownId, out var self))
                foreach (var d in SpanDays(self.Start, self.End)) days.TryAdd(d, false);
        }
        else if (days.Count == 0)
        {
            foreach (var d in m.EntryDates) days.TryAdd(d, false);
        }

        if (days.Count == 0) return null;

        var start = days.Keys.First();
        var end = days.Keys.Last();
        var loaded = days.Values.Count(v => v);

        var venue = VenueFor(m, discovered);

        // Флаги — из справочника, если старт у нас есть; у предстоящего — из «Входящих» и справки.
        string type;
        bool isChampionship;
        string pool;
        CalendarGroupDto? group;
        if (m.Rows.Count > 0)
        {
            var name = m.Name;
            var rows = m.Rows;
            var discipline = rows.Any(r => r.Discipline == Disciplines.Artistic) ? Disciplines.Artistic : rows[0].Discipline;
            var overrideKind = rows.Select(r => r.StandingKindOverride).FirstOrDefault(o => o is not null);
            type = TypeFor(name, discipline, overrideKind);
            isChampionship = rows.Any(r => r.IsChampionship);
            pool = rows.Select(r => r.PoolType).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "";
            var keys = rows.SelectMany(r => r.CategoryKeys).ToHashSet(StringComparer.Ordinal);
            group = GroupFor(rows.Any(r => r.IsMasters), keys, name, isChampionship, pool, overrideKind, inputs.CategoryBands);
        }
        else
        {
            var dc = m.OrgCompIds.Select(oc => discovered.GetValueOrDefault(oc)).FirstOrDefault(d => d is not null);
            var discipline = dc?.Discipline ?? Disciplines.GuessFromName(m.Name);
            type = TypeFor(m.Name, discipline, null);
            // Чемпионат: справка о старте (с ручной правкой), а у старта только из «Входящих» без
            // справки — по названию (то же правило, что в админке).
            isChampionship = m.FromDiscovered
                && !m.OrgCompIds.Any(oc => inputs.MeetInfoChampionship.ContainsKey(oc))
                ? CompetitionNameRules.IsChampionship(m.Name)
                : m.OrgCompIds.Any(oc => inputs.MeetInfoChampionship.GetValueOrDefault(oc));
            pool = "";
            group = GroupFor(false, [], m.Name, isChampionship, pool, null, inputs.CategoryBands);
        }

        // Длина бассейна у моря и артистики — не факт о старте (в справочнике там стоит что попало).
        if (type != CalendarMeetTypes.Pool) pool = "";

        // Предстоящий со строкой «Входящих»: ссылка на федерацию и отмена (пропал из списка сайта).
        // Затянутые (event/competition) отменёнными не бывают и ссылки наружу не получают.
        var own = m.Kind == CalendarMeetKinds.Upcoming && m.PrimaryOrgCompId is int p
            ? discovered.GetValueOrDefault(p)
            : null;
        var status = StatusFor(start, end, today, loaded, days.Count);
        if (own is { Missing: true } && own.Status != IgnoredStatus) status = CalendarMeetStatuses.Cancelled;

        return new CalendarMeetDto
        {
            Key = $"{m.Kind}:{m.Id}",
            Kind = m.Kind,
            Id = m.Id,
            Name = m.Name,
            Start = Iso(start),
            End = Iso(end),
            Days = days.Select(kv => new CalendarMeetDayDto { Date = Iso(kv.Key), Loaded = kv.Value }).ToList(),
            DaysLoaded = loaded,
            DaysTotal = days.Count,
            Pool = pool,
            Venue = venue,
            Type = type,
            IsChampionship = isChampionship,
            Group = group,
            Status = status,
            PostponedTo = null,
            HasStartList = m.HasStartList,
            SourceUrl = own is null ? null : SourceUrlFor(own.OrgCompId),
        };
    }

    // ── Правила (публичные — их и проверяют тесты) ─────────────────────────────

    /// <summary>
    /// Статус старта по датам. Cancelled/Postponed здесь не возникают — их ставит сборка по
    /// пометкам «Входящих» (шаг В4′): отмена у kind=upcoming, перенос — отдельным «призраком».
    /// Многодневка с частью загруженных дней — <c>in_progress</c>, со всеми — <c>complete</c>
    /// (В8), независимо от того, прошли ли даты: протокол дня мог приехать раньше остальных.
    /// </summary>
    public static string StatusFor(DateOnly start, DateOnly end, DateOnly today, int daysLoaded, int daysTotal)
    {
        if (daysTotal > 1 && daysLoaded > 0)
            return daysLoaded < daysTotal ? CalendarMeetStatuses.InProgress : CalendarMeetStatuses.Complete;
        if (start > today) return CalendarMeetStatuses.Upcoming;
        if (end >= today) return CalendarMeetStatuses.Live;
        return CalendarMeetStatuses.Done;
    }

    /// <summary>
    /// Тип старта для фильтра календаря. Артистика — по хранимому <c>Discipline</c> (правится в
    /// админке); открытая вода — по <c>StandingKindOverride='openwater'</c> (единственный
    /// хранимый признак моря, Р20) или по названию (решение В3), пока нет поля <c>WaterKind</c>.
    /// </summary>
    public static string TypeFor(string? name, string? discipline, string? standingKindOverride)
    {
        if (discipline == Disciplines.Artistic) return CalendarMeetTypes.Artistic;
        if (standingKindOverride == StandingKinds.OpenWater || LooksLikeOpenWater(name)) return CalendarMeetTypes.OpenWater;
        return CalendarMeetTypes.Pool;
    }

    /// <summary>«מים פתוחים», «צליחה» (заплыв через водоём), «open water» в двуязычных выгрузках.</summary>
    public static bool LooksLikeOpenWater(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && (name.Contains("מים פתוחים", StringComparison.Ordinal)
            || name.Contains("צליחת", StringComparison.Ordinal)
            || name.Contains("צליחה", StringComparison.Ordinal)
            || name.Contains("open water", StringComparison.OrdinalIgnoreCase)
            || name.Contains("openwater", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Бейдж группы (решение В6, «продумать позже»). Буква — из категорий соревнования
    /// (<see cref="CompetitionCategories.Canonical"/>); у старта без единой категории (предстоящий
    /// по заявкам) — догадка по названию. Возраст — полоса категории из справочника.
    /// Сезон ❄/☀ — по חורף/קיץ в названии, у чемпионата без пометки — по бассейну
    /// (<see cref="StandingKinds.Resolve"/>: 25м зимний, 50м летний).
    /// </summary>
    public static CalendarGroupDto? GroupFor(
        bool isMasters, IReadOnlyCollection<string> categoryKeys, string? name,
        bool isChampionship, string? poolType, string? standingKindOverride,
        IReadOnlyDictionary<string, CalendarCategoryBand> bands)
    {
        var canonical = CompetitionCategories.Canonical(isMasters, categoryKeys);
        if (canonical is null && categoryKeys.Count == 0) canonical = CanonicalFromName(name);
        if (canonical is null) return null;

        var (letter, label, key) = canonical switch
        {
            CompetitionCategories.Kids8_11 => ("K", "Kids", "results-kids-team"),
            CompetitionCategories.Young11_14 => ("Y", "Young", "results-youth-team"),
            CompetitionCategories.Juniors => ("J", "Juniors", "results-junior-results"),
            CompetitionCategories.Adults => ("A", "Adults", "results-main"),
            _ => ("M", "Masters", "results-masters"),
        };

        var ages = AgesLabel(bands.GetValueOrDefault(key));
        if (ages is null && letter == "M") ages = MastersAgesFallback;

        return new CalendarGroupDto
        {
            Letter = letter,
            Label = label,
            Ages = ages,
            Season = SeasonMarkFor(name, isChampionship, poolType, standingKindOverride),
        };
    }

    /// <summary>Каноническая ступень по названию — только для старта без категорий.</summary>
    public static string? CanonicalFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name.Contains("מאסטרס", StringComparison.Ordinal) || name.Contains("masters", StringComparison.OrdinalIgnoreCase))
            return CompetitionCategories.Masters;
        if (name.Contains("ילדים", StringComparison.Ordinal) || name.Contains("8-11", StringComparison.Ordinal))
            return CompetitionCategories.Kids8_11;
        if (name.Contains("צעירים", StringComparison.Ordinal) || name.Contains("11-14", StringComparison.Ordinal)
            || name.Contains("12-14", StringComparison.Ordinal))
            return CompetitionCategories.Young11_14;
        if (name.Contains("נוער", StringComparison.Ordinal) || name.Contains("junior", StringComparison.OrdinalIgnoreCase))
            return CompetitionCategories.Juniors;
        if (name.Contains("בוגרים", StringComparison.Ordinal))
            return CompetitionCategories.Adults;
        return null;
    }

    /// <summary>"winter" | "summer" | null. Название сильнее вывода по бассейну.</summary>
    public static string? SeasonMarkFor(string? name, bool isChampionship, string? poolType, string? standingKindOverride)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (name.Contains("חורף", StringComparison.Ordinal) || name.Contains("winter", StringComparison.OrdinalIgnoreCase))
                return StandingKinds.Winter;
            if (name.Contains("קיץ", StringComparison.Ordinal) || name.Contains("summer", StringComparison.OrdinalIgnoreCase))
                return StandingKinds.Summer;
        }

        var kind = StandingKinds.Resolve(isChampionship, poolType, standingKindOverride);
        return kind is StandingKinds.Winter or StandingKinds.Summer ? kind : null;
    }

    /// <summary>«8–11», «17+», null — полосы нет.</summary>
    public static string? AgesLabel(CalendarCategoryBand? band) => band switch
    {
        { MinAge: int min, MaxAge: int max } => $"{min}–{max}",
        { MinAge: int min } => $"{min}+",
        { MaxAge: int max } => $"≤{max}",
        _ => null,
    };

    // ── Внутреннее ──────────────────────────────────────────────────────────────

    private static IEnumerable<int> RowOrgCompIds(CalendarCompetitionRow r)
    {
        if (r.OrgCompId is int a) yield return a;
        if (r.EventOrgCompId is int b) yield return b;
        foreach (var s in r.SourceOrgCompIds) yield return s;
    }

    /// <summary>Площадка (В5): сначала compID самого старта, затем любой из его источников.</summary>
    private static string? VenueFor(MeetDraft m, IReadOnlyDictionary<int, CalendarDiscoveredRow> discovered)
    {
        var order = m.PrimaryOrgCompId is int p ? new[] { p }.Concat(m.OrgCompIds.Order()) : m.OrgCompIds.Order();
        foreach (var oc in order)
        {
            var v = discovered.GetValueOrDefault(oc)?.Venue?.Trim();
            if (!string.IsNullOrEmpty(v)) return v;
        }
        return null;
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private const string IgnoredStatus = DiscoveredCompetitionStatus.Ignored;

    private static bool InSeason(string isoStart, int season) =>
        DateOnly.TryParseExact(isoStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
        && SeasonMath.StartYearOf(start.ToDateTime(TimeOnly.MinValue)) == season;

    /// <summary>Дни промежутка «Входящих»: весь, если не длиннее <see cref="MaxSpanDays"/>, иначе только старт и конец.</summary>
    private static List<DateOnly> SpanDays(DateOnly start, DateOnly end)
    {
        if (end < start) return [start];
        if (end.DayNumber - start.DayNumber >= MaxSpanDays) return [start, end];
        var days = new List<DateOnly>();
        for (var d = start; d <= end; d = d.AddDays(1)) days.Add(d);
        return days;
    }

    private sealed class MeetDraft(string kind, int id)
    {
        public string Kind { get; } = kind;
        public int Id { get; } = id;
        public string Name { get; init; } = "";
        public IReadOnlyList<CalendarCompetitionRow> Rows { get; init; } = [];
        public bool SpanFromDiscovered { get; init; }
        public int? PrimaryOrgCompId { get; init; }
        public HashSet<int> OrgCompIds { get; } = [];
        public HashSet<DateOnly> EntryDates { get; } = [];
        public bool HasStartList { get; set; }
        /// <summary>Старт только из «Входящих» (ни справочника, ни заявок) — В9.</summary>
        public bool FromDiscovered { get; init; }
    }
}
