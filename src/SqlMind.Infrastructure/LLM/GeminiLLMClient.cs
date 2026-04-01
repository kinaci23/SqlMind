using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.LLM;

/// <summary>
/// ILLMClient implementation backed by the Gemini REST API.
/// Uses HttpClient for all network calls. Retries up to 3 times with exponential back-off.
/// API key is read from configuration (GEMINI_API_KEY).
/// </summary>
public sealed class GeminiLLMClient : ILLMClient
{
    // ── Gemini REST endpoint ──────────────────────────────────────────────────
    private const string Model       = "gemini-2.5-pro";
    private const string BaseUrl     = "https://generativelanguage.googleapis.com/v1beta/models";
    private const float  Temperature = 0.1f;   // deterministic output (0.0–0.2)
    private const int    MaxRetries  = 3;

    private readonly HttpClient _http;
    private readonly string     _apiKey;
    private readonly ILogger<GeminiLLMClient> _logger;

    public string ProviderName => "gemini";

    public GeminiLLMClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiLLMClient> logger)
    {
        _http   = httpClient;
        _logger = logger;
        _apiKey = configuration["GEMINI_API_KEY"]
                  ?? throw new InvalidOperationException(
                      "GEMINI_API_KEY is not configured. Set it in environment variables or appsettings.");
    }

    // ── ILLMClient ────────────────────────────────────────────────────────────

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var url     = $"{BaseUrl}/{Model}:generateContent?key={_apiKey}";
        var payload = BuildRequestPayload(systemPrompt, userPrompt);

        return await ExecuteWithRetryAsync(url, payload, cancellationToken);
    }

    public async Task<LlmAnalysisResult> AnalyzeAsync(
        LlmAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = PromptBuilder.BuildSystemPrompt();
        var userPrompt   = PromptBuilder.BuildUserPrompt(request);

        _logger.LogInformation(
            "Sending SQL analysis request to Gemini. CorrelationId={CorrelationId}",
            request.CorrelationId);

        var rawJson = await CompleteAsync(systemPrompt, userPrompt, cancellationToken);

        _logger.LogDebug(
            "Gemini raw response. CorrelationId={CorrelationId} Length={Length}",
            request.CorrelationId, rawJson.Length);

        return LlmOutputValidator.ValidateAndParse(rawJson);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Minimal ping: list models endpoint requires only a valid API key
            var url      = $"{BaseUrl}?key={_apiKey}";
            var response = await _http.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini health check failed.");
            return false;
        }
    }

    // ── Request / response helpers ────────────────────────────────────────────

    private static object BuildRequestPayload(string systemPrompt, string userPrompt) =>
        new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role  = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            },
            generationConfig = new
            {
                temperature      = Temperature,
                responseMimeType = "application/json"   // ask Gemini to return JSON directly
            }
        };

    /// <summary>
    /// Executes the HTTP call with exponential back-off retry on transient errors.
    /// Retries on 429 (rate-limit) and 5xx (server errors) only.
    /// </summary>
    private async Task<string> ExecuteWithRetryAsync(
        string url,
        object payload,
        CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                var response = await _http.PostAsJsonAsync(url, payload, cancellationToken);

                if (response.IsSuccessStatusCode)
                    return await ExtractTextFromResponse(response, cancellationToken);

                // Retriable status codes
                if (attempt < MaxRetries &&
                    (response.StatusCode == HttpStatusCode.TooManyRequests ||
                     (int)response.StatusCode >= 500))
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);

                    // Honour the retryDelay hint Gemini includes in 429 bodies
                    var delay = response.StatusCode == HttpStatusCode.TooManyRequests
                        ? (ParseRetryDelay(body) ?? TimeSpan.FromSeconds(Math.Pow(2, attempt) * 10))
                        : TimeSpan.FromSeconds(Math.Pow(2, attempt));

                    _logger.LogWarning(
                        "Gemini returned {StatusCode}. Retry {Attempt}/{Max} in {Delay}s.",
                        (int)response.StatusCode, attempt, MaxRetries, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"Gemini API error {(int)response.StatusCode}: {errorBody}");
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(
                    "Gemini HTTP error. Retry {Attempt}/{Max} in {Delay}s.",
                    attempt, MaxRetries, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Parses the retryDelay field (e.g. "20s") from a Gemini 429 error body.
    /// Returns null if the field is absent or unparseable.
    /// </summary>
    private static TimeSpan? ParseRetryDelay(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var error)) return null;
            if (!error.TryGetProperty("details", out var details)) return null;

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.TryGetProperty("retryDelay", out var retryProp))
                {
                    var raw = retryProp.GetString();
                    if (raw is not null && raw.EndsWith('s') &&
                        double.TryParse(raw[..^1], out var seconds))
                    {
                        return TimeSpan.FromSeconds(seconds + 2); // small buffer
                    }
                }
            }
        }
        catch { /* best-effort */ }
        return null;
    }

    /// <summary>
    /// Extracts the generated text from Gemini's response envelope.
    /// Gemini wraps the LLM output inside: candidates[0].content.parts[0].text
    /// </summary>
    private static async Task<string> ExtractTextFromResponse(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Gemini returned non-JSON response: {body[..Math.Min(200, body.Length)]}", ex);
        }

        using (doc)
        {
            // Navigate: candidates[0].content.parts[0].text
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.GetArrayLength() == 0)
                throw new InvalidOperationException("Gemini response contains no candidates.");

            var content = candidates[0].GetProperty("content");
            var parts   = content.GetProperty("parts");

            if (parts.GetArrayLength() == 0)
                throw new InvalidOperationException("Gemini candidate has no parts.");

            var text = parts[0].GetProperty("text").GetString()
                       ?? throw new InvalidOperationException("Gemini part text is null.");

            return text;
        }
    }
}
