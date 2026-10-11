using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Перерывы группы (Ш3.1) — <c>GET /api/hub-groups/{id}/breaks</c>. ЛИЧНЫЙ ответ, без кэша:
/// управляющему — все открытые перерывы и недавние возвращения, участнику — только свой.
/// </summary>
public sealed class HubGroupBreaksDto
{
    /// <summary>Свой перерыв зрителя (как аккаунта); null — не на перерыве.</summary>
    [JsonPropertyName("mine")]
    public HubGroupBreakDto? Mine { get; set; }

    /// <summary>Все действующие перерывы — только управляющему (иначе null).</summary>
    [JsonPropertyName("breaks")]
    public List<HubGroupBreakDto>? Breaks { get; set; }

    /// <summary>Вернулись сами (ответом «Going») за последние дни — только управляющему.</summary>
    [JsonPropertyName("returns")]
    public List<HubGroupBreakDto>? Returns { get; set; }

    [JsonPropertyName("can_manage")]
    public bool CanManage { get; set; }
}

public sealed class HubGroupBreakDto
{
    /// <summary>Аккаунт; ровно одно из user_id / swimmer_id.</summary>
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int? SwimmerId { get; set; }

    /// <summary>Имя аккаунта или пловца (иврит по умолчанию).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>ISO-дата с какого дня.</summary>
    [JsonPropertyName("since")]
    public string Since { get; set; } = "";

    /// <summary>Последний день перерыва yyyy-MM-dd; null — бессрочно.</summary>
    [JsonPropertyName("until")]
    public string? Until { get; set; }

    /// <summary>Поставил тренер (а не сам человек).</summary>
    [JsonPropertyName("set_by_coach")]
    public bool SetByCoach { get; set; }

    /// <summary>Только у возвращений: сколько дней был на перерыве.</summary>
    [JsonPropertyName("back_after_days")]
    public int? BackAfterDays { get; set; }
}

/// <summary>
/// Поставить/снять перерыв. Субъект: <c>swimmer_id</c> (пловец состава, только управляющему),
/// <c>user_id</c> (аккаунт; чужой — только управляющему) или ничего — сам зритель.
/// </summary>
public sealed class HubGroupBreakInputDto
{
    [JsonPropertyName("user_id")]
    public int? UserId { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int? SwimmerId { get; set; }

    /// <summary>true — на перерыв, false — снять.</summary>
    [JsonPropertyName("on_break")]
    public bool OnBreak { get; set; }

    /// <summary>yyyy-MM-dd — последний день перерыва; null — бессрочно (только тренеру).</summary>
    [JsonPropertyName("until")]
    public string? Until { get; set; }
}

/// <summary>Итог записи перерыва: свежее состояние или код отказа (как у RSVP).</summary>
public sealed class HubGroupBreakSaveResult
{
    public HubGroupBreaksDto? Breaks { get; init; }
    public string? Error { get; init; }
    public int Status { get; init; }

    public static HubGroupBreakSaveResult Ok(HubGroupBreaksDto dto) => new() { Breaks = dto };
    public static HubGroupBreakSaveResult Fail(int status, string error) => new() { Status = status, Error = error };
}
