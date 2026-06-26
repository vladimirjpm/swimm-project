using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

public class Argon2PasswordHasherTests
{
    private readonly Argon2PasswordHasher _hasher = new();

    // ── Hash format ──────────────────────────────────────────────────────────

    [Fact]
    public void Hash_Returns_Argon2idSelfDescribingFormat()
    {
        var hash = _hasher.Hash("SomePassword1!");

        // argon2id $ v=1 $ m=...,t=...,p=... $ <saltB64> $ <hashB64>
        var parts = hash.Split('$');
        Assert.Equal(5, parts.Length);
        Assert.Equal("argon2id", parts[0]);
        Assert.Equal("v=1", parts[1]);
        Assert.Contains("m=", parts[2]);
        Assert.Contains("t=", parts[2]);
        Assert.Contains("p=", parts[2]);
    }

    // ── Verify: правильный пароль ────────────────────────────────────────────

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = _hasher.Hash("MySecret99!");

        Assert.True(_hasher.Verify("MySecret99!", hash));
    }

    // ── Verify: неверный пароль ──────────────────────────────────────────────

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("MySecret99!");

        Assert.False(_hasher.Verify("WrongPassword!", hash));
    }

    // ── Verify: битая строка не падает, возвращает false ────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("notahash")]
    [InlineData("a$b$c")]
    [InlineData("argon2id$v=1$m=1,t=1,p=1$badsalt!!$badhash!!")]
    public void Verify_MalformedHash_ReturnsFalse(string malformed)
    {
        Assert.False(_hasher.Verify("SomePassword1!", malformed));
    }

    // ── Два вызова Hash — разные соли ────────────────────────────────────────

    [Fact]
    public void Hash_TwoCalls_ProduceDifferentHashes()
    {
        var hash1 = _hasher.Hash("SamePassword1!");
        var hash2 = _hasher.Hash("SamePassword1!");

        Assert.NotEqual(hash1, hash2);
    }
}
