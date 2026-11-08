namespace Swimm.Infrastructure.Services;

/// <summary>
/// Правила «строка принадлежит пловцу» поверх ключевого матча (И-63, 07.11.2026).
/// </summary>
/// <remarks>
/// <para>
/// Ключ <see cref="ResultMatchKey"/> сознательно без пловца (заплыв+дорожка устойчивы, имя — нет),
/// но в нём есть пол. Живой случай — comp 1580 «ליגה מספר 5- ראשון הרצליה»: протокол печатает
/// девочек 14+ в секции «בנים 14», а у наших строк пол выровнен по карточке (female). Перезабор
/// читал девочку мужским полом, её ключ совпадал с ключом МАЛЬЧИКА того же заплыва и дорожки в
/// соседней секции, и единственный кандидат брался по FIFO: строка мальчика переписывалась
/// данными девочки, мальчик вставлялся заново, а женская строка девочки оставалась дублем.
/// </para>
/// <para>
/// Два правила, оба только для уже опознанных пловцов (SwimmerId &gt; 0 — анонимные ноги эстафет
/// получают новый Id на каждый импорт и сюда не попадают):
/// 1. <see cref="PairSamePersonAcrossGender"/> — у пловца есть его старая строка с тем же
///    заплывом/дорожкой/раундом/сессией, но другим полом, и пара однозначна → сопоставляем их до
///    ключевого матча; пол хранимый (человек выровнял), перезабор его не откатывает.
/// 2. <see cref="IsClaimedByAnother"/> — FIFO не берёт строку другого пловца, если тот сам есть в
///    файле под этим ключом.
/// </para>
/// </remarks>
public static partial class ResultMatcher
{
    /// <summary>Ключ без пола: «то же место в протоколе», пол бывает напечатан не тот.</summary>
    private static ResultMatchKey WithoutGender(ResultMatchKey key) => key with { Gender = string.Empty };

    private static (HashSet<int> TakenOld, HashSet<int> TakenNew) PairSamePersonAcrossGender<TOld, TNew>(
        ResultMatch<TOld, TNew> result,
        IReadOnlyList<TOld> oldRows,
        IReadOnlyList<TNew> newRows,
        Func<TOld, ResultMatchKey> oldKey,
        Func<TNew, ResultMatchKey> newKey,
        Func<TOld, ResultDiscriminator> oldDisc,
        Func<TNew, ResultDiscriminator> newDisc)
    {
        var takenOld = new HashSet<int>();
        var takenNew = new HashSet<int>();

        // Кандидаты — только личные строки опознанных пловцов; группа = место без пола + человек.
        static bool Eligible(ResultMatchKey k, ResultDiscriminator d) => !k.IsRelay && d.SwimmerId > 0;

        var oldGroups = Enumerable.Range(0, oldRows.Count)
            .Select(i => (i, k: oldKey(oldRows[i]), d: oldDisc(oldRows[i])))
            .Where(x => Eligible(x.k, x.d))
            .GroupBy(x => (Place: WithoutGender(x.k), x.d))
            .ToDictionary(g => g.Key, g => g.ToList());

        var newGroups = Enumerable.Range(0, newRows.Count)
            .Select(i => (i, k: newKey(newRows[i]), d: newDisc(newRows[i])))
            .Where(x => Eligible(x.k, x.d))
            .GroupBy(x => (Place: WithoutGender(x.k), x.d));

        foreach (var g in newGroups)
        {
            // Однозначность: одна новая и одна старая строка этого человека на этом месте, и пол
            // РАЗНЫЙ. Равный пол — обычный случай, его решает ключевой матч, как и раньше.
            if (g.Count() != 1 || !oldGroups.TryGetValue(g.Key, out var olds) || olds.Count != 1) continue;
            var (ni, nk, _) = g.Single();
            var (oi, ok, _) = olds[0];
            if (nk.Gender == ok.Gender) continue;

            takenOld.Add(oi);
            takenNew.Add(ni);
            result.Matched.Add((oldRows[oi], newRows[ni]));
            result.CrossGender.Add((oldRows[oi], newRows[ni]));
        }

        return (takenOld, takenNew);
    }

    /// <summary>Какие опознанные пловцы есть в файле под каждым ключом (кроме уже сматченных предпроходом).</summary>
    private static Dictionary<ResultMatchKey, HashSet<int>> SwimmersClaimingKeys<TNew>(
        IReadOnlyList<TNew> newRows,
        HashSet<int> takenNew,
        Func<TNew, ResultMatchKey> newKey,
        Func<TNew, ResultDiscriminator> newDisc)
    {
        var claimed = new Dictionary<ResultMatchKey, HashSet<int>>();
        for (var i = 0; i < newRows.Count; i++)
        {
            if (takenNew.Contains(i)) continue;
            var swimmerId = newDisc(newRows[i]).SwimmerId;
            if (swimmerId <= 0) continue;
            var key = newKey(newRows[i]);
            if (!claimed.TryGetValue(key, out var set)) claimed[key] = set = [];
            set.Add(swimmerId);
        }
        return claimed;
    }

    /// <summary>Старая строка принадлежит другому опознанному пловцу, который сам есть в файле под этим ключом.</summary>
    private static bool IsClaimedByAnother(
        Dictionary<ResultMatchKey, HashSet<int>> claimed, ResultMatchKey key, int oldSwimmerId, int newSwimmerId) =>
        oldSwimmerId > 0 && oldSwimmerId != newSwimmerId
        && claimed.TryGetValue(key, out var set) && set.Contains(oldSwimmerId);
}
