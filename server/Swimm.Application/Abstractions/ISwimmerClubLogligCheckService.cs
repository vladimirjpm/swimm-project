using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Сверка кандидатов на смену клуба с карточкой loglig.com (Р98, §6 docs/plans/swimmer-club-change-plan.md).
/// Правило — чистое <c>SwimmerClubLogligRule</c>; здесь сеть (последовательно, ~1 запрос в секунду),
/// суточный кэш ответа (в самом кандидате) и запись: подтверждённая смена → <c>Swimmers.ClubId</c>
/// + журнал с источником <c>loglig</c>, кандидат удаляется; остальные ждут со своим вердиктом.
///
/// Зовут: фоновая служба (после импорта и раз в сутки), CLI <c>--refresh-swimmer-clubs</c> и кнопка
/// «проверить сейчас» в /Admin/Swimmers. Права не проверяет — это дело вызывающего (Admin).
/// </summary>
public interface ISwimmerClubLogligCheckService
{
    /// <summary>Сверить ждущих кандидатов и применить подтверждённые.</summary>
    Task<SwimmerClubCheckReport> CheckPendingAsync(SwimmerClubCheckScope scope, CancellationToken ct = default);

    /// <summary>
    /// Сверить кандидата одного пловца сейчас (кнопка админки): суточный кэш игнорируется, карточка
    /// loglig открывается заново. null — у пловца нет ждущего кандидата.
    /// </summary>
    Task<SwimmerClubVerdictDto?> CheckSwimmerAsync(int swimmerId, CancellationToken ct = default);

    /// <summary>
    /// Dry-run: вердикт loglig по плану догона, НИЧЕГО не пишет. В loglig ходит (ответ моложе суток
    /// из уже записанных кандидатов берётся без сети). Порядок ответа — порядок плана.
    /// </summary>
    Task<SwimmerClubCheckReport> PreviewAsync(
        IReadOnlyList<SwimmerClubChangePlan> plans, Action<int, int>? progress = null, CancellationToken ct = default);
}

/// <summary>
/// Сигнал «импорт завёл новых кандидатов»: фоновая служба сверки просыпается сразу, а не ждёт
/// ближайшего тика. Синглтон процесса; без службы (прод, локальная разработка) сигнал просто никто
/// не ждёт — кандидаты сверит суточный прогон в ProdAdmin или CLI.
/// </summary>
public interface ISwimmerClubCheckSignal
{
    /// <summary>Есть новые кандидаты. Несколько сигналов подряд сливаются в один.</summary>
    void Notify();

    /// <summary>Ждать сигнала не дольше <paramref name="timeout"/>; true — сигнал пришёл.</summary>
    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct);
}
