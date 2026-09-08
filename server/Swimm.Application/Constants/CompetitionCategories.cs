namespace Swimm.Application.Constants;

/// <summary>
/// Канонический таб селектора для соревнования: одна лестница приоритетов на весь продукт.
///
/// Правило жило локальной функцией внутри <c>ResultRepository</c> (выдача /api/competitions),
/// и второму потребителю (плитка соревнования в My media) пришлось бы его скопировать —
/// а копия рано или поздно разъедется: до 2026-07-31 ключи табов уже переезжали по кругу,
/// и любой отставший экран показал бы «K» там, где давно «Y».
/// </summary>
public static class CompetitionCategories
{
    public const string Masters = "masters";
    public const string Kids8_11 = "kids8_11";
    public const string Young11_14 = "young11_14";
    public const string Juniors = "juniors";
    public const string Adults = "adults";

    /// <summary>
    /// Канонический ключ по членству в категориях (сырые <c>Category.Key</c>) и флагу мастерс.
    /// <c>null</c> — соревнование ни в одной из канонических: кастомная категория
    /// (result-maccabiah) или её нет вовсе. Клиент показывает такое в «All» и в кастомных
    /// табах, но НЕ в Junior — фоллбека «всё прочее = junior» здесь сознательно нет.
    /// Ключи табов соответствуют ступеням (2026-07-31): kids8_11 «Kids» (8–11),
    /// young11_14 «Young» (11–14), juniors «Juniors» (נוער), adults «Adults» (בוגרים).
    /// </summary>
    public static string? Canonical(bool isMasters, IReadOnlyCollection<string>? categoryKeys) =>
        isMasters || categoryKeys?.Contains("results-masters") == true ? Masters
        : categoryKeys?.Contains("results-kids-team") == true ? Kids8_11
        : categoryKeys?.Contains("results-youth-team") == true ? Young11_14
        : categoryKeys?.Contains("results-junior-results") == true ? Juniors
        : categoryKeys?.Contains("results-main") == true ? Adults
        : null;
}
