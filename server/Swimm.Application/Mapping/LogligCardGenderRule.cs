using Swimm.Application.Validation;

namespace Swimm.Application.Mapping;

/// <summary>Чем кончилась попытка взять пол пловца с карточки loglig (Р125).</summary>
public enum CardGenderVerdict
{
    /// <summary>Все условия выполнены — пол с карточки пишется пловцу.</summary>
    Write,
    /// <summary>(а) Пол у пловца уже стоит — не перезаписываем, даже если loglig говорит иное.</summary>
    AlreadyKnown,
    /// <summary>(б) loglig-id нет или он не подтверждён (не <c>Verified</c>).</summary>
    NotVerified,
    /// <summary>Карточка не открылась (сеть, 404/500, чужая вёрстка) — повторить позже.</summary>
    CardUnavailable,
    /// <summary>(в) Год рождения на карточке не тот (или не напечатан).</summary>
    YearMismatch,
    /// <summary>(в) Имя на карточке не сошлось с именем пловца.</summary>
    NameMismatch,
    /// <summary>(г) Карточка открылась, а чипа «מגדר» с «זכר»/«נקבה» на ней нет.</summary>
    CardHasNoGender,
}

/// <summary>Пловец глазами правила: имя — уже нормализованные токены (нормализация дедупа).</summary>
public sealed record CardGenderSwimmer(
    string? Gender, int? LogligId, string? LogligIdStatus, IReadOnlyCollection<string> NameTokens, int BirthYear);

/// <summary>Что напечатано на карточке loglig: имя (нормализованные токены), год, пол как есть.</summary>
public sealed record CardGenderAnswer(IReadOnlyCollection<string> NameTokens, int? BirthYear, string? Gender);

/// <summary>
/// Р125: пол пловца — с карточки loglig, когда у нас он неизвестен. Чистая функция; сеть, суточный
/// ответ карточки и запись — у <c>LogligCardGenderService</c>.
///
/// Зачем. Смешанный заплыв («שומרי שבת», категории <c>mix-…</c>) не печатает пол в шапке, и у
/// пловца, который плыл только такие, пол остаётся пустым — находка <c>results.no-gender</c> висит
/// до ручного клика, хотя рядом стоит подтверждённый loglig-id, а на карточке loglig есть чип
/// «מגדר» (живой случай 25.11.2026: comp 1896, 13 строк, 12 пловцов).
///
/// Пишем, только если выполнено ВСЁ:
/// (а) пол у пловца пуст — напечатанное и проставленное человеком не перезаписываем;
/// (б) loglig-id подтверждён (<c>Verified</c>);
/// (в) год рождения совпал и имя сошлось тем же сопоставлением, которым loglig-id привязывают по
///     протоколу (<see cref="TokenNameMatcher"/>: тот же набор токенов либо одно имя полнее другого);
/// (г) карточка дала однозначный пол.
/// Расхождение «у нас female, на loglig male» правило не чинит — его показывает проверка
/// <c>swimmers.gender-vs-loglig</c>.
/// </summary>
public static class LogligCardGenderRule
{
    /// <summary>Тумблер шага после импорта (/Admin/Settings), по умолчанию выключен (до ревью расчёта последствий).</summary>
    public const string SettingKey = "LogligGenderOnImport";

    /// <summary>Действие в журнале аудита — строка на каждого пловца, которому проставлен пол.</summary>
    public const string AuditAction = "swimmer.gender-from-loglig";

    /// <summary>Источник пола в аудите.</summary>
    public const string Source = "loglig card";

    /// <summary>Актор автозапуска после импорта.</summary>
    public const string ActorName = "auto:import";

    /// <summary>Потолок запросов карточек на один импорт (запрос ≈ 1–2 с; пауза между ними 1 с).</summary>
    public const int MaxRequestsPerImport = 40;

    /// <summary>
    /// Потолок запросов на одно нажатие кнопки в Health; остаток — повторным нажатием. Вместе с
    /// <see cref="ButtonTimeout"/> держит нажатие далеко от лимита запроса Azure (230 с).
    /// </summary>
    public const int MaxRequestsPerButton = 30;

    /// <summary>Сколько одно нажатие кнопки идёт по карточкам; не успело — честный итог «дошли до N из M».</summary>
    public static readonly TimeSpan ButtonTimeout = TimeSpan.FromSeconds(150);

    /// <summary>Действие аудита, когда пол с карточки подтвердил человек (находка ручной проверки).</summary>
    public const string ConfirmAuditAction = "swimmer.gender-from-loglig-confirmed";

    /// <summary>
    /// Действие аудита на каждое нажатие кнопки «взять пол с карточек loglig» в /Admin/Health. По
    /// последней такой строке страница показывает итог прошлого нажатия (значок ⓘ у кнопки).
    /// </summary>
    public const string ButtonAuditAction = "datacheck.fix-gender-from-loglig";

    /// <summary>Причина ручной проверки «личность не подтверждена» — словами для итога кнопки.</summary>
    public const string IdentityUnconfirmedWhy = "личность не подтверждена: на карточке loglig нет ни одного заплыва пловца";

    /// <summary>Причина ручной проверки «серьёзные последствия» — словами для итога кнопки.</summary>
    public const string ImpactWhy = "изменилось бы серьёзное (рекорд, место, медаль, очки или топ)";

    /// <summary>Жёсткий потолок шага после импорта.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>Сколько живёт ответ карточки в общем месте (<c>Sys_SwimmerLogligClubs</c>) — как у клуба.</summary>
    public static readonly TimeSpan CacheTtl = SwimmerClubLogligRule.CacheTtl;

    /// <summary>Пол в формате <c>Swimmers.Gender</c> (CK: только male/female) из любого написания; иначе null.</summary>
    public static string? ToSwimmerGender(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "m" or "male" => "male",
        "f" or "female" => "female",
        _ => null,
    };

    /// <summary>Пол у пловца уже есть (любое непустое значение — и старое «M»/«F»).</summary>
    public static bool GenderKnown(string? gender) => !string.IsNullOrWhiteSpace(gender);

    /// <summary>(в) Карточка — про того же человека: год совпал и имя сошлось.</summary>
    public static bool SamePerson(CardGenderSwimmer swimmer, CardGenderAnswer card) =>
        Identity(swimmer, card) == CardGenderVerdict.Write;

    /// <param name="card">null — карточка не открылась.</param>
    public static (CardGenderVerdict Verdict, string? Gender) Decide(CardGenderSwimmer swimmer, CardGenderAnswer? card)
    {
        if (GenderKnown(swimmer.Gender)) return (CardGenderVerdict.AlreadyKnown, null);
        if (swimmer.LogligId is null || swimmer.LogligIdStatus != "Verified") return (CardGenderVerdict.NotVerified, null);
        if (card is null) return (CardGenderVerdict.CardUnavailable, null);

        var identity = Identity(swimmer, card);
        if (identity != CardGenderVerdict.Write) return (identity, null);

        return ToSwimmerGender(card.Gender) is { } gender
            ? (CardGenderVerdict.Write, gender)
            : (CardGenderVerdict.CardHasNoGender, null);
    }

    private static CardGenderVerdict Identity(CardGenderSwimmer swimmer, CardGenderAnswer card)
    {
        // Год обязателен: без него совпадение имени — слишком слабый признак (тёзки среди детей).
        if (card.BirthYear is not int year || year != swimmer.BirthYear) return CardGenderVerdict.YearMismatch;
        if (swimmer.NameTokens.Count == 0 || card.NameTokens.Count == 0) return CardGenderVerdict.NameMismatch;

        var same = TokenNameMatcher.ResolveSingle<bool>(
            [(card.NameTokens, card.BirthYear, true)], swimmer.NameTokens, swimmer.BirthYear);
        return same ? CardGenderVerdict.Write : CardGenderVerdict.NameMismatch;
    }

    /// <summary>Свежий ли сохранённый ответ карточки: тот же loglig-id, моложе суток и записан с именем.</summary>
    public static bool CacheValid(DateTime now, StoredLogligClub? stored, int logligId) =>
        stored is { CardName: not null } && stored.LogligId == logligId && now - stored.CheckedAt < CacheTtl;


    /// <summary>
    /// Р126, вторые ворота — уже после (а)–(г): причины, по которым пол НЕ пишется автоматически, а
    /// уходит человеку. Пусто — писать можно.
    /// • Личность: имени и года мало («טל כהן» — двуполое имя, тёзки среди детей). Нужен хоть один
    ///   заплыв пловца на этой карточке (<paramref name="cardResultsMatched"/>) либо строка
    ///   пловца, у которой игрок loglig со страницы дисциплины — этот же id (<paramref name="rowPlayerMatched"/>, Р122).
    /// • Серьёзные последствия (<see cref="GenderImpactRules"/>).
    /// </summary>
    public static IReadOnlyList<GenderImpactItem> ReviewReasons(
        bool cardResultsMatched, bool rowPlayerMatched, GenderImpact impact)
    {
        var reasons = new List<GenderImpactItem>();
        if (!cardResultsMatched && !rowPlayerMatched)
            reasons.Add(new(GenderImpactRules.IdentityUnconfirmed,
                "не смогли подтвердить, что это тот же человек: имя и год совпали, но ни один заплыв "
                + "пловца на карточке loglig не найден"));
        reasons.AddRange(impact.Serious);
        return reasons;
    }

    /// <summary>
    /// Годится ли сохранённый ответ для решения о поле: свежий (<see cref="CacheValid"/>) и про
    /// совпадение заплывов уже известно — либо из карточки, либо по строкам (Р122). Иначе
    /// карточку открываем заново: в общем месте результатов карточки нет.
    /// </summary>
    public static bool CacheDecides(DateTime now, StoredLogligClub? stored, int logligId, bool rowPlayerMatched) =>
        CacheValid(now, stored, logligId) && (rowPlayerMatched || stored!.ResultsMatched is not null);

    /// <summary>Итог прогона одной строкой — для тоста кнопки, лога и аудита.</summary>
    public static string Summary(
        int candidates, int set, int rows, int cardWithoutGender, int failed, int mismatch,
        int notVerified, int overLimit, int requests, int fromCache,
        int review = 0, int dbFailed = 0, int reached = -1, int reviewIdentity = 0, int reviewImpact = 0)
    {
        if (candidates == 0) return "Пловцов без пола нет — брать с loglig нечего.";

        var parts = new List<string> { $"проставлен {set} (строк результата {rows})" };
        if (review > 0) parts.Add(ReviewPart(review, reviewIdentity, reviewImpact));
        if (dbFailed > 0) parts.Add($"сбой записи — {dbFailed}");
        // Прервано по времени: честно — докуда дошли.
        if (reached >= 0 && reached < candidates)
            parts.Insert(0, $"время вышло, дошли до {reached} из {candidates} — нажмите ещё раз");
        if (cardWithoutGender > 0) parts.Add($"на карточке пола нет — {cardWithoutGender}");
        if (mismatch > 0) parts.Add($"имя или год карточки не совпали — {mismatch}");
        if (failed > 0) parts.Add($"карточка не открылась — {failed}");
        if (notVerified > 0) parts.Add($"без подтверждённого loglig-id — {notVerified}");
        if (overLimit > 0) parts.Add($"не дошли из-за потолка запросов — {overLimit}, повторите");
        return $"Пловцов без пола {candidates}: {string.Join("; ", parts)}. "
               + $"Запросов к loglig {requests}, из суточного ответа {fromCache}.";
    }

    /// <summary>
    /// «Ждут ручной проверки» с причиной словами (просьба Влада 25.11.2026: из одного числа не
    /// понять, почему не проставлено). У пловца могут быть обе причины — тогда он в обоих числах.
    /// </summary>
    private static string ReviewPart(int review, int identity, int impact)
    {
        var head = $"ждут ручной проверки — {review}";
        if (identity == review && impact == 0) return $"{head}, потому что {IdentityUnconfirmedWhy}";
        if (impact == review && identity == 0) return $"{head}, потому что {ImpactWhy}";
        var why = new List<string>();
        if (identity > 0) why.Add($"{IdentityUnconfirmedWhy} — {identity}");
        if (impact > 0) why.Add($"{ImpactWhy} — {impact}");
        if (why.Count == 0) return head;
        return $"{head} ({string.Join(", ", why)}{(identity + impact > review ? "; у части — обе причины" : "")})";
    }
}
