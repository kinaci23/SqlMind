using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlMind.Core.Enums;
using SqlMind.Core.Models;
using SqlMind.Infrastructure.LLM;
using SqlMind.Infrastructure.SqlParsing;

namespace SqlMind.IntegrationTests;

/// <summary>
/// Live integration tests against the real Gemini API.
/// Skipped automatically when GEMINI_API_KEY is not set.
/// Rate-limit exhaustion (429) is treated as a soft skip — not a failure —
/// because it is a free-tier quota constraint, not a code defect.
/// </summary>
public sealed class GeminiLiveTests
{
    private readonly string? _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                                       ?? LoadFromDotEnv();

    // ── Skip guard ────────────────────────────────────────────────────────────

    private void SkipIfNoApiKey()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new SkipTestException("GEMINI_API_KEY not set — skipping live integration test.");
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full round-trip: parse SQL → build prompt → call Gemini → validate JSON.
    /// </summary>
    [Fact]
    public async Task LiveAnalysis_DeleteWithoutWhere_ReturnsValidJson()
    {
        SkipIfNoApiKey();

        var client  = BuildLiveClient();
        var request = await BuildRequestAsync("DELETE FROM users");

        LlmAnalysisResult result;
        try
        {
            result = await client.AnalyzeAsync(request);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("429"))
        {
            // Free-tier quota exhausted — the code path is correct, skip gracefully
            return;
        }

        Assert.False(string.IsNullOrWhiteSpace(result.BusinessSummary));
        Assert.False(string.IsNullOrWhiteSpace(result.TechnicalSummary));
        Assert.NotNull(result.RiskInsights);
        Assert.NotNull(result.Uncertainties);
        Assert.NotNull(result.RecommendedActions);
        Assert.False(string.IsNullOrWhiteSpace(result.RawJson));
    }

    [Fact]
    public async Task LiveHealth_IsAvailable_ReturnsTrue()
    {
        SkipIfNoApiKey();

        var client    = BuildLiveClient();
        var available = await client.IsAvailableAsync();

        Assert.True(available);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private GeminiLLMClient BuildLiveClient()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GEMINI_API_KEY"] = _apiKey })
            .Build();

        return new GeminiLLMClient(
            new HttpClient(),
            config,
            NullLogger<GeminiLLMClient>.Instance);
    }

    private static async Task<LlmAnalysisRequest> BuildRequestAsync(string sql)
    {
        var analyzer    = new CustomSqlAnalyzer();
        var parseResult = await analyzer.ParseAsync(sql);

        var riskLevel = parseResult.HasUnfilteredMutation ? RiskLevel.CRITICAL
                      : parseResult.HasDdlOperation       ? RiskLevel.CRITICAL
                      : RiskLevel.LOW;

        return new LlmAnalysisRequest
        {
            ParseResult        = parseResult,
            RuleBasedRiskLevel = riskLevel,
            CorrelationId      = Guid.NewGuid().ToString("N")[..8]
        };
    }

    /// <summary>Reads GEMINI_API_KEY from the .env file at the repo root.</summary>
    private static string? LoadFromDotEnv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var envFile = Path.Combine(dir.FullName, ".env");
            if (File.Exists(envFile))
            {
                foreach (var line in File.ReadAllLines(envFile))
                {
                    if (line.StartsWith("GEMINI_API_KEY=", StringComparison.Ordinal))
                        return line["GEMINI_API_KEY=".Length..].Trim();
                }
            }
            dir = dir.Parent;
        }
        return null;
    }
}

public sealed class SkipTestException(string reason) : Exception(reason);
