using Swimm.Application.Abstractions;

namespace Swimm.API.Cli;

/// <summary>Консольные команды — сиды тестовых и исторических данных. Перенесены из <c>Program.cs</c> как есть; вызов — <see cref="CliCommands"/>.</summary>
internal static class SeedCli
{
    // Одноразовый сид тренировок «Дельфин-мастерс» (соревнования не трогаем — они уже в БД):
    //   dotnet run -- --seed-dolphin-training <json> <canon.csv> --group <hubGroupId> [--force]
    internal static async Task SeedDolphinTrainingAsync(WebApplication app, string[] args)
    {
        string ArgAfter(string flag, string what)
        {
            var i = Array.IndexOf(args, flag) + 1;
            if (i <= 0 || i >= args.Length || args[i].StartsWith("--"))
            {
                Console.Error.WriteLine(
                    "Usage: dotnet run -- --seed-dolphin-training <json> <canon.csv> --group <hubGroupId> [--force]");
                Environment.Exit(1);
            }
            return args[i];
        }

        var jsonPath = args[Array.IndexOf(args, "--seed-dolphin-training") + 1];
        var csvPath = args[Array.IndexOf(args, "--seed-dolphin-training") + 2];
        var groupId = int.Parse(ArgAfter("--group", "hubGroupId"));

        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IDolphinTrainingSeeder>();
        var seedLog = await seeder.SeedAsync(jsonPath, csvPath, groupId, args.Contains("--force"));
        foreach (var line in seedLog)
            Console.WriteLine(line);
        return;
    }

    // Тестовые персонажи utest-* и тестовые группы (docs/plans/test-personas-plan.md, этап 2):
    //   dotnet run -- --seed-personas [--reset]
    // Идемпотентно; --reset сначала удаляет всех utest-пользователей и их тест-группы.
    // Только Development: сидер заводит аккаунты, на живой базе им не место.
    internal static async Task SeedPersonasAsync(WebApplication app, string[] args)
    {
        if (!app.Environment.IsDevelopment())
        {
            Console.Error.WriteLine("--seed-personas работает только в Development (ASPNETCORE_ENVIRONMENT=Development).");
            Environment.Exit(1);
            return;
        }

        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IPersonaSeeder>();
        foreach (var line in await seeder.SeedAsync(args.Contains("--reset")))
            Console.WriteLine(line);
        return;
    }
}
