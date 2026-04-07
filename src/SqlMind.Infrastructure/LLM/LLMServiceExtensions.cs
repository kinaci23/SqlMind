using Microsoft.Extensions.DependencyInjection;
using SqlMind.Core.Interfaces;

namespace SqlMind.Infrastructure.LLM;

/// <summary>
/// Registers LLM services in the DI container.
/// </summary>
public static class LLMServiceExtensions
{
    /// <summary>
    /// Adds <see cref="GeminiLLMClient"/> as the <see cref="ILLMClient"/> implementation,
    /// using the HttpClient factory for proper lifetime management.
    /// </summary>
    public static IServiceCollection AddGeminiLLMClient(this IServiceCollection services)
    {
        services
            .AddHttpClient<ILLMClient, GeminiLLMClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(120);
            });

        return services;
    }
}
