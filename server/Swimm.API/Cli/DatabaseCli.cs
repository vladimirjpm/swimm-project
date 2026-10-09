using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Cli;

/// <summary>Консольные команды — база: миграции и роли. Перенесены из <c>Program.cs</c> как есть; вызов — <see cref="CliCommands"/>.</summary>
internal static class DatabaseCli
{
    // Миграции применяются явно — либо через `dotnet ef database update`, либо передав флаг
    // `--migrate` при запуске приложения. Авто-миграция при старте отключена: runtime-процесс
    // не должен иметь DDL-прав (подготовка к least-privilege DB role, Phase 3).
    internal static void Migrate(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDbMigrator>().Migrate();
        return;
    }

    // Выдать роль Admin по email. Нужен на первом запуске в проде: после деплоя админка
    // недостижима, пока у кого-то нет роли, а сделать это было нечем, кроме ручного INSERT
    // в прод-БД. docs/plans/azure-deploy-plan.md Б12.
    //   dotnet run -- --grant-admin you@example.com
    // Пользователь должен уже существовать — сначала войди на сайте через Google.
    internal static async Task GrantAdminAsync(WebApplication app, string[] args)
    {
        var emailIndex = Array.IndexOf(args, "--grant-admin") + 1;
        if (emailIndex >= args.Length || args[emailIndex].StartsWith("--"))
        {
            Console.Error.WriteLine("Usage: dotnet run -- --grant-admin <email>");
            Environment.ExitCode = 2;
            return;
        }

        var email = args[emailIndex];
        using var scope = app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminRepository>();

        var user = (await admin.GetUsersAsync())
            .FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            Console.Error.WriteLine($"Пользователь {email} не найден. Сначала войди на сайте — аккаунт создаётся при первом входе.");
            Environment.ExitCode = 1;
            return;
        }

        // RoleId=1 — Admin (сид ролей в SwimmDbContext: 1 Admin, 2 User, 3 Coach).
        var result = await admin.AddRoleAsync(user.Id, roleId: 1);
        switch (result)
        {
            case RoleOperationResult.Ok:
                Console.WriteLine($"Роль Admin выдана: {email} (id {user.Id}). Сессии сброшены — войди заново.");
                break;
            case RoleOperationResult.AlreadyAssigned:
                Console.WriteLine($"У {email} роль Admin уже есть — ничего не менял.");
                break;
            default:
                Console.Error.WriteLine($"Не удалось выдать роль: {result}");
                Environment.ExitCode = 1;
                break;
        }
        return;
    }
}
