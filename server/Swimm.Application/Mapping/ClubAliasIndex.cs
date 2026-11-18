namespace Swimm.Application.Mapping;

/// <summary>Клуб справочника для индекса старых названий.</summary>
public sealed record ClubAliasClub(int Id, string Name, string NameEn, int? MergedIntoId, bool IsPseudo);

/// <summary>Прежнее название клуба (строка <c>ClubNames</c>).</summary>
public sealed record ClubAliasName(int Id, int ClubId, string Name, string NameEn, string Kind);

/// <summary>
/// Попадание по старому названию: итоговый живой клуб и — если название было ПРЕЖНИМ
/// (переименование) — строка <c>ClubNames</c>, которую ставят результату в <c>ClubNameId</c>.
/// </summary>
public sealed record ClubAliasHit(int ClubId, int? ClubNameId);

/// <summary>
/// Импорт узнаёт старые названия клуба (docs/plans/swimmer-club-change-plan.md §5, Н5).
///
/// Живые клубы импорт ищет сам (точная пара Name|NameEn, затем одно Name). Этот индекс —
/// следующий шаг, ДО заведения нового клуба: название из протокола могло принадлежать
/// <list type="number">
/// <item>прежнему названию живого клуба (<c>ClubNames</c>) — перезалив протокола 2024/25 с
/// «הפועל עירוני הוד השרון» попадёт в Aluma и запомнит напечатанное название;</item>
/// <item>склеенному клубу-дублю (надгробию) — тогда результат уходит в его итоговый канон.</item>
/// </list>
/// Без этого любая склейка «откатывалась» следующим импортом старого протокола: склеенные из
/// кэша клубов исключены, и импорт заводил клуб с прежним названием заново.
///
/// Совпадение — по ивритскому названию; если в протоколе его нет — по английскому. Цепочки
/// склеек проходятся до живого клуба; псевдоклуб (сборная) попаданием не считается.
/// </summary>
public sealed class ClubAliasIndex
{
    private readonly Dictionary<string, ClubAliasHit> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClubAliasHit> _byNameEn = new(StringComparer.OrdinalIgnoreCase);

    public ClubAliasIndex(IEnumerable<ClubAliasClub> clubs, IEnumerable<ClubAliasName> names)
    {
        var clubList = clubs.ToList();
        var mergedInto = clubList.ToDictionary(c => c.Id, c => c.MergedIntoId);
        var pseudo = clubList.Where(c => c.IsPseudo).Select(c => c.Id).ToHashSet();

        int? Live(int id) =>
            ClubMergeChains.FinalOf(id, mergedInto) is int final && !pseudo.Contains(final) ? final : null;

        // 1) Прежние названия — первыми: они знают и клуб, и что название было другим.
        foreach (var n in names.OrderBy(n => n.Id))
        {
            if (Live(n.ClubId) is not int club) continue;
            var hit = new ClubAliasHit(club, n.Kind == Domain.Entities.ClubNameKinds.Former ? n.Id : null);
            Add(_byName, n.Name, hit);
            Add(_byNameEn, n.NameEn, hit);
        }

        // 2) Надгробия склеенных клубов: их названия ведут в итоговый канон.
        foreach (var c in clubList.Where(c => c.MergedIntoId != null).OrderBy(c => c.Id))
        {
            if (Live(c.Id) is not int club) continue;
            var hit = new ClubAliasHit(club, null);
            Add(_byName, c.Name, hit);
            Add(_byNameEn, c.NameEn, hit);
        }
    }

    /// <summary>Клуб по старому названию из протокола. null — такого названия у нас не было.</summary>
    public ClubAliasHit? Find(string? name, string? nameEn)
    {
        var he = (name ?? string.Empty).Trim();
        if (he.Length > 0) return _byName.GetValueOrDefault(he);
        var en = (nameEn ?? string.Empty).Trim();
        return en.Length > 0 ? _byNameEn.GetValueOrDefault(en) : null;
    }

    /// <summary>Первый по порядку источник выигрывает: прежнее название — у надгробия того же имени.</summary>
    private static void Add(Dictionary<string, ClubAliasHit> index, string? key, ClubAliasHit hit)
    {
        var k = (key ?? string.Empty).Trim();
        if (k.Length > 0) index.TryAdd(k, hit);
    }
}
