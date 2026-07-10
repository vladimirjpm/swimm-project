using Microsoft.Extensions.DependencyInjection;
using Swimm.Application.Abstractions;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.IsrOrg;
using Swimm.Parsing.Parsers.IsrOrgAgeRecords;
using Swimm.Parsing.Parsers.IsrOrgMastersRecords;
using Swimm.Parsing.Parsers.WorldRecords;
using Swimm.Parsing.RecordSources;

namespace Swimm.Parsing;

public static class DependencyInjection
{
    /// <summary>
    /// Регистрирует парсеры протоколов, IResultSourceProvider и IRecordSourceProvider
    /// (этап 2.6 — обновление рекордов из URL-источников). Singleton: у парсеров нет
    /// instance-состояния (debug-лог — статический и сбрасывается на каждый Parse, т.е. он
    /// и так не переживёт конкурентные прогоны — их сериализует фоновая очередь импорта).
    /// Парсеры регистрируются и как конкретный тип (для record-провайдеров), и как
    /// IFormatParser (для PdfResultSourceProvider) — один и тот же singleton-экземпляр.
    /// </summary>
    public static IServiceCollection AddParsing(this IServiceCollection services)
    {
        services.AddSingleton<IsrOrgParser>();
        services.AddSingleton<IFormatParser>(sp => sp.GetRequiredService<IsrOrgParser>());

        services.AddSingleton<IsrOrgAgeRecordsParser>();
        services.AddSingleton<IFormatParser>(sp => sp.GetRequiredService<IsrOrgAgeRecordsParser>());

        services.AddSingleton<IsrOrgMastersRecordsParser>();
        services.AddSingleton<IFormatParser>(sp => sp.GetRequiredService<IsrOrgMastersRecordsParser>());

        services.AddSingleton<WorldRecordsParser>();
        services.AddSingleton<IFormatParser>(sp => sp.GetRequiredService<WorldRecordsParser>());

        services.AddHttpClient();

        services.AddSingleton<IResultSourceProvider, PdfResultSourceProvider>();

        services.AddSingleton<IRecordSourceProvider, WorldRecordsSourceProvider>();
        services.AddSingleton<IRecordSourceProvider, IsrOrgAgeRecordsSourceProvider>();
        services.AddSingleton<IRecordSourceProvider, IsrOrgMastersRecordsSourceProvider>();

        return services;
    }
}
