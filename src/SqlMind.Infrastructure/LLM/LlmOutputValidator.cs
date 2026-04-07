using System.Text.Json;
using SqlMind.Core;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.LLM;

/// <summary>
/// Validates that the raw JSON returned by the LLM satisfies the required output schema.
/// Throws <see cref="LlmOutputValidationException"/> if any required field is missing or empty.
/// Free-text responses (non-JSON) are rejected outright.
/// </summary>
public static class LlmOutputValidator
{
    private static readonly string[] RequiredFields =
    [
        "business_summary",
        "technical_summary",
        "risk_insights",
        "uncertainties",
        "recommended_actions"
    ];

    /// <summary>
    /// Parses and validates the raw JSON string, then maps it to <see cref="LlmAnalysisResult"/>.
    /// </summary>
    /// <exception cref="LlmOutputValidationException">
    /// Thrown when the JSON is invalid or a required field is missing/empty.
    /// </exception>
    public static LlmAnalysisResult ValidateAndParse(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            throw new LlmOutputValidationException("LLM returned an empty response.");

        // Strip markdown code fences if the model wrapped the JSON anyway
        var json = StripCodeFences(rawJson);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new LlmOutputValidationException(
                $"LLM response is not valid JSON: {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;

            foreach (var field in RequiredFields)
            {
                if (!root.TryGetProperty(field, out var prop))
                    throw new LlmOutputValidationException(
                        $"LLM response missing required field: '{field}'.");

                if (prop.ValueKind == JsonValueKind.String &&
                    string.IsNullOrWhiteSpace(prop.GetString()))
                    throw new LlmOutputValidationException(
                        $"LLM response field '{field}' is empty.");
            }

            var result = new LlmAnalysisResult
            {
                BusinessSummary    = root.GetProperty("business_summary").GetString()!,
                TechnicalSummary   = root.GetProperty("technical_summary").GetString()!,
                RiskInsights       = ReadStringArray(root, "risk_insights"),
                Uncertainties      = ReadStringArray(root, "uncertainties"),
                RecommendedActions = ReadStringArray(root, "recommended_actions"),
                RawJson            = json
            };

            AppLogger.Info($"Parsed business_summary: '{result.BusinessSummary}'");

            return result;
        }
    }

    // -------------------------------------------------------------------------

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string fieldName)
    {
        var prop = root.GetProperty(fieldName);

        if (prop.ValueKind != JsonValueKind.Array)
            throw new LlmOutputValidationException(
                $"LLM response field '{fieldName}' must be a JSON array.");

        var list = new List<string>();
        foreach (var element in prop.EnumerateArray())
        {
            var value = element.GetString();
            if (value is not null) list.Add(value);
        }
        return list.AsReadOnly();
    }

    private static string StripCodeFences(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            // Remove opening fence (```json or ``` )
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0) trimmed = trimmed[(firstNewline + 1)..];

            // Remove closing fence
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) trimmed = trimmed[..lastFence];
        }
        return trimmed.Trim();
    }
}

/// <summary>
/// Thrown when the LLM response fails schema validation.
/// </summary>
public sealed class LlmOutputValidationException : Exception
{
    public LlmOutputValidationException(string message) : base(message) { }
    public LlmOutputValidationException(string message, Exception inner) : base(message, inner) { }
}
