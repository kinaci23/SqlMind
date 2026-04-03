using Microsoft.Extensions.DependencyInjection;
using SqlMind.Core.Interfaces;

namespace SqlMind.Infrastructure.Jobs;

public static class JobServiceExtensions
{
    public static IServiceCollection AddHangfireJobService(this IServiceCollection services)
    {
        services.AddSingleton<IBackgroundJobService, HangfireJobService>();
        return services;
    }
}
