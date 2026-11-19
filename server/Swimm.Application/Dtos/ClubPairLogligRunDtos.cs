namespace Swimm.Application.Dtos;

/// <summary>Состояние прогона «Проверить в loglig» (Р112). Строки — для JSON и CSS страницы.</summary>
public static class ClubPairLogligRunStates
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>Итог одной пары внутри прогона: сводка уже с новыми ответами.</summary>
public sealed record ClubPairLogligRunPair(int ClubAId, int ClubBId, ClubPairLogligSummary Summary, int Requests, int Failed);

/// <summary>
/// Прогон «Проверить в loglig» (Р112): одна пара или все пары «Общие пловцы». Живёт в памяти
/// процесса (как прогон рекордов по странам), фоновая задача правит поля по ходу, страница
/// /Admin/Clubs опрашивает статус. Списки заменяются целиком, а не дописываются: статус читают
/// из другого потока.
/// </summary>
public sealed class ClubPairLogligRunStatus
{
    public Guid RunId { get; init; }

    /// <summary><see cref="ClubPairLogligRunStates"/>.</summary>
    public string State { get; set; } = ClubPairLogligRunStates.Queued;

    /// <summary>true — все пары эвристики «общие пловцы»; false — одна пара <see cref="ClubAId"/>/<see cref="ClubBId"/>.</summary>
    public bool AllPairs { get; init; }
    public int? ClubAId { get; init; }
    public int? ClubBId { get; init; }

    public DateTimeOffset QueuedAt { get; init; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public int PairsTotal { get; set; }
    public int PairsDone { get; set; }

    /// <summary>Сколько карточек надо открыть: общие пловцы всех пар с несвежим ответом, каждый один раз.</summary>
    public int SwimmersTotal { get; set; }
    /// <summary>Сколько из них уже открыто (удачно или нет) — прогресс «12 / 87».</summary>
    public int SwimmersDone { get; set; }
    /// <summary>Карточек, которые не открылись (ответ не записан).</summary>
    public int Failed { get; set; }

    /// <summary>Какую пару проходим сейчас.</summary>
    public int? CurrentClubAId { get; set; }
    public int? CurrentClubBId { get; set; }

    public string? Error { get; set; }

    /// <summary>Пройденные пары с обновлёнными сводками — по ним страница перерисовывает строки.</summary>
    public IReadOnlyList<ClubPairLogligRunPair> Pairs { get; set; } = [];

    public bool Active => State is ClubPairLogligRunStates.Queued or ClubPairLogligRunStates.Running;
}
