using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Ключ физической идентичности результата (докс: import-upsert-plan.md, Р2).
/// Заплыв+дорожка не меняются при перевыпуске протокола; SwimmerId сознательно не входит
/// в ключ (анонимные relay-леги, опечатки в имени пловца).
/// </summary>
public readonly record struct ResultMatchKey(
    int CompetitionId,
    int StyleId,
    string Distance,
    string Gender,
    int Heat,
    int Lane,
    bool IsRelay,
    /// <summary>
    /// Раунд зачёта (<c>timed-final</c>/<c>final</c>/<c>prelim</c>), пустая строка — источник
    /// раундов не различает. Без него утренний и вечерний заплывы одного пловца в чемпионате
    /// «мокдамот и финал» имеют ОДИН ключ, и переимпорт схлопывает их в одну строку —
    /// вторая молча затирает первую (И13, docs/data-integrity.md §10). У старых данных поле
    /// пустое у обеих сторон, поэтому ключи не меняются и переимпорт не «поедет».
    /// </summary>
    string Round);

/// <summary>
/// Вторичный дискриминатор — им разводятся строки ВНУТРИ коллизии ключа.
///
/// SwimmerId устойчив между переимпортами для уже именованной строки, но одного его мало:
/// у клуба бывает две команды в одной дисциплине, и один и тот же пловец плывёт в обеих
/// (comp #1513: «הפועל עמק חפר» 3037 и 3089, оба heat 3 lane 9, пловец 8238 в обеих) —
/// тогда SwimmerId совпадает у ОБЕИХ строк и матч сваливался в FIFO, то есть в лотерею.
/// Состав команды — то, чем эти строки различаются в самом протоколе.
///
/// HeatType (prelim/final/extra) добавлен 2026-08-23: у одного пловца бывают две строки с
/// ОДИНАКОВЫМИ заплывом и дорожкой, различающиеся только сессией — предварительные и финал
/// случайно совпали номерами (comp 1556-1559), либо после финала плыли призовой заплыв
/// (comp 1586: final + extra). SwimmerId у них один, TeamKey пуст, и матч сваливался в FIFO,
/// то есть в лотерею: время финала могло записаться в строку предварительных.
///
/// ⚠ Именно ДИСКРИМИНАТОР, а не ключ: он работает только внутри коллизии. У старых данных
/// HeatType пуст с обеих сторон — пары не совпадут по нему и доматчатся FIFO, ровно как
/// раньше. Внеси HeatType в ключ — и при первом же переимпорте старого протокола новым
/// парсером (в файле сессия есть, в БД пусто) строки перестали бы находиться и задвоились.
/// </summary>
public readonly record struct ResultDiscriminator(int SwimmerId, string TeamKey, string HeatType = "");

/// <summary>
/// Результат матчинга старых (уже в БД) и новых (из импортируемого файла) строк результата.
/// </summary>
public sealed class ResultMatch<TOld, TNew>
{
    public List<(TOld Old, TNew New)> Matched { get; } = [];
    public List<TNew> Inserted { get; } = [];
    public List<TOld> Deleted { get; } = [];

    /// <summary>
    /// Подмножество <see cref="Matched"/>: тот же пловец, тот же заплыв и дорожка, но пол строки
    /// в базе другой (И-63). Пол у таких пар хранимый — его выровнял человек, перезабор его не
    /// откатывает (см. <c>ResultMatcher.SamePerson.cs</c>).
    /// </summary>
    public List<(TOld Old, TNew New)> CrossGender { get; } = [];
}

/// <summary>
/// Чистая функция матчинга результатов при переимпорте (upsert). Без доступа к БД — принимает
/// уже загруженные old/new строки и функции извлечения ключа, возвращает matched/insert/delete.
/// </summary>
/// <remarks>
/// <para>
/// Коллизии ключа (несколько строк с одинаковым ключом) в реальных протоколах ЕСТЬ: строки-ноги
/// эстафет (leg rows — раздельные Result-строки на каждого пловца команды, RelayId у них NULL,
/// восстановлены парсером отдельно от строки-сводки команды) делят Heat/Lane/Distance/Style/Gender
/// на все ноги одной команды — инцидент 2026-07-20 (Маккабиада): 294 группы с коллизией ключа,
/// например (4X100, male, heat 1, lane 5, relay=false) — 5 строк в одной группе.
/// </para>
/// <para>
/// До фикса коллизии разрешались чистым FIFO по порядку следования. Это ломалось, когда фикс
/// парсера менял состав строк между переимпортами одного протокола (леги переставлялись/
/// добавлялись/убирались) — порядковый номер переставал соответствовать той же ноге, и матчер
/// вставлял новые строки вместо апдейта существующих (161 строка "задвоилась" при DeleteMissing=
/// false → 1076 вместо 915).
/// </para>
/// <para>
/// Разведка реальных данных (Competition 1483-1485, comp_id=1485 heat=1 lane=5): семь строк одной
/// группы коллизии различаются по SwimmerId почти во всех случаях (шесть разных ID) — совпадение
/// SwimmerId нашлось только у одной ноги, представленной дважды (вероятно, артефакт разбора PDF).
/// SwimmerId — единственное поле, устойчивое между переимпортами ОДНОГО и того же протокола для
/// уже опознанной (именованной) ноги: порядок следования в файле, Position и TimeSplit — не
/// устойчивы (парсер меняет раскладку, диск/финиш пересчитывают позиции). Но SwimmerId в ключ
/// целиком (Р2) внести нельзя: анонимная нога получает новый одноразовый Swimmer на КАЖДЫЙ импорт
/// (см. isAnonymousSwimmer в JsonImportService) — её SwimmerId никогда не совпадёт со старой
/// строкой, и обычный переход анонимной ноги в именованную (Р5, «правильно и желаемо») перестал бы
/// матчиться вовсе.
/// </para>
/// <para>
/// Решение — комбинация, а не замена ключа: внутри группы с коллизией (больше одной строки на
/// сторону) сначала матчим пары с РАВНЫМ SwimmerId (устойчиво для уже именованных ног — правка
/// опечатки в имени пловца не меняет SwimmerId, поэтому обычные одиночные результаты, где
/// коллизии нет, эта логика вообще не задевает); всё, что не срослось по SwimmerId (анонимные
/// ноги — их ID никогда не совпадут, либо пары с реально другим составом) — доматчивается, как и
/// раньше, по порядку следования (FIFO) среди оставшихся. Анонимная-в-именованную нога по-прежнему
/// матчится через FIFO, если весь состав группы переходит анонимный→именованный разом (обычный
/// случай); частичная замена внутри группы теперь не путает уже опознанные ноги друг с другом.
/// </para>
/// </remarks>
public static partial class ResultMatcher
{
    public static ResultMatch<TOld, TNew> Match<TOld, TNew>(
        IReadOnlyList<TOld> oldRows,
        IReadOnlyList<TNew> newRows,
        Func<TOld, ResultMatchKey> oldKeySelector,
        Func<TNew, ResultMatchKey> newKeySelector,
        Func<TOld, ResultDiscriminator> oldDiscriminatorSelector,
        Func<TNew, ResultDiscriminator> newDiscriminatorSelector)
    {
        var result = new ResultMatch<TOld, TNew>();

        // Предпроход (И-63): тот же пловец под другим полом — до ключевого матча, иначе строка
        // ушла бы в чужой ключ. Сматченные исключаются из всего, что ниже.
        var (takenOld, takenNew) = PairSamePersonAcrossGender(result, oldRows, newRows,
            oldKeySelector, newKeySelector, oldDiscriminatorSelector, newDiscriminatorSelector);
        var claimed = SwimmersClaimingKeys(newRows, takenNew, newKeySelector, newDiscriminatorSelector);

        // Старые строки, сгруппированные по ключу, в порядке следования — плюс параллельный
        // массив "занята ли позиция i уже матчем" на группу (для FIFO-доматча после SwimmerId-прохода).
        var oldByKey = new Dictionary<ResultMatchKey, List<TOld>>();
        for (var oi = 0; oi < oldRows.Count; oi++)
        {
            if (takenOld.Contains(oi)) continue;
            var old = oldRows[oi];
            var key = oldKeySelector(old);
            if (!oldByKey.TryGetValue(key, out var list))
            {
                list = [];
                oldByKey[key] = list;
            }
            list.Add(old);
        }
        var consumed = new Dictionary<ResultMatchKey, bool[]>();
        foreach (var (key, list) in oldByKey)
            consumed[key] = new bool[list.Count];

        for (var ni = 0; ni < newRows.Count; ni++)
        {
            if (takenNew.Contains(ni)) continue;
            var @new = newRows[ni];
            var key = newKeySelector(@new);
            var matched = false;

            if (oldByKey.TryGetValue(key, out var bucket))
            {
                var flags = consumed[key];

                // Коллизия (больше одной старой строки на этот ключ) — сначала пробуем матч по
                // SwimmerId, устойчивый для уже опознанных (именованных) ног между переимпортами.
                // Для обычных одиночных результатов (bucket.Count == 1) эта ветка не участвует —
                // поведение при отсутствии коллизии не меняется.
                if (bucket.Count > 1)
                {
                    var newDiscriminator = newDiscriminatorSelector(@new);
                    for (var i = 0; i < bucket.Count; i++)
                    {
                        if (!flags[i] && oldDiscriminatorSelector(bucket[i]) == newDiscriminator)
                        {
                            result.Matched.Add((bucket[i], @new));
                            flags[i] = true;
                            matched = true;
                            break;
                        }
                    }
                }

                // Доматч по порядку следования (FIFO) — как раньше, для всего, что не срослось
                // по SwimmerId (анонимные ноги, либо обычный единственный кандидат в группе).
                if (!matched)
                {
                    var newSwimmer = newDiscriminatorSelector(@new).SwimmerId;
                    for (var i = 0; i < bucket.Count; i++)
                    {
                        // Строку другого пловца не забираем, если он сам есть в файле под этим
                        // ключом (И-63): она — его, а не «свободная позиция».
                        if (!flags[i] && !IsClaimedByAnother(claimed, key,
                                oldDiscriminatorSelector(bucket[i]).SwimmerId, newSwimmer))
                        {
                            result.Matched.Add((bucket[i], @new));
                            flags[i] = true;
                            matched = true;
                            break;
                        }
                    }
                }
            }

            if (!matched)
                result.Inserted.Add(@new);
        }

        foreach (var (key, list) in oldByKey)
        {
            var flags = consumed[key];
            for (var i = 0; i < list.Count; i++)
                if (!flags[i])
                    result.Deleted.Add(list[i]);
        }

        return result;
    }

    /// <summary>Ключ для строки результата, уже сохранённой в БД (RelayId — надёжный источник IsRelay).</summary>
    public static ResultMatchKey KeyOfPersisted(ResultRecord r) =>
        new(r.CompetitionId, r.StyleId, r.Distance, KeyGender(r.Gender, r.RelayId != null), r.Heat, r.Lane,
            r.RelayId != null, r.Round ?? string.Empty);

    /// <summary>Ключ для ещё не сохранённой строки результата (Relay — навигация, RelayId ещё не проставлен).</summary>
    public static ResultMatchKey KeyOfTransient(ResultRecord r) =>
        new(r.CompetitionId, r.StyleId, r.Distance, KeyGender(r.Gender, r.Relay != null || r.RelayId != null),
            r.Heat, r.Lane, r.Relay != null || r.RelayId != null, r.Round ?? string.Empty);

    /// <summary>
    /// Пол в ключе: у эстафеты «none» (неизвестен) и «mixed» (смешанная) — одно значение. Строку
    /// в базе могли перевести в mixed (Э4, по полу участников), а парсер переимпорта снова
    /// отдаёт none — без этого переимпорт не узнал бы свою строку и плодил бы дубль.
    /// </summary>
    public static string KeyGender(string? gender, bool isRelay) =>
        isRelay && gender is null or "" or "none" or "mixed" ? "none|mixed" : gender ?? string.Empty;

    /// <summary>Дискриминатор старой строки — Relay подгружен через Include (см. JsonImportService).</summary>
    public static ResultDiscriminator DiscriminatorOfPersisted(ResultRecord r) =>
        new(r.SwimmerId, TeamKey(r.Relay), r.HeatType ?? string.Empty);

    /// <summary>Дискриминатор новой строки — Relay ещё навигация, Id у неё нет.</summary>
    public static ResultDiscriminator DiscriminatorOfTransient(ResultRecord r) =>
        new(r.SwimmerId, TeamKey(r.Relay), r.HeatType ?? string.Empty);

    /// <summary>
    /// Состав команды как строка. Берём именно состав, а не TeamName: у двух команд одного
    /// клуба название одинаковое, различаются они только людьми. Если состава нет (парсер не
    /// разобрал) — ключ пустой, и строка доматчивается по FIFO, как было до этого.
    /// </summary>
    /// <remarks>
    /// Берём текст состава из файла (<c>relay_swimmers_name</c>), а НЕ структурный
    /// <c>Relay.Members</c>: у новой строки членство ещё не сохранено, и у только что
    /// заведённого пловца SwimmerId = 0 — ключ по членам склеил бы все команды в одну.
    /// </remarks>
    private static string TeamKey(Relay? relay) =>
        relay is null
            ? string.Empty
            : (relay.SwimmersName ?? relay.TeamName ?? string.Empty).Trim();
}
