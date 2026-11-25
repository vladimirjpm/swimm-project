namespace Swimm.Application.Abstractions;

/// <summary>
/// Автосклейка надёжных двойников, найденных по неявкам стартового протокола (решение Влада
/// 25.11.2026). Заявка висит на карточке с loglig-id, а результат в её заплыве и дорожке —
/// на свежей карточке «его же» (импорт не узнал имя: короче, без второго имени). Такие пары
/// склеиваются штатной <see cref="ISwimmerMergeService"/>; что не надёжно (год расходится,
/// опечатка, тёзки с разными loglig-id) — остаётся человеку в находке «Заявки без результата».
/// </summary>
public interface INoShowTwinMergeService
{
    /// <summary>Склеить надёжных двойников неявок этих соревнований-дней. Выключатель — <see cref="NoShowTwinMergeRules.SettingKey"/>.</summary>
    Task<NoShowTwinMergeReport> MergeAsync(IReadOnlyCollection<int> competitionIds, CancellationToken ct = default);
}

/// <param name="Merged">«#остался ← #ушёл имя» на каждую склеенную пару.</param>
/// <param name="Skipped">Надёжная по признакам пара, которую склейка отвергла (конфликт) — с причиной.</param>
public sealed record NoShowTwinMergeReport(IReadOnlyList<string> Merged, IReadOnlyList<string> Skipped)
{
    public static NoShowTwinMergeReport Empty { get; } = new([], []);
}

public static class NoShowTwinMergeRules
{
    /// <summary>Тумблер (/Admin/Settings): склеивать надёжных двойников неявок после импорта.</summary>
    public const string SettingKey = "NoShowTwinAutoMerge";

    /// <summary>Строка аудита на каждую склейку.</summary>
    public const string AuditAction = "swimmer.auto-merge-noshow-twin";
}
