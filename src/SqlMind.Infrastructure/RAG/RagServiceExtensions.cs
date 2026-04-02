using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlMind.Core.Interfaces;
using SqlMind.Infrastructure.Embedding;
using SqlMind.Infrastructure.Persistence;

namespace SqlMind.Infrastructure.RAG;

/// <summary>
/// DI registration for the RAG pipeline:
///   IEmbeddingService → GeminiEmbeddingService
///   IRagService       → RagService
///   SqlMindDbContext  → PostgreSQL (connection string from DATABASE_URL env var / config)
/// </summary>
public static class RagServiceExtensions
{
    /// <summary>
    /// Adds the full RAG stack (DbContext, EmbeddingService, RagService) to the DI container.
    /// Reads connection string from configuration key <c>DATABASE_URL</c> or
    /// <c>ConnectionStrings:DefaultConnection</c>.
    /// </summary>
    public static IServiceCollection AddRagPipeline(this IServiceCollection services)
    {
        // ── DbContext ─────────────────────────────────────────────────────────
        services.AddDbContext<SqlMindDbContext>((sp, options) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();

            // Prefer DATABASE_URL (Docker / env-var convention), fall back to config section
            var connectionString =
                config["DATABASE_URL"]
                ?? config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Database connection string is not configured. " +
                    "Set DATABASE_URL or ConnectionStrings:DefaultConnection.");

            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseVector();
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            });
        });

        // ── Embedding service ─────────────────────────────────────────────────
        services.AddHttpClient<IEmbeddingService, GeminiEmbeddingService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // ── RAG service ───────────────────────────────────────────────────────
        services.AddScoped<IRagService, RagService>();

        return services;
    }
}
