using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Ответы на одно занятие группы (docs/plans/entity-hero-roles-plan.md, Ш2) —
/// <c>GET/PUT /api/hub-groups/{id}/rsvp/{session}</c>. ЛИЧНЫЙ ответ: только участникам и
/// управляющим, без кэша. В общий (кэшируемый) ответ страницы группы не попадает ничего
/// отсюда — ни свой ответ, ни имена: состав участников-аккаунтов приватный.
/// </summary>
public sealed class TrainingRsvpDto
{
    /// <summary>Ключ занятия <c>yyyy-MM-dd-HHmm</c> (<c>TrainingRsvpRules.SessionKey</c>).</summary>
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = "";

    /// <summary>yyyy-MM-dd по Израилю.</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("start")]
    public string Start { get; set; } = "";

    [JsonPropertyName("end")]
    public string? End { get; set; }

    [JsonPropertyName("yes")]
    public int Yes { get; set; }

    [JsonPropertyName("maybe")]
    public int Maybe { get; set; }

    [JsonPropertyName("no")]
    public int No { get; set; }

    /// <summary>
    /// Знаменатель полосы: активные участники-аккаунты, кроме тех, кто на перерыве и не ответил
    /// (Ш3.1 — «On break» не висит вечным «нет ответа»). Остаток — «нет ответа».
    /// </summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>Ответ зрителя; null — не отвечал (или он не участник).</summary>
    [JsonPropertyName("mine")]
    public TrainingRsvpMineDto? Mine { get; set; }

    /// <summary>Зритель — активный участник: ему положены кнопки ответа.</summary>
    [JsonPropertyName("is_member")]
    public bool IsMember { get; set; }

    /// <summary>Зритель управляет группой: ему — список людей и ответ за участника.</summary>
    [JsonPropertyName("can_manage")]
    public bool CanManage { get; set; }

    /// <summary>Окно ответа для зрителя открыто (занятие не началось / управляющему — неделя после).</summary>
    [JsonPropertyName("can_answer")]
    public bool CanAnswer { get; set; }

    /// <summary>Зритель на перерыве в день занятия (Ш3.1): ответ «Going» его снимет.</summary>
    [JsonPropertyName("on_break")]
    public bool OnBreak { get; set; }

    /// <summary>Последний день перерыва зрителя yyyy-MM-dd; null — бессрочно или не на перерыве.</summary>
    [JsonPropertyName("break_until")]
    public string? BreakUntil { get; set; }

    /// <summary>
    /// Все активные участники с ответами — ТОЛЬКО управляющему (иначе null). Порядок: иду,
    /// не уверен, не приду, без ответа, на перерыве без ответа; внутри — по имени.
    /// </summary>
    [JsonPropertyName("people")]
    public List<TrainingRsvpPersonDto>? People { get; set; }
}

public sealed class TrainingRsvpMineDto
{
    [JsonPropertyName("answer")]
    public string Answer { get; set; } = "";

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>Ответ поставил тренер, а не сам участник.</summary>
    [JsonPropertyName("set_by_coach")]
    public bool SetByCoach { get; set; }
}

public sealed class TrainingRsvpPersonDto
{
    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    /// <summary>Имя аккаунта (как в списке участников группы).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>male | female | null — по привязанному пловцу, для цвета аватара.</summary>
    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    /// <summary>null — не ответил.</summary>
    [JsonPropertyName("answer")]
    public string? Answer { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("set_by_coach")]
    public bool SetByCoach { get; set; }

    /// <summary>На перерыве в день занятия (Ш3.1).</summary>
    [JsonPropertyName("on_break")]
    public bool OnBreak { get; set; }

    /// <summary>Сам вернулся с перерыва недавно — сколько дней был на нём («back after …»).</summary>
    [JsonPropertyName("back_after_days")]
    public int? BackAfterDays { get; set; }
}

/// <summary>Ответ на занятие. <c>answer = null</c> — снять ответ.</summary>
public sealed class TrainingRsvpInputDto
{
    [JsonPropertyName("answer")]
    public string? Answer { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>За кого отвечаем; null — за себя. Чужой — только управляющему.</summary>
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }
}

/// <summary>Итог записи ответа: либо новые ответы занятия, либо код отказа.</summary>
public sealed class TrainingRsvpSaveResult
{
    public TrainingRsvpDto? Rsvp { get; init; }

    /// <summary>null — успех; иначе текст для 400/403/404.</summary>
    public string? Error { get; init; }

    /// <summary>HTTP-код отказа (400 / 403 / 404); у успеха не используется.</summary>
    public int Status { get; init; }

    public static TrainingRsvpSaveResult Ok(TrainingRsvpDto dto) => new() { Rsvp = dto };
    public static TrainingRsvpSaveResult Fail(int status, string error) => new() { Status = status, Error = error };
}
