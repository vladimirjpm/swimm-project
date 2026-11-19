using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>Вывод по паре клубов-кандидатов (Р113). Строки — для JSON и CSS страницы.</summary>
public static class ClubPairVerdicts
{
    /// <summary>Скорее один клуб двумя названиями — склейка «Дубль».</summary>
    public const string OneClub = "one";
    /// <summary>Скорее клуб сменил название — склейка «Переименование».</summary>
    public const string Rename = "rename";
    /// <summary>Скорее разные клубы — не склеивать, скрыть пару «не дубли».</summary>
    public const string TwoClubs = "two";
    /// <summary>Данных мало — решать руками.</summary>
    public const string Unclear = "unclear";
}

/// <summary>
/// Итоговая рекомендация по паре клубов-кандидатов в дубли (Р113, /Admin/Clubs): «скорее один
/// клуб», «скорее переименование», «скорее разные» или «не ясно». Влад 19.11.2026: «допиши
/// рекомендацию. скорее 1 клуб, или скорее разные. или ещё что». Чистая функция над тем, что уже
/// посчитано: протоколы (<see cref="ClubPairOverlapRule"/>), loglig (<see cref="ClubPairLogligRule"/>),
/// эвристика. Порядок — от сильного довода к слабому, первый сработавший решает:
///
/// | довод                                                        | вывод          |
/// |--------------------------------------------------------------|----------------|
/// | один пловец записан за оба клуба в одном соревновании         | один клуб      |
/// | оба в одних протоколах отдельными командами                   | разные         |
/// | loglig видит общих пловцов в обоих клубах                    | разные         |
/// | периоды в протоколах не пересекаются                          | переименование |
/// | «уверенная» эвристика (то же имя, мусорный хвост, NameEn)     | один клуб      |
/// | иначе                                                        | не ясно        |
///
/// Почему протокол сильнее loglig: протокол — сама федерация в момент старта (две строки зачёта —
/// две команды), loglig — нынешняя регистрация пловца, а дети переходят.
/// </summary>
public static class ClubPairRecommendationRule
{
    public static ClubPairRecommendation Decide(ClubDedupCandidate c)
    {
        var p = c.Protocols ?? ClubPairOverlap.None;
        var l = c.Loglig;

        if (p.SameSwimmerMeets > 0)
            return new(ClubPairVerdicts.OneClub,
                $"один и тот же пловец записан за оба клуба в одном соревновании ({p.SameSwimmerMeets}) — протокол пишет клуб двумя названиями");

        if (p.Meets > 0)
            return new(ClubPairVerdicts.TwoClubs,
                $"в одних протоколах отдельными командами: соревнований {p.Meets}"
                + (p.SeparateStandings > 0 ? $", свой клубный зачёт в {p.SeparateStandings}" : "")
                + (l is { ClubA: > 0, ClubB: > 0 } ? $"; loglig видит детей в обоих ({l.ClubA} / {l.ClubB})" : "")
                + " — общие пловцы скорее переходили; не склеивать, скрыть «не дубли»");

        if (l is { ClubA: > 0, ClubB: > 0 })
            return new(ClubPairVerdicts.TwoClubs,
                $"loglig видит общих пловцов в обоих клубах ({l.ClubA} / {l.ClubB}) — это два зарегистрированных клуба");

        if (p.SpansApart && p.SpanA is { } a && p.SpanB is { } b)
        {
            var dupOlder = b.To < a.From;
            return new(ClubPairVerdicts.Rename,
                $"вместе в протоколах не встречаются, периоды не пересекаются (канон {Span(a)}, дубль {Span(b)}) — похоже, клуб сменил название"
                + (dupOlder ? "; вид «Переименование»" : "; вид «Переименование», но дубль — более новое название: поменяй канон и дубль местами"));
        }

        if (c.Sure)
            return new(ClubPairVerdicts.OneClub, "одно название в разной записи, вместе в протоколах не встречаются");

        return new(ClubPairVerdicts.Unclear,
            "вместе в протоколах не встречаются, периоды пересекаются" + (l is null ? "" : ", loglig не различает")
            + " — открой оба клуба и реши руками");
    }

    private static string Span(ClubSpan s) => $"{s.From:MM.yyyy}–{s.To:MM.yyyy}";
}
