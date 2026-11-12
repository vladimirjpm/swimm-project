using System.Buffers.Binary;

namespace Swimm.Application.Validation;

/// <summary>Что сервер узнал о картинке значка из её собственных байтов.</summary>
public sealed record SeriesEmblemInfo(string ContentType, int Width, int Height);

/// <summary>
/// Проверка загружаемого значка серии (docs/plans/series-emblem-plan.md). Серверной библиотеки
/// обработки изображений в проекте нет, и она не нужна: уменьшает картинку браузер админки, а
/// здесь только удостоверяемся, что пришло именно то, что можно отдавать наружу.
///
/// Тип определяется по СИГНАТУРЕ файла — заголовку Content-Type и расширению не верим: значок
/// раздаётся публично, и «картинка», оказавшаяся HTML или SVG со скриптом, недопустима. Принимаем
/// только PNG и WebP (растр без исполняемого содержимого); размеры читаем из заголовка файла.
/// </summary>
public static class SeriesEmblemImage
{
    /// <summary>Лимит файла. Ожидаемый значок после уменьшения — 30–100 КБ.</summary>
    public const int MaxBytes = 300 * 1024;

    /// <summary>
    /// Лимит стороны. Браузер уменьшает до <see cref="TargetSide"/>; запас — на случай, если
    /// значку понадобится двойная плотность.
    /// </summary>
    public const int MaxSide = 1024;

    /// <summary>До какой большей стороны уменьшает картинку страница админки.</summary>
    public const int TargetSide = 512;

    public const string Png = "image/png";
    public const string Webp = "image/webp";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> PngEnd => [0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    public static bool TryRead(ReadOnlySpan<byte> data, out SeriesEmblemInfo info, out string error)
    {
        info = new SeriesEmblemInfo("", 0, 0);
        error = "";

        if (data.Length == 0) { error = "Файл пуст"; return false; }
        if (data.Length > MaxBytes)
        {
            error = $"Файл больше {MaxBytes / 1024} КБ ({data.Length / 1024} КБ)";
            return false;
        }

        int width, height;
        string type;
        if (data.StartsWith(PngSignature))
        {
            if (!TryReadPng(data, out width, out height)) { error = "Файл PNG повреждён или обрезан"; return false; }
            type = Png;
        }
        else if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            if (!TryReadWebp(data, out width, out height, out var animated)) { error = "Файл WebP повреждён или обрезан"; return false; }
            if (animated) { error = "Анимированный WebP не подходит — нужен статичный значок"; return false; }
            type = Webp;
        }
        else
        {
            error = "Это не PNG и не WebP (проверяется содержимое файла, а не расширение)";
            return false;
        }

        if (width < 1 || height < 1 || width > MaxSide || height > MaxSide)
        {
            error = $"Размер {width}×{height} — сторона значка должна быть от 1 до {MaxSide} px";
            return false;
        }

        info = new SeriesEmblemInfo(type, width, height);
        return true;
    }

    /// <summary>Расширение для имени файла при скачивании и в адресе — по типу содержимого.</summary>
    public static string Extension(string contentType) => contentType == Webp ? "webp" : "png";

    // PNG: подпись (8) + первый блок IHDR: длина (4, =13), «IHDR» (4), ширина (4 BE), высота (4 BE).
    // Конец файла — блок IEND: по нему видно, что загрузка не оборвалась.
    private static bool TryReadPng(ReadOnlySpan<byte> d, out int width, out int height)
    {
        width = height = 0;
        if (d.Length < 8 + 25 + 12) return false;
        if (BinaryPrimitives.ReadUInt32BigEndian(d[8..]) != 13 || !d.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
        if (!d.EndsWith(PngEnd)) return false;

        var w = BinaryPrimitives.ReadUInt32BigEndian(d[16..]);
        var h = BinaryPrimitives.ReadUInt32BigEndian(d[20..]);
        if (w > int.MaxValue || h > int.MaxValue) return false;
        width = (int)w;
        height = (int)h;
        return true;
    }

    // WebP: «RIFF» + размер (4 LE) + «WEBP», дальше первый блок одного из трёх видов:
    //   «VP8 » — с потерями: размеры по 14 бит после стартового кода 9D 01 2A;
    //   «VP8L» — без потерь: байт 0x2F, затем 14 + 14 бит «размер минус один»;
    //   «VP8X» — расширенный: флаги, затем холст 24 + 24 бита «размер минус один».
    private static bool TryReadWebp(ReadOnlySpan<byte> d, out int width, out int height, out bool animated)
    {
        width = height = 0;
        animated = false;
        if (d.Length < 30) return false;
        // Размер в заголовке RIFF обязан совпасть с файлом: обрезанный или с хвостом — отказ.
        if (BinaryPrimitives.ReadUInt32LittleEndian(d[4..]) + 8 != (uint)d.Length) return false;

        var chunk = d.Slice(12, 4);
        if (chunk.SequenceEqual("VP8 "u8))
        {
            if (d[23] != 0x9D || d[24] != 0x01 || d[25] != 0x2A) return false;
            width = BinaryPrimitives.ReadUInt16LittleEndian(d[26..]) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(d[28..]) & 0x3FFF;
            return true;
        }
        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (d[20] != 0x2F) return false;
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(d[21..]);
            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
            return true;
        }
        if (chunk.SequenceEqual("VP8X"u8))
        {
            animated = (d[20] & 0x02) != 0;
            width = (d[24] | d[25] << 8 | d[26] << 16) + 1;
            height = (d[27] | d[28] << 8 | d[29] << 16) + 1;
            return true;
        }
        return false;
    }
}
