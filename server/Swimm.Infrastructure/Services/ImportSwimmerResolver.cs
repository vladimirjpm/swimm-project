using Microsoft.EntityFrameworkCore;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Кто этот пловец: строка протокола → карточка <see cref="Swimmer"/>. Вынесено из
/// <see cref="JsonImportService"/> (08.10.2026, docs/ARCHITECTURE.md §7), где цепочка жила внутри
/// метода импорта на 2600 строк и проверялась только целым импортом. Живёт ОДИН импорт: индексы
/// карточек грузятся в начале и дополняются по ходу, чтобы строки одного файла узнавали
/// пловцов, заведённых этим же файлом.
///
/// Порядок поиска личной строки (каждый следующий — только если предыдущий промахнулся):
/// <list type="number">
///   <item>точный ключ «фамилия|имя|год» (<see cref="SwimmerMatchKey"/>);</item>
///   <item>английское имя строки — среди EN-полей и среди карточек, заведённых латиницей
///     (такие канонизируются: иврит в основные поля, латиница в *En);</item>
///   <item>то же имя с другой границей фамилии (<see cref="SwimmerFullNameKey"/>, И-28);</item>
///   <item>имя латиницей в ивритском протоколе — по английским именам карточек;</item>
///   <item>точное совпадение в базе (карточка вне кэша);</item>
///   <item>другое написание при том же клубе (<see cref="SwimmerSpellingKey"/>, Р79);</item>
///   <item>новая карточка.</item>
/// </list>
/// После поиска — страж тёзок (И-11): тот же пловец за другой клуб в том же соревновании —
/// другой человек, ему заводится своя карточка.
/// </summary>
internal sealed class ImportSwimmerResolver
{
    private readonly SwimmDbContext _db;
    private readonly List<string> _diagnosticLog;

    private readonly Dictionary<string, Swimmer> _byKey = new();
    // Второй индекс по EN-имени — фоллбек-матчинг для двуязычных/EN-протоколов
    // (Maccabiah-кейс: пловец известен под английским именем).
    private readonly Dictionary<string, Swimmer> _byKeyEn = new();
    // Фоллбек «то же имя, другая граница фамилии» — см. SwimmerFullNameKey.
    private readonly Dictionary<string, Swimmer> _byFullName = new();
    // Фоллбек «то же имя другим написанием» (כתיב מלא/חסר) — см. SwimmerSpellingKey.
    private readonly Dictionary<string, List<Swimmer>> _bySpelling = new();

    // Страж тёзок (инцидент И-11, docs/data-integrity.md). Ключ матчинга пловца —
    // «фамилия|имя|годРождения», без клуба и без внешнего id, поэтому два ребёнка-тёзки
    // одного года рождения склеивались в одну запись ВСЕГДА. Признак, что это разные
    // люди: в ОДНОМ соревновании пловец не может выступать за два клуба.
    //   _seenClubInCompetition: (соревнование, пловец) → канонический клуб этого прогона;
    //   _namesakeByClub:        (соревнование, ключ имени, клуб) → выделенный тёзка.
    private readonly Dictionary<(int CompetitionId, int SwimmerId), int> _seenClubInCompetition = new();
    private readonly Dictionary<(int CompetitionId, string NameKey, int ClubId), Swimmer> _namesakeByClub = new();

    private ImportSwimmerResolver(SwimmDbContext db, List<string> diagnosticLog)
    {
        _db = db;
        _diagnosticLog = diagnosticLog;
    }

    /// <summary>
    /// Индексы по всем карточкам. Карточки ОТСЛЕЖИВАЮТСЯ: импорт дописывает в них пол, клуб,
    /// страну и EN-имя, изменения уходят общим сохранением.
    /// </summary>
    public static async Task<ImportSwimmerResolver> LoadAsync(SwimmDbContext db, List<string> diagnosticLog)
    {
        var resolver = new ImportSwimmerResolver(db, diagnosticLog);
        foreach (var s in await db.Swimmers.ToListAsync())
        {
            resolver._byKey.TryAdd(SwimmerMatchKey(s.LastName, s.FirstName, s.BirthYear), s);
            resolver._byFullName.TryAdd(SwimmerFullNameKey(s.LastName, s.FirstName, s.BirthYear), s);
            AddBySpelling(resolver._bySpelling, s);
            if (!string.IsNullOrWhiteSpace(s.LastNameEn))
                resolver._byKeyEn.TryAdd(SwimmerMatchKey(s.LastNameEn, s.FirstNameEn, s.BirthYear), s);
        }
        return resolver;
    }

    /// <summary>
    /// Карточка личной (или эстафетной — по «владельцу» строки) строки протокола. <c>KeyEn</c> —
    /// ключ английского имени строки, если оно есть и отличается от основного; нужен
    /// <see cref="FillEnglishName"/> после того, как импорт дополнит карточку.
    /// </summary>
    public async Task<(Swimmer Swimmer, string? KeyEn)> ResolveAsync(ResultJsonItem item, Club? club, int competitionId)
    {
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
                BirthYear = item.BirthYear ?? 0
            };
            SetEnglishName(swimmer, item);
            _db.Swimmers.Add(swimmer);
            await _db.SaveChangesAsync();
        }
        else if (!_byKey.TryGetValue(swimmerKey, out swimmer!))
        {
            // Фоллбек по EN-имени: пловец с заполненными *En-полями либо созданный
            // из EN-протокола (английское имя в основных полях) — второй случай
            // канонизируем: HE в основные поля, EN в *En.
            if (swimmerKeyEn != null
                && (_byKeyEn.TryGetValue(swimmerKeyEn, out swimmer)
                    || _byKey.TryGetValue(swimmerKeyEn, out swimmer)))
            {
                if (SwimmerMatchKey(swimmer.LastName, swimmer.FirstName, swimmer.BirthYear) == swimmerKeyEn)
                {
                    swimmer.LastName = item.LastName ?? string.Empty;
                    swimmer.FirstName = item.FirstName ?? string.Empty;
                    SetEnglishName(swimmer, item);
                }
            }
            else if (_byFullName.TryGetValue(
                         SwimmerFullNameKey(item.LastName, item.FirstName, item.BirthYear), out swimmer))
            {
                // Тот же человек, разрезанный на имя и фамилию иначе, — не новый пловец.
            }
            else
            {
                // Ивритский протокол напечатал имя ЛАТИНИЦЕЙ в основных полях, а EN-полей
                // у строки нет (6595: «Yael Fridland»), — ищем среди английских имён
                // карточек, как у ноги эстафеты. Без этого каждый переимпорт заводил
                // дубль рядом с פרידלנד יעל #7136 (склеивали 06.10 и снова 07.10).
                swimmer = swimmerKeyEn == null && IsLatinName(item.LastName, item.FirstName)
                    ? await FindByEnglishNameAsync(item.LastName, item.FirstName, item.BirthYear)
                    : null;

                // Not in pre-loaded cache — may still exist if duplicate key collision was silenced
                swimmer ??= await _db.Swimmers.FirstOrDefaultAsync(s =>
                    s.LastName == (item.LastName ?? "") &&
                    s.FirstName == (item.FirstName ?? "") &&
                    s.BirthYear == (item.BirthYear ?? 0));

                swimmer ??= FindBySpelling(_bySpelling, item.LastName, item.FirstName, item.BirthYear,
                    club, _diagnosticLog);
            }

            if (swimmer == null)
            {
                swimmer = new Swimmer
                {
                    LastName = item.LastName ?? string.Empty,
                    FirstName = item.FirstName ?? string.Empty,
                    BirthYear = item.BirthYear ?? 0
                };
                SetEnglishName(swimmer, item);
                _db.Swimmers.Add(swimmer);
                await _db.SaveChangesAsync();
                AddBySpelling(_bySpelling, swimmer);
            }

            _byKey[swimmerKey] = swimmer;
            _byFullName.TryAdd(
                SwimmerFullNameKey(swimmer.LastName, swimmer.FirstName, swimmer.BirthYear), swimmer);
        }

        // Страж тёзок: тот же пловец уже плыл в ЭТОМ соревновании за другой клуб —
        // значит это другой человек с тем же именем и годом рождения (инцидент И-11).
        // Сравниваем канонические клубы: у склеенных клубов остаётся «надгробие»
        // (MergedIntoId), и без канонизации один и тот же клуб выглядел бы как два.
        if (!isAnonymousSwimmer && item.IsRelay != true && club != null && swimmer.Id != 0)
        {
            var canonicalClubId = club.MergedIntoId ?? club.Id;
            var namesakeKey = (competitionId, swimmerKey, canonicalClubId);

            if (_namesakeByClub.TryGetValue(namesakeKey, out var namesake))
            {
                swimmer = namesake;
            }
            else if (_seenClubInCompetition.TryGetValue((competitionId, swimmer.Id), out var seenClub)
                     && seenClub != canonicalClubId)
            {
                swimmer = new Swimmer
                {
                    LastName = item.LastName ?? string.Empty,
                    FirstName = item.FirstName ?? string.Empty,
                    BirthYear = item.BirthYear ?? 0
                };
                SetEnglishName(swimmer, item);
                _db.Swimmers.Add(swimmer);
                await _db.SaveChangesAsync();
                _namesakeByClub[namesakeKey] = swimmer;
                _diagnosticLog.Add(
                    $"Тёзки: '{item.LastName} {item.FirstName}' ({item.BirthYear}) выступают за разные клубы "
                    + $"в одном соревновании — заведена отдельная запись #{swimmer.Id} для '{club.Name}'");
            }

            _seenClubInCompetition[(competitionId, swimmer.Id)] = canonicalClubId;
        }

        return (swimmer, swimmerKeyEn);
    }

    /// <summary>Дозаполнение пустых EN-имён у существующего пловца (двуязычный импорт).</summary>
    public void FillEnglishName(Swimmer swimmer, ResultJsonItem item, string? keyEn)
    {
        if (keyEn != null && string.IsNullOrWhiteSpace(swimmer.LastNameEn) && SetEnglishName(swimmer, item))
            _byKeyEn.TryAdd(keyEn, swimmer);
    }

    /// <summary>
    /// Пишет пловцу EN-имя строки — только если оно ЕГО (10.10.2026, docs/plans/en-names-handoff.md).
    /// Двуязычный импорт кладёт в EN-поля всё, что дала пара HE+EN, а она ошибается двумя
    /// способами: EN-фамилия потерялась, и вместо неё подставлен иврит («ברוטמן | BRUTMAN»), или
    /// EN-строка съехала на соседа («פוגל דודוביץ איתן» ↔ «ELKIN DANIEL», comp 1486). Оба случая
    /// узнаёт <see cref="EnglishNameFit"/>; тогда EN-поля остаются пустыми — витрина покажет
    /// иврит, а пустое поле дозаполнит следующий протокол или <c>--fix-en-names</c>. Пустое
    /// лучше чужого: чужое никто не перезапишет, импорт трогает только пустые.
    /// Ноль сравнимых имён (EN-протокол, анонимная нога) — пишем как есть.
    /// </summary>
    /// <returns>true — имя записано.</returns>
    private bool SetEnglishName(Swimmer swimmer, ResultJsonItem item)
    {
        var (lastEn, firstEn) = (item.LastNameEn ?? string.Empty, item.FirstNameEn ?? string.Empty);
        var (verdict, word, _) = EnglishNameFit.Assess(swimmer.LastName, swimmer.FirstName, lastEn, firstEn);
        if (verdict is EnglishNameVerdict.HebrewInLatin or EnglishNameVerdict.Foreign)
        {
            _diagnosticLog.Add(
                $"EN-имя «{lastEn} | {firstEn}» не записано пловцу '{swimmer.LastName} {swimmer.FirstName}' "
                + $"({swimmer.BirthYear}): {(verdict == EnglishNameVerdict.HebrewInLatin ? "иврит в латинском поле" : $"«{word}» не похоже ни на одно ивритское слово")}");
            swimmer.LastNameEn = string.Empty;
            swimmer.FirstNameEn = string.Empty;
            return false;
        }

        swimmer.LastNameEn = lastEn;
        swimmer.FirstNameEn = firstEn;
        return true;
    }

    /// <summary>Пол карточки, известной этому импорту (для пола эстафеты по составу, Э4).</summary>
    public string? GenderOf(int swimmerId) => _byKey.Values.FirstOrDefault(s => s.Id == swimmerId)?.Gender;

    /// <summary>
    /// Резолвит ноги эстафеты (леги из парсера) в <see cref="RelayMember"/>: каждое имя —
    /// в Swimmer тем же матчингом, что и обычный пловец (кэш → БД → заглушка). Анонимные
    /// леги (без имени) пропускаем — членство им не атрибутировать. Дубли SwimmerId внутри
    /// одной эстафеты схлопываем (уникальный индекс RelayId+SwimmerId).
    /// </summary>
    public async Task<List<RelayMember>> ResolveRelayMembersAsync(List<RelaySwimmerJson>? legs, Club? club)
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
            if (!_byKey.TryGetValue(key, out var swimmer))
            {
                swimmer = await _db.Swimmers.FirstOrDefaultAsync(s =>
                    s.LastName == (leg.LastName ?? "") &&
                    s.FirstName == (leg.FirstName ?? "") &&
                    s.BirthYear == (leg.BirthYear ?? 0));

                // Та же нога, разрезанная на имя и фамилию иначе (протокол гадает границу,
                // loglig берёт её из колонок) — не новый пловец, см. SwimmerFullNameKey.
                swimmer ??= await FindLegByFullNameAsync(leg);
                swimmer ??= await FindLegByEnglishNameAsync(leg);
                // Нога плывёт за клуб команды — тот же фоллбек написания, что у личной строки.
                swimmer ??= FindBySpelling(_bySpelling, leg.LastName, leg.FirstName, leg.BirthYear,
                    club, _diagnosticLog);

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
                    AddBySpelling(_bySpelling, swimmer);
                }
                _byKey[key] = swimmer;
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
    /// Нога, записанная с ДРУГОЙ границей между именем и фамилией: ищем по всем словам
    /// полного имени и году рождения (<see cref="SwimmerFullNameKey"/>). Неоднозначность
    /// (двое под одним ключом) — не матчим: лучше тень, чем чужие результаты в профиле.
    /// </summary>
    private async Task<Swimmer?> FindLegByFullNameAsync(RelaySwimmerJson leg)
    {
        var year = leg.BirthYear ?? 0;
        if (year == 0) return null;

        var key = SwimmerFullNameKey(leg.LastName, leg.FirstName, year);
        var sameYear = await _db.Swimmers.Where(s => s.BirthYear == year).ToListAsync();
        var hits = sameYear
            .Where(s => SwimmerFullNameKey(s.LastName, s.FirstName, s.BirthYear) == key)
            .ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    /// <summary>
    /// Запасной ключ ноги эстафеты: английское имя + год. Нужен, когда протокол пишет пловца
    /// латиницей не так, как лежит его карточка: у Shirli Ben Shoham в карточке «SHOHAM BEN»
    /// (латиница в ивритском поле, обратный порядок), а loglig печатает «BEN SHOHAM» — совпадает
    /// с её английским именем. Без запасного ключа каждый переимпорт заводил ей новую карточку
    /// (19.09.2026, comp 64). Регистр не важен; берём только ОДНОЗНАЧНОЕ совпадение — двое
    /// с тем же латинским именем и годом значит, что гадать нельзя, и заводится новая карточка.
    /// </summary>
    private Task<Swimmer?> FindLegByEnglishNameAsync(RelaySwimmerJson leg) =>
        FindByEnglishNameAsync(leg.LastName, leg.FirstName, leg.BirthYear);

    /// <summary>
    /// Карточка, чьё АНГЛИЙСКОЕ имя совпало с данным (регистр не важен) при том же годе.
    /// Только однозначное совпадение: двое — гадать нельзя, вызывающий заводит новую карточку.
    /// Общий для ноги эстафеты и личной строки, напечатанной латиницей.
    /// </summary>
    private async Task<Swimmer?> FindByEnglishNameAsync(string? lastName, string? firstName, int? birthYear)
    {
        var last = (lastName ?? "").Trim().ToUpperInvariant();
        var first = (firstName ?? "").Trim().ToUpperInvariant();
        if (last.Length == 0 || birthYear is not int year || year == 0) return null;

        var found = await _db.Swimmers
            .Where(s => s.BirthYear == year
                        && s.LastNameEn.ToUpper() == last
                        && s.FirstNameEn.ToUpper() == first)
            .Take(2)
            .ToListAsync();
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>Имя целиком латиницей: есть буквы, и все буквы — a–z / A–Z.</summary>
    private static bool IsLatinName(string? last, string? first)
    {
        var letters = $"{last}{first}".Where(char.IsLetter).ToList();
        return letters.Count > 0 && letters.All(char.IsAsciiLetter);
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
    /// Ключ написания: полное имя без «י», «ו», дефисов, апострофов и пробелов + год рождения.
    ///
    /// Зачем: иврит пишет одно и то же имя то полным письмом, то неполным (כתיב מלא/חסר), а
    /// протоколы ещё и ставят дефис в двойной фамилии через раз. «יוקליס עמליה» и
    /// «יוקלס עמליה» (#61745), «בן-בסט יובל» и «בן בסט יובל» (#60127) — одни и те же люди, но
    /// точный ключ у них разный, и каждый переимпорт заводил новую карточку (склеивали 06.10 и
    /// снова 07.10; решение Влада 07.10.2026 — Р79 в docs/data-integrity.md).
    ///
    /// Ключ слабый (у разных людей скелет может совпасть), поэтому сам по себе он не решает —
    /// см. <see cref="FindBySpelling"/>: нужны ещё тот же клуб и единственный кандидат.
    /// null — в имени нет иврита (латиница так не пишется) или нет года.
    /// </summary>
    internal static string? SwimmerSpellingKey(string? last, string? first, int? year)
    {
        var full = $"{last} {first}";
        if (year is not > 0 || !full.Any(c => c is >= 'א' and <= 'ת')) return null;

        var skeleton = new string(SwimmerDedupService.Normalize(full)
            .Where(c => c is not ('י' or 'ו' or '-' or '־' or ' ' or '\'' or '"'))
            .ToArray());
        return skeleton.Length == 0 ? null : $"{skeleton}|{year}";
    }

    private static void AddBySpelling(Dictionary<string, List<Swimmer>> index, Swimmer swimmer)
    {
        // Пловец группы (Р71) — не кандидат: его заводил тренер, и чужой протокол не должен
        // попадать в его карточку (тот же принцип, что в SwimmerDedupService).
        if (swimmer.PrivateHubGroupId != null) return;
        if (SwimmerSpellingKey(swimmer.LastName, swimmer.FirstName, swimmer.BirthYear) is not { } key) return;
        if (!index.TryGetValue(key, out var list)) index[key] = list = [];
        list.Add(swimmer);
    }

    /// <summary>
    /// Карточка того же человека, записанного другим написанием (<see cref="SwimmerSpellingKey"/>):
    /// тот же год, ТОТ ЖЕ КЛУБ (клуб строки или его канон после склейки — у карточки клуб
    /// последнего старта) и ровно один такой кандидат. Иначе null — вызывающий заводит новую
    /// карточку, а сомнительную пару покажет проверка дублей в Health, решать будет человек.
    /// </summary>
    private static Swimmer? FindBySpelling(
        Dictionary<string, List<Swimmer>> index, string? last, string? first, int? year,
        Club? club, List<string> diagnosticLog)
    {
        if (club is null || SwimmerSpellingKey(last, first, year) is not { } key) return null;
        if (!index.TryGetValue(key, out var candidates)) return null;

        var clubIds = new[] { club.Id, club.MergedIntoId ?? club.Id };
        var sameClub = candidates.Where(s => s.ClubId is int c && clubIds.Contains(c)).Take(2).ToList();
        if (sameClub.Count != 1) return null;

        var found = sameClub[0];
        diagnosticLog.Add(
            $"Другое написание: '{last} {first}' ({year}) узнан как #{found.Id} '{found.LastName} {found.FirstName}' "
            + $"(тот же год и клуб «{club.Name}»)");
        return found;
    }

    /// <summary>
    /// Ключ пловца, НЕ ЗАВИСЯЩИЙ от того, где прошла граница между именем и фамилией:
    /// фамилия и имя одной строкой без пробелов + год рождения.
    ///
    /// Зачем: границу знает не каждый источник. Основной протокол даёт ногу эстафеты плоской
    /// строкой «Фамилия… Имя Год» и вынужден гадать — <c>ParseRelaySwimmerLine</c> отдаёт имени
    /// ровно одно слово, поэтому «בניסים שרה רוז 2015» превращается в фамилию «בניסים שרה» и
    /// имя «רוז». У PDF промежуточных loglig имя и фамилия лежат в РАЗНЫХ КОЛОНКАХ, и тот же
    /// человек разрезан правильно. Обычный ключ «фамилия|имя|год» на этих двух написаниях
    /// разный, и импорт заводит пловца-тень рядом с настоящим: 20.09.2026 так появилось
    /// 50 дублей за один прогон (docs/data-integrity.md, И-28).
    ///
    /// ⚠ ПОРЯДОК СЛОВ ЗНАЧИМ: склеиваем «фамилия + имя» как есть, без пробелов. «שני עידו» и
    /// «עידו שני» остаются РАЗНЫМИ людьми — решение зафиксировано тестом
    /// <c>Import_NameSurnameSwap_NotMerged</c>: в иврите обе половины бывают и именем, и
    /// фамилией, и перестановка склеила бы двух разных детей. Сдвиг границы при этом ловится:
    /// «בניסים שרה»+«רוז» и «בניסים»+«שרה רוז» дают одну и ту же склейку.
    /// ⚠ Это ФОЛЛБЕК, а не основной ключ: сначала пробуем точное совпадение, и только если его
    /// нет — этот. Сторож тёзок по клубам (И-11) работает после и не отключается.
    /// </summary>
    private static string SwimmerFullNameKey(string? last, string? first, int? year)
    {
        var glued = $"{SwimmerDedupService.Normalize(last ?? "")}{SwimmerDedupService.Normalize(first ?? "")}"
            .Replace(" ", string.Empty);
        return $"{glued}|{year ?? 0}";
    }
}
