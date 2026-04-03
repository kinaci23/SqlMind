using Microsoft.Extensions.DependencyInjection;
using SqlMind.Core.Interfaces;
using SqlMind.Infrastructure.Risk;
using SqlMind.Infrastructure.SqlParsing;

namespace SqlMind.Infrastructure;

public static class SqlParsingServiceExtensions
{
    public static IServiceCollection AddSqlAnalysis(this IServiceCollection services)
    {
        services.AddSingleton<ISqlAnalyzer,    CustomSqlAnalyzer>();
        services.AddSingleton<IRiskEvaluator,  RuleBasedRiskEvaluator>();
        return services;
    }
}
