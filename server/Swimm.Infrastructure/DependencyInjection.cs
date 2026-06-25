using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;

namespace Swimm.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Least-privilege: разные DB-роли для разных путей.
        //   AdminConnection (swimm_rw) — рантайм-запись (auth, admin, импорт).
        //   ReadConnection  (swimm_ro) — публичный read-путь (browsing результатов).
        // Если выделенная строка не задана — graceful fallback на DefaultConnection
        // (одна роль, без изоляции): дев и не настроенные окружения продолжают работать.
        var defaultCs = configuration.GetConnectionString("DefaultConnection");
        var adminCs = configuration.GetConnectionString("AdminConnection") ?? defaultCs;
        var readCs = configuration.GetConnectionString("ReadConnection") ?? defaultCs;

        services.AddDbContext<SwimmDbContext>(options => options.UseNpgsql(adminCs));

        services.AddDbContext<SwimmReadDbContext>(options => options
            .UseNpgsql(readCs)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        // Settings (singleton — in-memory store)
        services.AddSingleton<ISettingsService, AdminSettingsService>();

        // Scoped services
        services.AddScoped<IImportService, JsonImportService>();
        services.AddScoped<ISchemaService, DbSchemaService>();

        // Import job queue — singleton, разделяется между контроллером и BackgroundService
        services.AddSingleton<ImportJobQueue>();
        services.AddSingleton<IImportJobQueue>(sp => sp.GetRequiredService<ImportJobQueue>());

        // Repositories
        services.AddScoped<IResultRepository, ResultRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();

        // Локальный вход (email + пароль)
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>(); // дев: логирует ссылку; прод — SMTP
        services.AddScoped<ILocalAuthService, LocalAuthService>();

        // DB migrator
        services.AddScoped<IDbMigrator, DbMigrator>();

        return services;
    }
}
