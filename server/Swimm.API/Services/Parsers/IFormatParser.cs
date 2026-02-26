using System.Collections.Generic;
using System.IO;
using Swimm.API.Services.Models;

namespace Swimm.API.Services.Parsers;

public interface IFormatParser
{
    string FormatName { get; }
    IEnumerable<Result> Parse(ParseRequest request);
    string GetDebugLog();

    /// <summary>
    /// Returns a format-specific normative dictionary structure, or null if not supported.
    /// </summary>
    object? ParseNormative(ParseRequest request) => null;
}

public record ParseRequest(
    Stream PrimaryStream,
    string PrimaryFileName,
    Stream? SecondaryStream = null,
    string? SecondaryFileName = null,
    bool IsAward = false,
    string? PoolType = null
);
