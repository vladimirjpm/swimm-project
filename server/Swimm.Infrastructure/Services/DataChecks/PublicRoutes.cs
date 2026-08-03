namespace Swimm.Infrastructure.Services.DataChecks;

/// <summary>
/// Пути публичного сайта для находок реестра — «пойти посмотреть глазами».
///
/// ⚠ Это ЧЕТВЁРТОЕ зеркало контракта чистых URL. Остальные три:
/// <c>client/src/utils/routes.ts</c> (истина), <c>cleanUrlRewrite</c> в
/// <c>client/vite.config.js</c> (dev) и rewrite-middleware в <c>Program.cs</c> (прод).
/// Меняешь путь — правь все четыре.
///
/// Только относительные пути: базу подставляет страница (<c>PublicSite:BaseUrl</c>), иначе
/// dev-адрес вида localhost:5173 осел бы в БД и уехал в прод. И только идентичность ресурса —
/// вид (tab/filter/swim) в путь не идёт, это правило контракта.
/// </summary>
internal static class PublicRoutes
{
    public static string Competition(int id) => $"/competitions/{id}";
    public static string Swimmer(int id) => $"/swimmers/{id}";
    public static string Club(int id) => $"/clubs/{id}";
}
