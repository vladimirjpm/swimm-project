using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// «Витрина держит прошлый сезон» — пояснение для всех витрин season best
/// (docs/season-boundary-rule.md).
///
/// Приходит ТОЛЬКО в окне между началом календарного сезона и последним зимним чемпионатом:
/// новый сезон уже идёт, но season best за него ещё не показывают — стартов слишком мало,
/// чтобы «лучшее в сезоне» что-то значило. Вне этого окна поле <c>null</c>, и витрина о
/// границе молчит.
///
/// ⚠ Здесь только ДАННЫЕ, без готовой фразы: текст интерфейса живёт на клиенте
/// (client/src/utils/helpers/season-helper.ts), один на все витрины.
/// </summary>
public sealed class ShowcaseSeasonNoticeDto
{
    /// <summary>Год начала сезона, который витрина показывает вместо нового.</summary>
    [JsonPropertyName("showing_season")]
    public int ShowingSeason { get; set; }

    /// <summary>Метка показываемого сезона: «2025/26».</summary>
    [JsonPropertyName("showing_label")]
    public string ShowingLabel { get; set; } = "";

    /// <summary>Год начала сезона, который уже идёт по календарю, но ещё не открыт витриной.</summary>
    [JsonPropertyName("pending_season")]
    public int PendingSeason { get; set; }

    /// <summary>Метка ждущего сезона: «2026/27».</summary>
    [JsonPropertyName("pending_label")]
    public string PendingLabel { get; set; } = "";

    /// <summary>
    /// Дата ближайшего зимнего чемпионата ждущего сезона (dd/MM/yyyy) — если он уже есть в
    /// базе. null — расписания ещё нет, и обещать дату нечем.
    ///
    /// ⚠ Это НЕ дата переключения витрины: переключает её ПОСЛЕДНИЙ зимний чемпионат всех
    /// ступеней, а они плывут врозь (в 2025/26 мастерс 10 января, возрастные 13–26 февраля).
    /// Поэтому UI говорит «чемпионаты начинаются такого-то», а не «откроется такого-то».
    /// </summary>
    [JsonPropertyName("winter_starts")]
    public string? WinterStarts { get; set; }
}
