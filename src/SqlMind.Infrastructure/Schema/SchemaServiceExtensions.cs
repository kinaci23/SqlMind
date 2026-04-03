using Microsoft.Extensions.DependencyInjection;
using SqlMind.Core.Interfaces;

namespace SqlMind.Infrastructure.Schema;

/// <summary>
/// DI registration for the schema ingestion pipeline.
/// Depends on IRagService and SqlMindDbContext being registered first
/// (AddRagPipeline must be called before this extension).
/// </summary>
public static class SchemaServiceExtensions
{
    public static IServiceCollection AddSchemaIngestion(this IServiceCollection services)
    {
        services.AddScoped<ISchemaIngestionService, SchemaIngestionService>();
        return services;
    }
}
