using Microsoft.Extensions.DependencyInjection;
using SqlMind.Core.Interfaces;
using SqlMind.Infrastructure.Policy;
using SqlMind.Infrastructure.Tools;

namespace SqlMind.Infrastructure;

/// <summary>
/// DI registration for Day-5 agent components:
/// PolicyEngine, ITool implementations, and ToolExecutor.
/// </summary>
public static class AgentServiceExtensions
{
    public static IServiceCollection AddAgentServices(this IServiceCollection services)
    {
        // Policy Engine
        services.AddSingleton<IPolicyEngine, PolicyEngine>();

        // Tools — registered individually so DI can inject IEnumerable<ITool> into ToolExecutor
        services.AddSingleton<ITool, CreateTicketTool>();
        services.AddSingleton<ITool, SendNotificationTool>();
        services.AddSingleton<ITool, RequestApprovalTool>();

        // Tool Executor
        services.AddSingleton<IToolExecutor, ToolExecutor>();

        return services;
    }
}
