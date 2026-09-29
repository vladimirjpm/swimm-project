using System.Text.RegularExpressions;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// И16 (docs/data-integrity.md): тренировочные времена не выходят за аудиторию группы. Их читают
/// только управляющие и активные участники-аккаунты группы занятия; страница пловца, рекорды,
/// season best и рейтинги читают только <c>Results</c>. Правило держится на том, что
/// <c>Sys_TrainingResults</c> трогают считаные места, — этот тест держит список.
///
/// Входов в таблицу три, и сторожатся все: DbSet <c>TrainingResults</c>, DbSet
/// <c>TrainingSessions</c> (от сессии времена достаются навигацией <c>Session.Results</c>, а её по
/// имени не отличить от таблицы обычных результатов) и сырой SQL с именем таблицы. Новое место
/// роняет тест, исчезнувшее — тоже: список не врёт. Смотрит ИСХОДНИКИ, как
/// <see cref="DirectMemoryCacheTests"/> (и с той же оговоркой: при <c>--artifacts-path</c> вне
/// репозитория исходников не найдёт).
/// </summary>
public class TrainingResultsPrivacyTests
{
    /// <summary>Файл (от папки server/) → почему ему можно.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["Swimm.Infrastructure/Data/SwimmDbContext.cs"] =
            "объявление DbSet и маппинг таблиц",
        ["Swimm.Infrastructure/Repositories/HubGroupTrainingRepository.cs"] =
            "ЕДИНСТВЕННОЕ чтение времён для показа: таб тренировок группы; аудиторию (управляющие + активные " +
            "участники) проверяет вызывающий до вызова",
        ["Swimm.Infrastructure/Services/DolphinTrainingSeeder.cs"] =
            "сидер тренировок Дельфина — запись",
        ["Swimm.Infrastructure/Services/PersonaSeeder.cs"] =
            "сидер тестовых персонажей — запись",
        ["Swimm.Infrastructure/Services/HubGroupAdminService.cs"] =
            "перечень потерь при удалении группы — только ЧИСЛО сессий и результатов",
        ["Swimm.Infrastructure/Services/HubGroupMediaService.cs"] =
            "привязка медиа к занятию: «эта сессия — этой группы», только id сессии",
        ["Swimm.Infrastructure/Services/SwimmerMergeService.cs"] =
            "склейка пловцов переносит тренировочные строки на оставшегося",
        ["Swimm.Infrastructure/Services/SwimmerDedupService.cs"] =
            "«есть ли у пловца данные» (Any) — пустого не предлагать к удалению",
        ["Swimm.Infrastructure/Services/JsonImportService.cs"] =
            "«есть ли у пловца данные» (Any) при чистке пловцов-сирот после переимпорта",
    };

    private static readonly string[] Projects =
        ["Swimm.API", "Swimm.Application", "Swimm.Domain", "Swimm.Infrastructure", "Swimm.Parsing"];

    // DbSet-ы (db.TrainingResults / db.TrainingSessions), Set<…>() и сырой SQL с именем таблицы.
    private static readonly Regex TrainingAccess = new(
        @"\.Training(Results|Sessions)\b|Set<Training(Result|Session)>|""[^""]*Sys_Training(Results|Sessions)",
        RegexOptions.Compiled);

    [Fact]
    public void TrainingResults_ReadOnlyInTheAllowedPlaces()
    {
        var users = FilesTouchingTrainingTables();

        // Сторож самого теста: сканер, который тихо перестал находить файлы, зеленеет вечно.
        Assert.Contains("Swimm.Infrastructure/Repositories/HubGroupTrainingRepository.cs", users);

        var unexpected = users.Where(f => !Allowed.ContainsKey(f)).ToList();
        Assert.True(unexpected.Count == 0,
            "Тренировочные таблицы (Sys_TrainingResults/Sys_TrainingSessions) трогает новое место: " +
            string.Join(", ", unexpected) + ". И16: тренировочные времена видит только аудитория группы — " +
            "страница пловца, рекорды, season best и рейтинги читают только Results. Показать времена — " +
            "через IHubGroupTrainingRepository; служебная запись/подсчёт без времён — строка в Allowed с причиной.");

        var stale = Allowed.Keys.Where(f => !users.Contains(f)).ToList();
        Assert.True(stale.Count == 0,
            "Эти файлы больше не трогают тренировочные таблицы — уберите их из Allowed: " + string.Join(", ", stale));
    }

    private static HashSet<string> FilesTouchingTrainingTables()
    {
        var server = ServerDirectory();
        var users = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in Projects)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(server, project), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(server, file).Replace('\\', '/');
                // Миграции — история схемы, не чтение данных.
                if (relative.Contains("/bin/") || relative.Contains("/obj/") || relative.Contains("/Migrations/")) continue;

                // Упоминание в комментарии — не использование.
                if (File.ReadLines(file).Any(line =>
                        !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                        && TrainingAccess.IsMatch(line)))
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
