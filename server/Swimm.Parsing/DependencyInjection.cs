using Microsoft.Extensions.DependencyInjection;
using Swimm.Application.Abstractions;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.IsrOrg;
using Swimm.Parsing.Parsers.IsrOrgAgeRecords;
using Swimm.Parsing.Parsers.IsrOrgMastersRecords;
using Swimm.Parsing.Parsers.WorldRecords;

namespace Swimm.Parsing;

public static class DependencyInjection
{
    /// <summary>
    /// Регистрирует парсеры протоколов и IResultSourceProvider.
    /// Singleton: у парсеров нет instance-состояния (debug-лог — статический и сбрасывается
    /// на каждый Parse, т.е. он и так не переживёт конкурентные прогоны — их сериализует
    /// фоновая очередь импорта).
    /// </summary>
    public static IServiceCollection AddParsing(this IServiceCollection services)
    {
        services.AddSingleton<IFormatParser, IsrOrgParser>();
        services.AddSingleton<IFormatParser, IsrOrgAgeRecordsParser>();
        services.AddSingleton<IFormatParser, IsrOrgMastersRecordsParser>();
        services.AddSingleton<IFormatParser, WorldRecordsParser>();

        services.AddSingleton<IResultSourceProvider, PdfResultSourceProvider>();

        return services;
    }
}
