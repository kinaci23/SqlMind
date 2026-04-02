using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlMind.Core.Interfaces;

namespace SqlMind.Infrastructure.Embedding;

/// <summary>
/// IEmbeddingService implementation backed by the Gemini gemini-embedding-001 REST API.
/// Produces 3072-dimensional float vectors.
/// Retries up to 3 times with exponential back-off on 429 / 5xx responses.
/// </summary>
public sealed class GeminiEmbeddingService : IEmbeddingService
{
    // ── Constants ─────────────────────────────────────────────────────────────
    private const string EmbeddingModel = "gemini-embedding-001";
    private const string BaseUrl        = "https://generativelanguage.googleapis.com/v1beta/models";
    private const int    ExpectedDim    = 3072;
    private const int    MaxRetries     = 3;

    private readonly HttpClient _http;
    private readonly string     _apiKey;
    private readonly ILogger<GeminiEmbeddingService> _logger;

    public int Dimensions => ExpectedDim;

    public GeminiEmbeddingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiEmbeddingService> logger)
    {
        _http   = httpClient;
        _logger = logger;
        _apiKey = configuration["GEMINI_API_KEY"]
                  ?? throw new InvalidOperationException(
                      "GEMINI_API_KEY is not configured.");
    }

    // ── IEmbeddingService ─────────────────────────────────────────────────────

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var url     = $"{BaseUrl}/{EmbeddingModel}:embedContent?key={_apiKey}";
        var payload = BuildPayload(text);

        var values = await ExecuteWithRetryAsync(url, payload, ct);
        ValidateDimensions(values);
        return values;
    }

    public async Task<List<float[]>> EmbedBatchAsync(List<string> texts, CancellationToken ct = default)
    {
        if (texts is null || texts.Count == 0)
            return [];

        // Gemini embedding API does not expose a true batch endpoint in v1beta;
        // we fan-out individually but honour cancellation between calls.
        var results = new List<float[]>(texts.Count);
        foreach (var text in texts)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await EmbedAsync(text, ct));
        }
        return results;
    }

    // ── Request / response helpers ────────────────────────────────────────────

    private static object BuildPayload(string text) => new
    {
        model   = $"models/{EmbeddingModel}",
        content = new
        {
            parts = new[] { new { text } }
        }
    };

    private async Task<float[]> ExecuteWithRetryAsync(
        string url, object payload, CancellationToken ct)
    {
        var attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                var response = await _http.PostAsJsonAsync(url, payload, ct);

                if (response.IsSuccessStatusCode)
                    return await ParseEmbeddingResponse(response, ct);

                if (attempt < MaxRetries &&
                    (response.StatusCode == HttpStatusCode.TooManyRequests ||
                     (int)response.StatusCode >= 500))
                {
                    var delay = response.StatusCode == HttpStatusCode.TooManyRequests
                        ? TimeSpan.FromSeconds(Math.Pow(2, attempt) * 10)
                        : TimeSpan.FromSeconds(Math.Pow(2, attempt));

                    _logger.LogWarning(
                        "Gemini Embedding {StatusCode}. Retry {Attempt}/{Max} in {Delay}s.",
                        (int)response.StatusCode, attempt, MaxRetries, delay.TotalSeconds);

                    await Task.Delay(delay, ct);
                    continue;
                }

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"Gemini Embedding API error {(int)response.StatusCode}: {errorBody}");
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(
                    "Gemini Embedding HTTP error. Retry {Attempt}/{Max} in {Delay}s.",
                    attempt, MaxRetries, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    private static async Task<float[]> ParseEmbeddingResponse(
        HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Gemini Embedding returned non-JSON: {body[..Math.Min(200, body.Length)]}", ex);
        }

        using (doc)
        {
            // Response shape: { "embedding": { "values": [0.1, 0.2, ...] } }
            if (!doc.RootElement.TryGetProperty("embedding", out var embedding))
                throw new InvalidOperationException(
                    "Gemini Embedding response missing 'embedding' field.");

            if (!embedding.TryGetProperty("values", out var valuesEl))
                throw new InvalidOperationException(
                    "Gemini Embedding response missing 'embedding.values' field.");

            return valuesEl.EnumerateArray()
                           .Select(e => e.GetSingle())
                           .ToArray();
        }
    }

    private static void ValidateDimensions(float[] vector)
    {
        if (vector.Length != ExpectedDim)
            throw new InvalidOperationException(
                $"Gemini Embedding returned {vector.Length} dimensions; expected {ExpectedDim}. " +
                "Ensure model is text-embedding-004.");
    }
}
