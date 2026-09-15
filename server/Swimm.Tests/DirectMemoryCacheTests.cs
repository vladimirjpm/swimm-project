using System.Text.RegularExpressions;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Правило №1 кэша (docs/plans/cache-tags-plan.md §4): кэш данных — только через
/// <c>ICacheService</c>. Прямой <c>IMemoryCache</c> второй экземпляр API не увидит, а сброс по
/// меткам (К4) до него не дойдёт — такой кэш врёт до конца своего TTL после любой правки.
///
/// Места, где <c>IMemoryCache</c> законен, — закрытый список <see cref="Allowed"/> с причиной:
/// сама реализация кэша, состояние сценариев «превью → применить» (это не кэш данных), внешний
/// источник, сводки админки. Новое место роняет тест, исчезнувшее — тоже: список не врёт.
///
/// Смотрит ИСХОДНИКИ, а не сборки: тесты сознательно не собирают Swimm.API (сборка не упирается
/// в лок запущенного API), а контроллеры — как раз частое место для «быстрого» кэша (К5).
/// ⚠ Состояние сценариев из списка при нескольких экземплярах API потеряется на соседнем
/// экземпляре — это часть фазы масштабирования вместе с К6 (docs/plans/cache-tags-plan.md §6).
/// </summary>
public class DirectMemoryCacheTests
{
    /// <summary>Файл (от папки server/) → почему ему можно.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["Swimm.Infrastructure/Services/MemoryCacheService.cs"] =
            "сама реализация ICacheService",
        ["Swimm.Infrastructure/DependencyInjection.cs"] =
            "регистрация: отдаёт IMemoryCache настройкам (AdminSettingsService)",
        ["Swimm.Infrastructure/Services/AdminSettingsService.cs"] =
            "сбрасывает кэш схемы БД (DbSchemaService) при смене его настроек",
        ["Swimm.Infrastructure/Services/DbSchemaService.cs"] =
            "схема БД для админки — не данные витрины; сбрасывается сменой настроек схемы",
        ["Swimm.Infrastructure/Services/DashboardStatusService.cs"] =
            "сводка дашборда админки: 2 минуты без сброса — сознательно",
        ["Swimm.Infrastructure/Services/PreviewRecordCheckService.cs"] =
            "участники соревнования с сайта loglig — внешний источник, не наша база",
        ["Swimm.Infrastructure/Services/RecordDiffService.cs"] =
            "состояние сценария «дифф рекордов → применить» (10 мин), не кэш данных",
        ["Swimm.Infrastructure/Services/DiscoveryPreviewService.cs"] =
            "состояние сценария «превью Discovery → импорт», не кэш данных",
        ["Swimm.API/Controllers/AdminController.cs"] =
            "состояние сценария «превью PDF → импорт» (15 мин), не кэш данных",
    };

    private static readonly string[] Projects =
        ["Swimm.API", "Swimm.Application", "Swimm.Domain", "Swimm.Infrastructure", "Swimm.Parsing"];

    // IMemoryCache и конкретный MemoryCache; MemoryCacheService и AddMemoryCache — нет.
    private static readonly Regex MemoryCacheType = new(@"\bI?MemoryCache\b", RegexOptions.Compiled);

    [Fact]
    public void DirectMemoryCache_OnlyInTheAllowedPlaces()
    {
        var users = FilesUsingMemoryCache();

        // Сторож самого теста: сканер, который тихо перестал находить файлы, зеленеет вечно.
        Assert.Contains("Swimm.Infrastructure/Services/MemoryCacheService.cs", users);

        var unexpected = users.Where(f => !Allowed.ContainsKey(f)).ToList();
        Assert.True(unexpected.Count == 0,
            "Прямой IMemoryCache мимо ICacheService: " + string.Join(", ", unexpected) + ". " +
            "Кэш данных — только через ICacheService.GetOrCreateAsync: метки и сброс он получит сам. " +
            "Если это не кэш данных (состояние сценария, внешний источник) — строка в Allowed с причиной.");

        var stale = Allowed.Keys.Where(f => !users.Contains(f)).ToList();
        Assert.True(stale.Count == 0,
            "Эти файлы больше не держат IMemoryCache — уберите их из Allowed: " + string.Join(", ", stale));
    }

    private static HashSet<string> FilesUsingMemoryCache()
    {
        var server = ServerDirectory();
        var users = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in Projects)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(server, project), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(server, file).Replace('\\', '/');
                if (relative.Contains("/bin/") || relative.Contains("/obj/")) continue;

                // Упоминание в комментарии — не использование.
                if (File.ReadLines(file).Any(line =>
                        !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                        && MemoryCacheType.IsMatch(line)))
                    users.Add(relative);
            }
        }
        return users;
    }

    /// <summary>Папка server/ — ближайшая сверху от сборки тестов, где лежит Swimm.sln.</summary>
    private static string ServerDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Swimm.sln"))) return dir.FullName;

        throw new InvalidOperationException(
            $"Не нашёл Swimm.sln выше {AppContext.BaseDirectory} — тест смотрит исходники и без них не работает.");
    }
}
