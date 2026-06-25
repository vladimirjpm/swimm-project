using Microsoft.Extensions.DependencyInjection;

namespace Swimm.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
