using Swimm.Application.Validation;
using Xunit;

namespace Swimm.Tests;

public class MediaUrlValidatorTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123DEF")]
    [InlineData("https://youtu.be/abc123DEF")]
    [InlineData("https://www.youtube.com/shorts/abc123DEF")]
    [InlineData("https://www.youtube.com/embed/abc123DEF")]
    public void Youtube_ValidLinks_Accepted(string url)
    {
        var ok = MediaUrlValidator.TryValidate("video", "youtube", url, out var error);

        Assert.True(ok, error);
        Assert.Null(error);
    }

    [Fact]
    public void Youtube_GarbageUrl_Rejected()
    {
        var ok = MediaUrlValidator.TryValidate("video", "youtube", "https://example.com/not-a-video", out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Vimeo_ValidLink_Accepted()
    {
        var ok = MediaUrlValidator.TryValidate("video", "vimeo", "https://vimeo.com/123456789", out var error);

        Assert.True(ok, error);
    }

    [Fact]
    public void Vimeo_GarbageUrl_Rejected()
    {
        var ok = MediaUrlValidator.TryValidate("video", "vimeo", "https://vimeo.com/not-a-number", out var error);

        Assert.False(ok);
    }

    [Fact]
    public void HttpScheme_Rejected()
    {
        var ok = MediaUrlValidator.TryValidate("video", "youtube", "http://www.youtube.com/watch?v=abc123", out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void JavascriptScheme_Rejected()
    {
        var ok = MediaUrlValidator.TryValidate("video", "other", "javascript:alert(1)", out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Other_HttpsUrl_Accepted()
    {
        var ok = MediaUrlValidator.TryValidate("video", "other", "https://example.com/clip.mp4", out var error);

        Assert.True(ok, error);
    }

    [Fact]
    public void Other_ImageMediaType_Accepted()
    {
        var ok = MediaUrlValidator.TryValidate("image", "other", "https://example.com/photo.jpg", out var error);

        Assert.True(ok, error);
    }

    [Fact]
    public void ImageMediaType_WithYoutubeSource_Rejected()
    {
        var ok = MediaUrlValidator.TryValidate("image", "youtube", "https://www.youtube.com/watch?v=abc123", out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void UnknownSourceType_Rejected()
    {
        var ok = MediaUrlValidator.TryValidate("video", "tiktok", "https://example.com/x", out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void UnknownMediaType_Rejected()
    {
        var ok = MediaUrlValidator.TryValidate("audio", "other", "https://example.com/x", out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TooLongUrl_Rejected()
    {
        var url = "https://example.com/" + new string('a', 1000);
        var ok = MediaUrlValidator.TryValidate("video", "other", url, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
    // ── DedupKey: запрет дубля ссылки (docs/plans/media-duplicates-handoff.md) ──

    [Theory]
    [InlineData("https://drive.google.com/file/d/1RH1YV22pp1ubIjvDlbxwSNsJIWOQVYOL/view")]
    [InlineData("https://drive.google.com/file/d/1RH1YV22pp1ubIjvDlbxwSNsJIWOQVYOL/view?usp=sharing")]
    [InlineData("https://drive.google.com/file/u/0/d/1RH1YV22pp1ubIjvDlbxwSNsJIWOQVYOL/view")]
    [InlineData("https://drive.google.com/open?id=1RH1YV22pp1ubIjvDlbxwSNsJIWOQVYOL")]
    [InlineData("https://drive.google.com/uc?export=view&id=1RH1YV22pp1ubIjvDlbxwSNsJIWOQVYOL")]
    [InlineData("  https://drive.google.com/file/d/1RH1YV22pp1ubIjvDlbxwSNsJIWOQVYOL/view  ")]
    public void DedupKey_SameDriveFile_SameKey(string url)
    {
        Assert.Equal("gdrive:1RH1YV22pp1ubIjvDlbxwSNsJIWOQVYOL", MediaUrlValidator.DedupKey(url));
    }

    [Fact]
    public void DedupKey_DifferentDriveFiles_DifferentKeys()
    {
        Assert.NotEqual(
            MediaUrlValidator.DedupKey("https://drive.google.com/file/d/AAA111/view"),
            MediaUrlValidator.DedupKey("https://drive.google.com/file/d/BBB222/view"));
    }

    [Fact]
    public void DedupKey_OtherLink_OnlyTrimmed()
    {
        Assert.Equal("https://youtu.be/abc123", MediaUrlValidator.DedupKey(" https://youtu.be/abc123 "));
        Assert.NotEqual(
            MediaUrlValidator.DedupKey("https://youtu.be/abc123"),
            MediaUrlValidator.DedupKey("https://youtu.be/abc124"));
    }
}
