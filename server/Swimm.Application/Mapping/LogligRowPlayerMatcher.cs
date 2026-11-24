using System;
using System.Collections.Generic;
using System.Linq;

namespace Swimm.Application.Mapping;

/// <summary>Личная строка страницы дисциплины loglig с игроком, которому она принадлежит.</summary>
/// <param name="Date">Дата дня «dd/MM/yyyy»; пусто — страница дату не напечатала.</param>
/// <param name="TimeMs">null — без времени (DNS, DQ, NS…).</param>
public sealed record RowPlayerFromSource(
    string Date, string StyleName, string Distance, string Gender,
    int Heat, int Lane, int? TimeMs, int PlayerId);

/// <summary>Наша личная строка тех же дней.</summary>
public sealed record RowPlayerInDb(
    long ResultId, string Date, string StyleName, string Distance, string Gender,
    int Heat, int Lane, int? TimeMs);

/// <summary>Итог сопоставления: кому что писать и сколько строк источника осталось без пары.</summary>
/// <param name="Matches">Наша строка → игрок loglig; только однозначные пары.</param>
/// <param name="Ambiguous">Строк источника, чей ключ не единственный у источника или у нас.</param>
/// <param name="SourceUnmatched">Строк источника, которым нашей строки не нашлось.</param>
public sealed record RowPlayerPlan(
    IReadOnlyDictionary<long, int> Matches, int Ambiguous, int SourceUnmatched);

/// <summary>
/// Чья это строка: строка страницы дисциплины loglig → наша строка результата (Р122, И-69).
///
/// Имя и год рождения в ключ НЕ входят нарочно: проверка ищет как раз строки, которые лежат на
/// чужой карточке, и сравнение по имени спрятало бы находку. Строку опознаёт то, что напечатано в
/// обоих протоколах независимо от человека:
/// <list type="number">
/// <item><b>со временем</b> — день + дисциплина + заплыв + дорожка + время;</item>
/// <item><b>без времени</b> (DNS, DQ, NS: времени нет хотя бы на одной стороне) — день +
///   дисциплина + пол + заплыв + дорожка, среди строк, не сведённых первым шагом. Пол здесь нужен:
///   заплыв 1 дорожка 4 есть и у девочек, и у мальчиков, а времени, чтобы их различить, нет.</item>
/// </list>
/// Пара пишется, только если ключ единственный И у источника, И у нас. У чемпионатов loglig заводит
/// событие на каждый возраст и нумерует заплывы в каждом с 1 — без времени такие строки
/// неразличимы и остаются без id: лучше пусто, чем чужой игрок.
///
/// Чистая функция: две плоские выборки на входе, план на выходе. Ни сети, ни БД.
/// </summary>
public static class LogligRowPlayerMatcher
{
    private const string AnyDay = "*";

    /// <param name="allowUntimed">
    /// false — шаг «без времени» не выполняется. Так надо, когда прочитаны НЕ ВСЕ страницы
    /// соревнования: строка без времени опознаётся только заплывом и дорожкой, а её двойник из
    /// непрочитанной дисциплины другого возраста (те же заплыв 1, дорожка 4) остался бы невидим —
    /// и id чужого игрока лёг бы на чужую DNS-строку. Со временем такой риск закрывает само время.
    /// </param>
    public static RowPlayerPlan Build(
        IReadOnlyList<RowPlayerFromSource> source, IReadOnlyList<RowPlayerInDb> db, bool allowUntimed = true)
    {
        var matches = new Dictionary<long, int>();
        var contested = new HashSet<long>();
        int ambiguous = 0, unmatched = 0;

        // Сводит строки источника с нашими свободными строками по ключу; возвращает строки источника,
        // которым под ключом не нашлось НИ ОДНОЙ нашей (им ещё может помочь следующий шаг).
        List<RowPlayerFromSource> Step(IEnumerable<RowPlayerFromSource> src, bool withTime)
        {
            string Key(string date, string style, string distance, string gender, int heat, int lane, int? time) =>
                withTime
                    ? $"{date}|{style.ToLowerInvariant()}|{distance.ToLowerInvariant()}|h{heat}|l{lane}|t{time}"
                    : $"{date}|{style.ToLowerInvariant()}|{distance.ToLowerInvariant()}|{gender}|h{heat}|l{lane}";

            var free = db.Where(d => !matches.ContainsKey(d.ResultId) && !contested.Contains(d.ResultId)
                                     && (!withTime || d.TimeMs is not null)).ToList();
            var dated = free.ToLookup(d => Key(d.Date, d.StyleName, d.Distance, d.Gender, d.Heat, d.Lane, d.TimeMs));
            // Страница не напечатала дату — ищем среди наших строк без учёта дня.
            var anyDay = free.ToLookup(d => Key(AnyDay, d.StyleName, d.Distance, d.Gender, d.Heat, d.Lane, d.TimeMs));

            var rest = new List<RowPlayerFromSource>();
            foreach (var group in src.GroupBy(s =>
                         Key(s.Date.Length > 0 ? s.Date : AnyDay, s.StyleName, s.Distance, s.Gender, s.Heat, s.Lane, s.TimeMs)))
            {
                var first = group.First();
                var ours = (first.Date.Length > 0 ? dated : anyDay)[group.Key].ToList();
                if (ours.Count == 0) { rest.AddRange(group); continue; }
                if (ours.Count > 1 || group.Count() > 1) { ambiguous += group.Count(); continue; }

                // Без времени сводим, только если его нет хотя бы на одной стороне: «оба со
                // временем, но время разное» — расхождение протоколов, а не доказательство.
                if (!withTime && first.TimeMs is not null && ours[0].TimeMs is not null) { unmatched++; continue; }

                var id = ours[0].ResultId;
                if (matches.Remove(id))
                {
                    // Одну нашу строку взяли два ключа (строка источника с датой и без неё):
                    // не доказана ни одна — и прежняя пара, и эта.
                    contested.Add(id);
                    ambiguous += 2;
                    continue;
                }
                if (contested.Contains(id)) { ambiguous++; continue; }
                matches[id] = first.PlayerId;
            }
            return rest;
        }

        var afterTimed = Step(source.Where(s => s.TimeMs is not null), withTime: true);
        var untimed = source.Where(s => s.TimeMs is null).Concat(afterTimed).ToList();
        var left = allowUntimed ? Step(untimed, withTime: false) : untimed;

        return new RowPlayerPlan(matches, ambiguous, unmatched + left.Count);
    }
}

/// <summary>Слова и коды шага «игрок loglig у строки» (Р122): настройка, аудит, актор, потолок.</summary>
public static class LogligRowPlayerRules
{
    /// <summary>Тумблер шага после импорта (/Admin/Settings).</summary>
    public const string SettingKey = "LogligRowPlayersOnImport";

    /// <summary>Действие в журнале аудита — одна строка на прогон соревнования.</summary>
    public const string AuditAction = "competition.loglig-row-players";

    /// <summary>Актор автозапуска после импорта.</summary>
    public const string ActorName = "auto:import";

    /// <summary>
    /// Сколько прогон после импорта ЧИТАЕТ страницы. Страница — около 2 с (пауза провайдера;
    /// замер 24.11.2026: 21 запрос за 40 с — docs/data-integrity.md, Р122): лига в 20 дисциплин —
    /// ~40 с, чемпионат в 150–250 — 5–8,5 мин; бюджета хватает примерно на 600 страниц. По истечении чтение останавливается, а всё
    /// прочитанное сопоставляется и записывается — работа не теряется; остаток добирает CLI
    /// (у него бюджета нет).
    /// </summary>
    public static readonly TimeSpan ReadBudget = TimeSpan.FromMinutes(20);

    /// <summary>Жёсткий потолок прогона после импорта: бюджет чтения + запас на последнюю страницу и запись.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(25);
}
