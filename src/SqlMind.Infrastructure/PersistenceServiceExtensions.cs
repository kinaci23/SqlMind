using Microsoft.Extensions.DependencyInjection;
using SqlMind.Core.Interfaces;
using SqlMind.Infrastructure.Persistence;

namespace SqlMind.Infrastructure;

/// <summary>
/// DI registration for repository layer (Day 6).
/// DbContext itself is registered in RagServiceExtensions.AddRagPipeline.
/// </summary>
public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IAnalysisJobRepository,    AnalysisJobRepository>();
        services.AddScoped<IAnalysisResultRepository, AnalysisResultRepository>();
        services.AddScoped<IAuditLogRepository,       AuditLogRepository>();
        return services;
    }
}
