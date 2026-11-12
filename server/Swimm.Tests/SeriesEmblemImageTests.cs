using System.Buffers.Binary;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swimm.API.Controllers;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Validation;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Значок серии (docs/plans/series-emblem-plan.md), часть без базы: что сервер принимает как
/// картинку (сигнатура PNG/WebP, а не расширение и не Content-Type; лимиты размера и стороны)
/// и как публичный адрес её отдаёт (хэш в пути, вечный кэш, nosniff).
/// </summary>
public class SeriesEmblemImageTests
{
    // ── Заготовки файлов: только то, что читает проверка, — заголовок и конец ──────────────

    /// <summary>PNG: подпись, блок IHDR с размерами, <paramref name="padding"/> байт «данных», блок IEND.</summary>
    internal static byte[] Png(int width, int height, int padding = 0)
    {
        var d = new byte[8 + 25 + padding + 12];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(d, 0);
        BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(8), 13);
        "IHDR"u8.CopyTo(d.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(20), (uint)height);
        new byte[] { 0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 }.CopyTo(d, d.Length - 12);
        return d;
    }

    private static byte[] Riff(string chunk, int length = 40)
    {
        var d = new byte[length];
        "RIFF"u8.CopyTo(d);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(4), (uint)(length - 8));
        "WEBP"u8.CopyTo(d.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(d, 12);
        return d;
    }

    internal static byte[] WebpLossy(int width, int height)
    {
        var d = Riff("VP8 ");
        d[23] = 0x9D; d[24] = 0x01; d[25] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(26), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(28), (ushort)height);
        return d;
    }

    private static byte[] WebpLossless(int width, int height)
    {
        var d = Riff("VP8L");
        d[20] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(21), (uint)(width - 1) | (uint)(height - 1) << 14);
        return d;
    }

    private static byte[] WebpExtended(int width, int height, bool animated = false)
    {
        var d = Riff("VP8X");
        d[20] = (byte)(animated ? 0x02 : 0x10);
        d[24] = (byte)(width - 1); d[25] = (byte)((width - 1) >> 8);
        d[27] = (byte)(height - 1); d[28] = (byte)((height - 1) >> 8);
        return d;
    }

    // ── Что принимается ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Png_IsRecognised_WithDimensionsFromHeader()
    {
        Assert.True(SeriesEmblemImage.TryRead(Png(512, 384), out var info, out var error), error);
        Assert.Equal(new SeriesEmblemInfo("image/png", 512, 384), info);
    }

    [Fact]
    public void Webp_AllThreeEncodings_AreRecognised()
    {
        Assert.True(SeriesEmblemImage.TryRead(WebpLossy(512, 300), out var lossy, out _));
        Assert.Equal(new SeriesEmblemInfo("image/webp", 512, 300), lossy);

        Assert.True(SeriesEmblemImage.TryRead(WebpLossless(400, 512), out var lossless, out _));
        Assert.Equal(new SeriesEmblemInfo("image/webp", 400, 512), lossless);

        // Так кодирует браузер картинку с прозрачностью.
        Assert.True(SeriesEmblemImage.TryRead(WebpExtended(512, 512), out var extended, out _));
        Assert.Equal(new SeriesEmblemInfo("image/webp", 512, 512), extended);
    }

    [Fact]
    public void FileAtTheLimit_IsAccepted_OneByteOver_IsNot()
    {
        var atLimit = Png(512, 512, SeriesEmblemImage.MaxBytes - Png(1, 1).Length);
        Assert.Equal(SeriesEmblemImage.MaxBytes, atLimit.Length);
        Assert.True(SeriesEmblemImage.TryRead(atLimit, out _, out _));

        var over = Png(512, 512, SeriesEmblemImage.MaxBytes - Png(1, 1).Length + 1);
        Assert.False(SeriesEmblemImage.TryRead(over, out _, out var error));
        Assert.Contains("КБ", error);
    }

    // ── Что отвергается ──────────────────────────────────────────────────────────────────

    public static TheoryData<string, byte[]> NotAnEmblem() => new()
    {
        { "пустой файл", [] },
        { "JPEG", [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0xFF, 0xD9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] },
        { "GIF", "GIF89a\u0001\0\u0001\0\0\0\0;                       "u8.ToArray() },
        { "SVG со скриптом", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray() },
        { "HTML", "<!doctype html><html><body><script>alert(1)</script></body></html>"u8.ToArray() },
        { "PNG без конца (обрезан)", Png(512, 512)[..^4] },
        { "подпись PNG и мусор", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38] },
        { "WebP короче, чем заявлено в RIFF", WebpLossy(512, 512)[..^2] },
        { "RIFF, но не WebP (WAV)", System.Text.Encoding.ASCII.GetBytes("RIFF \0\0\0WAVEfmt                             ") },
        { "анимированный WebP", WebpExtended(512, 512, animated: true) },
        { "сторона больше лимита", Png(SeriesEmblemImage.MaxSide + 1, 100) },
        { "нулевая сторона", Png(0, 100) },
    };

    [Theory]
    [MemberData(nameof(NotAnEmblem))]
    public void Rejected(string what, byte[] data)
    {
        Assert.False(SeriesEmblemImage.TryRead(data, out _, out var error), what);
        Assert.NotEqual("", error);
    }

    // ── Публичная выдача ─────────────────────────────────────────────────────────────────

    private sealed class ReaderStub(int id, string sha256, SeriesEmblemFileDto file) : ISeriesEmblemReader
    {
        public Task<SeriesEmblemFileDto?> GetAsync(int requestedId, string requestedSha, CancellationToken ct = default) =>
            Task.FromResult(requestedId == id && requestedSha == sha256 ? file : null);
    }

    private static SeriesEmblemsController Controller(ISeriesEmblemReader reader) =>
        new(reader) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task PublicUrl_ServesBytes_WithImmutableCache_AndNoSniff()
    {
        var bytes = Png(512, 512);
        var sha = new string('a', 64);
        var controller = Controller(new ReaderStub(7, sha, new SeriesEmblemFileDto(bytes, "image/png")));

        var result = Assert.IsType<FileContentResult>(await controller.Get(7, sha, default));

        Assert.Equal(bytes, result.FileContents);
        Assert.Equal("image/png", result.ContentType);
        var headers = controller.Response.Headers;
        Assert.Equal("public, max-age=31536000, immutable", headers.CacheControl.ToString());
        Assert.Equal("nosniff", headers.XContentTypeOptions.ToString());
    }

    [Fact]
    public async Task PublicUrl_WrongHash_Is404_AndNotCacheable()
    {
        var controller = Controller(new ReaderStub(7, new string('a', 64), new SeriesEmblemFileDto(Png(1, 1), "image/png")));

        Assert.IsType<NotFoundResult>(await controller.Get(7, new string('b', 64), default));
        // 404 не должен унести «вечный» кэш: под этим адресом значок ещё может появиться.
        Assert.Equal("", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void PublicUrl_IsBuiltInOnePlace_AndMatchesTheRoute()
    {
        var dto = new SeriesEmblemDto(7, new string('a', 64), "image/webp", 512, 512, 40_000, DateTime.UtcNow);
        Assert.Equal($"/api/series-emblems/7/{new string('a', 64)}", dto.Url);

        var route = typeof(SeriesEmblemsController).GetMethod(nameof(SeriesEmblemsController.Get))!
            .GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>().Single().Template;
        Assert.Equal("/api/series-emblems/{id:int}/{sha256}", route);
    }
}
