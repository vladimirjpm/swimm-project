using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Разбор протокола isr.org.il/loglig парой HE + EN — одно правило для кнопки «Затянуть»,
/// пакетного забора (<see cref="DiscoveryPreviewService"/>) и консольного <c>--repull</c>.
///
/// HE — основной файл (канонические имена), EN вторым даёт <c>LastNameEn</c>/<c>FirstNameEn</c>.
/// Без EN-имён импорт теряет фоллбек матчинга по английскому имени и заводит тень там, где
/// ивритская карточка записана иначе: 08.10.2026 консольный перезалив #1720 (тогда HE-only)
/// завёл двойника #59991, у которого ивритские имя и фамилия переставлены (И-46).
/// </summary>
public static class ProtocolPairParser
{
    /// <summary>Итог разбора. <c>UsedEn</c> = EN-файл реально участвовал в разборе.</summary>
    public sealed record Result(ParsedCompetition Parsed, bool UsedEn, string? PairError);

    /// <summary>
    /// Пара HE + EN; пара не склеилась (разный порядок записей и т.п.) — откат на HE-only,
    /// причина в <see cref="Result.PairError"/> (EN-имена потом добирает «Синхр. языки»).
    /// Ошибка разбора самого HE-файла пробрасывается как есть.
    /// </summary>
    public static async Task<Result> ParseAsync(
        IResultSourceProvider source, byte[] pdfHe, string fileNameHe, byte[]? pdfEn, string? fileNameEn)
    {
        if (pdfEn != null)
        {
            try
            {
                using var ms = new MemoryStream(pdfHe);
                using var msEn = new MemoryStream(pdfEn);
                var pair = await source.ParseAsync(new ResultSourceRequest(
                    ms, fileNameHe, "IsrOrg",
                    SecondaryStream: msEn, SecondaryFileName: fileNameEn,
                    Language: "he"));
                return new Result(pair, UsedEn: true, PairError: null);
            }
            catch (InvalidOperationException ex)
            {
                using var msHe = new MemoryStream(pdfHe);
                var heOnly = await source.ParseAsync(new ResultSourceRequest(msHe, fileNameHe, "IsrOrg", Language: "he"));
                return new Result(heOnly, UsedEn: false, PairError: ex.Message);
            }
        }

        using var single = new MemoryStream(pdfHe);
        var parsed = await source.ParseAsync(new ResultSourceRequest(single, fileNameHe, "IsrOrg", Language: "he"));
        return new Result(parsed, UsedEn: false, PairError: null);
    }
}
