using Swimm.Application.Abstractions;

namespace Swimm.Application.Mapping;

/// <summary>Чем кончилась доклейка промежуточных (у дня или у всего прогона).</summary>
public enum SplitAutoKind
{
    /// <summary>Записано хоть что-то: личные строки или ноги эстафет.</summary>
    Attached,
    /// <summary>У loglig промежуточных нет: в источнике ни команд, ни личных заплывов.</summary>
    NothingOnLoglig,
    /// <summary>В источнике есть, но писать нечего: уже стоят или не сопоставились.</summary>
    NothingNew,
    /// <summary>Не удалось: нет LogligId, сайт лёг, таймаут — повторять кнопкой.</summary>
    Failed,
}

/// <summary>
/// Правила автодоклейки промежуточных после импорта (решение Влада 23.11.2026): тумблер,
/// классификация исхода, подписи шага 9 чек-листа и текст аудита. Чистые функции — тесты
/// <c>SplitAutoAttachRulesTests</c>.
/// </summary>
public static class SplitAutoAttachRules
{
    /// <summary>Тумблер в /Admin/Settings — по образцу <c>LogligStampOnImport</c>, по умолчанию включён.</summary>
    public const string SettingKey = "SplitAttachOnImport";

    /// <summary>Код аудита — тот же, что у кнопки «Промежуточные»: история доклеек одна.</summary>
    public const string AuditAction = "competition.attach-splits";

    /// <summary>
    /// Актор автозапуска в аудите (кнопка пишет email админа, CLI — «cli»). По нему шаг 9 и
    /// страница аудита отличают «само после импорта» от ручного прогона.
    /// </summary>
    public const string ActorName = "auto:import";

    /// <summary>Признак автозапуска в DetailsJson аудита.</summary>
    public const string Trigger = "import";

    /// <summary>
    /// Потолок одной доклейки. У чемпионата источник — 115 запросов (~2.5 мин); с запасом на
    /// медленный loglig. Дольше — прерываем: следующий импорт пачки не должен ждать вечно.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    /// <summary>Исход одного прогона <see cref="ISplitAttachService.AttachAsync"/>.</summary>
    public static SplitAutoKind Classify(SplitAttachResult r)
    {
        if (!r.Applied) return SplitAutoKind.Failed;
        if (r.Report.LegWrites > 0 || r.Report.SwimWrites > 0) return SplitAutoKind.Attached;
        // Ничего не записали, а часть источника не прочиталась (504 loglig) — это «не знаем»,
        // а не «промежуточных нет»: повторить кнопкой (живой случай compID 16917, 23.11.2026).
        if (r.SourceIncomplete) return SplitAutoKind.Failed;
        return r.Report.Teams == 0 && r.Report.Swims == 0 ? SplitAutoKind.NothingOnLoglig : SplitAutoKind.NothingNew;
    }

    /// <summary>
    /// Исход дня: записанное у дня — «доклеено»; иначе исход прогона, но «доклеено» у соседнего дня
    /// для этого дня значит «нового нет».
    /// </summary>
    public static SplitAutoKind DayKind(SplitAutoKind run, int dayWrites) =>
        dayWrites > 0 ? SplitAutoKind.Attached
        : run == SplitAutoKind.Attached ? SplitAutoKind.NothingNew
        : run;

    /// <summary>Исход нескольких прогонов (у compID бывает больше одного события): сбой важнее всего.</summary>
    public static SplitAutoKind Combine(IEnumerable<SplitAutoKind> kinds)
    {
        var list = kinds.ToList();
        if (list.Count == 0 || list.Contains(SplitAutoKind.Failed)) return SplitAutoKind.Failed;
        if (list.Contains(SplitAutoKind.Attached)) return SplitAutoKind.Attached;
        return list.Contains(SplitAutoKind.NothingNew) ? SplitAutoKind.NothingNew : SplitAutoKind.NothingOnLoglig;
    }

    /// <summary>Код исхода в DetailsJson аудита и в ответе статуса задачи.</summary>
    public static string Code(SplitAutoKind kind) => kind switch
    {
        SplitAutoKind.Attached => "attached",
        SplitAutoKind.NothingOnLoglig => "none-on-loglig",
        SplitAutoKind.NothingNew => "nothing-new",
        _ => "failed",
    };

    public static SplitAutoKind ParseCode(string? code) => code switch
    {
        "attached" => SplitAutoKind.Attached,
        "none-on-loglig" => SplitAutoKind.NothingOnLoglig,
        "nothing-new" => SplitAutoKind.NothingNew,
        _ => SplitAutoKind.Failed,
    };

    /// <summary>Короткий итог: «доклеено: личных 340, ног эстафет 48» / «на loglig промежуточных нет» / …</summary>
    public static string Text(SplitAutoKind kind, int swimWrites, int legWrites) => kind switch
    {
        SplitAutoKind.Attached => "доклеено: " + string.Join(", ", new[]
        {
            swimWrites > 0 ? $"личных {swimWrites}" : null,
            legWrites > 0 ? $"ног эстафет {legWrites}" : null,
        }.Where(x => x != null)),
        SplitAutoKind.NothingOnLoglig => "на loglig промежуточных нет",
        SplitAutoKind.NothingNew => "нового нет: уже стоят или не сопоставились",
        _ => "не удалось — повторите кнопкой",
    };

    /// <summary>Сводка строки аудита у автозапуска.</summary>
    public static string AuditSummary(int competitionId, SplitAutoKind kind, int swimWrites, int legWrites) =>
        $"Промежуточные сами после импорта (#{competitionId}): {Text(kind, swimWrites, legWrites)}";

    /// <summary>
    /// Подпись шага 9 чек-листа: идёт сейчас (задача импорта ещё доклеивает) → итог последней
    /// доклейки дня из аудита → флаг <c>HasSplits</c> → null (подпись по умолчанию).
    /// </summary>
    /// <returns>Текст, подсказка (полный отчёт), красная ли подпись.</returns>
    public static (string Text, string? Title, bool Danger)? Caption(SplitAttachNote? note, bool hasSplits, bool pending)
    {
        if (pending)
            return ("промежуточные доклеиваются сами после импорта…",
                "Доклейка идёт в фоне за импортом; итог появится здесь после перезагрузки страницы", false);
        if (note is not null)
        {
            var when = $"{Constants.IsraelTime.ToLocal(note.At):dd.MM HH:mm}";
            var who = note.Auto ? "само после импорта" : "вручную";
            return ($"промежуточные: {Text(note.Kind, note.SwimWrites, note.LegWrites)} · {who} {when}",
                note.Summary, note.Kind == SplitAutoKind.Failed);
        }
        return hasSplits ? ("промежуточные есть", "Флаг «⏱ Есть промежуточные» у дня", false) : null;
    }
}
