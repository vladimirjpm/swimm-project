using Npgsql;

namespace Swimm.Infrastructure.Data;

/// <summary>
/// Спаны Npgsql для телеметрии (docs/plans/application-insights-plan.md, А2.7). Сам спан запроса —
/// длительность и текст SQL с плейсхолдерами — нужен. А событие <c>received-first-response</c>,
/// которое Npgsql вешает на каждый спан, экспортёр Azure Monitor превращает в ОТДЕЛЬНУЮ строку
/// <c>AppTraces</c> (уровень Verbose): по строке лога на каждый SQL-запрос, пользы ноль, а
/// дневной лимит приёма 0,15 ГБ. Найдено проверкой на живой рабочей области 17.11.
///
/// Без слушателя спанов (телеметрия выключена — разработка, тесты) ничего не меняет.
/// </summary>
internal static class NpgsqlTracingDefaults
{
    public static void Apply(NpgsqlDataSourceBuilder dataSource) =>
        dataSource.ConfigureTracing(t => t.EnableFirstResponseEvent(false));
}
