using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// И-46: консольный <c>--repull</c> парсил только HE и терял EN-имена — импорт без фоллбека по
/// английскому имени завёл двойника #59991. Теперь кнопка, пачка и консоль разбирают протокол
/// одним <see cref="ProtocolPairParser"/>: пара HE + EN, не склеилась — откат на HE.
/// </summary>
public class ProtocolPairParserTests
{
    private sealed class FakeSource(bool pairFails) : IResultSourceProvider
    {
        public List<ResultSourceRequest> Calls { get; } = [];
        public string SourceName => "fake";
        public IReadOnlyList<string> AvailableFormats => ["IsrOrg"];

        public Task<ParsedCompetition> ParseAsync(ResultSourceRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            if (pairFails && request.SecondaryStream != null)
                throw new InvalidOperationException("pair mismatch");
            return Task.FromResult(new ParsedCompetition
            {
                Format = request.Format,
                ResultsJson = request.SecondaryStream != null ? "pair" : "he",
            });
        }
    }

    private static readonly byte[] He = [1], En = [2];

    [Fact]
    public async Task BothFiles_ParsedAsPair()
    {
        var source = new FakeSource(pairFails: false);
        var r = await ProtocolPairParser.ParseAsync(source, He, "x-he.pdf", En, "x-en.pdf");

        Assert.True(r.UsedEn);
        Assert.Null(r.PairError);
        Assert.Equal("pair", r.Parsed.ResultsJson);
        var call = Assert.Single(source.Calls);
        Assert.Equal("x-he.pdf", call.PrimaryFileName);
        Assert.Equal("x-en.pdf", call.SecondaryFileName);
        Assert.Equal("he", call.Language);
    }

    [Fact]
    public async Task PairMismatch_FallsBackToHeOnly()
    {
        var source = new FakeSource(pairFails: true);
        var r = await ProtocolPairParser.ParseAsync(source, He, "x-he.pdf", En, "x-en.pdf");

        Assert.False(r.UsedEn);
        Assert.Equal("pair mismatch", r.PairError);
        Assert.Equal("he", r.Parsed.ResultsJson);
        Assert.Equal(2, source.Calls.Count);
        Assert.Null(source.Calls[1].SecondaryStream);
    }

    [Fact]
    public async Task NoEnFile_HeOnly()
    {
        var source = new FakeSource(pairFails: false);
        var r = await ProtocolPairParser.ParseAsync(source, He, "x-he.pdf", null, null);

        Assert.False(r.UsedEn);
        Assert.Null(r.PairError);
        Assert.Equal("he", r.Parsed.ResultsJson);
        Assert.Null(Assert.Single(source.Calls).SecondaryStream);
    }
}
