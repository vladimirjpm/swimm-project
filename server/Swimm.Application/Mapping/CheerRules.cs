namespace Swimm.Application.Mapping;

/// <summary>
/// 🎉 «поздравить с заплывом» — числа для чтения счётчиков (<c>GET /api/results/cheers</c>).
///
/// Клики (поставить/снять) живут под общей политикой реакций «reactions» (60/мин на
/// пользователя). Чтение — ОТДЕЛЬНО: страница протокола спрашивает счётчики пачками, и
/// общий лимит на клики ломал бы её при обычном листании. Поэтому у чтения своя мягкая
/// политика по IP (решение Влада 25.10.2026): обычная страница в неё не упирается, бот —
/// упирается. IP настоящий: <c>UseForwardedHeaders</c> разбирает <c>X-Forwarded-For</c>
/// фронтенда App Service раньше лимитера.
/// </summary>
public static class CheerRules
{
    /// <summary>Сколько id принимает одно чтение (больше — 400).</summary>
    public const int MaxIdsPerRead = 500;

    /// <summary>Сколько id клиент кладёт в один запрос (<c>hooks/useCheers.ts</c>, CHUNK).</summary>
    public const int ClientChunk = 300;

    /// <summary>Политика rate limit чтения счётчиков (Program.cs).</summary>
    public const string ReadRateLimitPolicy = "cheers-read";

    /// <summary>
    /// Запросов в минуту на IP. Самый большой протокол — около 4000 строк = 14 запросов на
    /// загрузку; 300 — это 20+ полных загрузок в минуту с одного адреса (клуб, школа, общий
    /// Wi-Fi на соревновании), а бот упирается в 5 запросов в секунду.
    /// </summary>
    public const int ReadPerMinute = 300;

    /// <summary>Строк в самом большом протоколе с запасом — для проверки бюджета в тестах.</summary>
    public const int LargestProtocolRows = 4000;
}
