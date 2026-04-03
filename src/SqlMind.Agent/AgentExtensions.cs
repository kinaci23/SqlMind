using Microsoft.Extensions.DependencyInjection;

namespace SqlMind.Agent;

public static class AgentExtensions
{
    public static IServiceCollection AddAnalysisOrchestrator(this IServiceCollection services)
    {
        services.AddScoped<AgentOrchestrator>();
        services.AddScoped<AnalysisOrchestrator>();
        return services;
    }
}
