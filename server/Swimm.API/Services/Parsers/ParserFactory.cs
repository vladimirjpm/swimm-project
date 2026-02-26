using System;
using System.Collections.Generic;
using System.Linq;
using Swimm.API.Services.Parsers.IsrOrg;
using Swimm.API.Services.Parsers.IsrOrgAgeRecords;

namespace Swimm.API.Services.Parsers;

public static class ParserFactory
{
    private static readonly Dictionary<string, IFormatParser> Parsers = new(StringComparer.OrdinalIgnoreCase);

    static ParserFactory()
    {
        Register(new IsrOrgParser());
        Register(new IsrOrgAgeRecordsParser());
    }

    public static void Register(IFormatParser parser)
    {
        Parsers[parser.FormatName] = parser;
    }

    public static IFormatParser Get(string formatName)
    {
        if (Parsers.TryGetValue(formatName, out var parser))
            return parser;

        var available = string.Join(", ", Parsers.Keys);
        throw new InvalidOperationException(
            $"Unknown format '{formatName}'. Available: {available}");
    }

    public static IReadOnlyList<string> AvailableFormats =>
        Parsers.Keys.ToList().AsReadOnly();
}
