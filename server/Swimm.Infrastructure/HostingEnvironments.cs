using Microsoft.Extensions.Hosting;

namespace Swimm.Infrastructure;

/// <summary>
/// Имена окружений, которые знает Infrastructure. Сами режимы описаны в
/// <c>Swimm.API/Hosting/HostingMode.cs</c>; здесь только то, что нужно регистрации сервисов.
/// </summary>
public static class HostingEnvironments
{
    /// <summary>Локальная админка против прод-БД (решение В17, docs/plans/azure-deploy-plan.md §0-тер-бис).</summary>
    public const string ProdAdmin = "ProdAdmin";

    /// <summary>
    /// Процесс на машине Влада с репозиторием рядом: разработка или ProdAdmin. Например, архив
    /// источников рекордов (относительный путь к <c>!records-sources/</c>) пишется только там.
    /// </summary>
    public static bool IsLocalMachine(IHostEnvironment env) =>
        env.IsDevelopment() || env.IsEnvironment(ProdAdmin);
}
