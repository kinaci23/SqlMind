using SqlMind.Core.Enums;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.LLM;

/// <summary>
/// Builds the system and user prompts for SQL analysis.
/// System and user prompts are constructed in separate methods to prevent prompt injection.
/// </summary>
public static class PromptBuilder
{
    // -------------------------------------------------------------------------
    // Required JSON output schema — injected into every system prompt
    // -------------------------------------------------------------------------

    private const string OutputSchema = """
        {
          "business_summary":    "<non-technical explanation for stakeholders>",
          "technical_summary":   "<detailed technical explanation of the SQL and its risk>",
          "risk_insights":       ["<risk signal 1>", "<risk signal 2>"],
          "uncertainties":       ["<area where more context is needed>"],
          "recommended_actions": ["<concrete action 1>", "<concrete action 2>"]
        }
        """;

    // -------------------------------------------------------------------------
    // System prompt
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the system prompt. This is isolated from user input to prevent injection.
    /// Defines the assistant's role, hard rules, and output contract.
    /// </summary>
    public static string BuildSystemPrompt() => $"""
        You are SqlMind, an expert SQL risk analysis assistant embedded in an automated
        database governance system. Your sole task is to analyse SQL scripts that have
        already been evaluated by a deterministic rule-based engine, and to provide
        complementary business and technical insights.

        ## Hard Rules
        1. You MUST respond with valid JSON only — no markdown, no prose, no code fences.
        2. Every response MUST include all five fields in the schema below.
        3. You MUST NOT contradict a CRITICAL or HIGH rule-based risk finding.
           If the rule engine flagged a risk, your insights must acknowledge it.
        4. Temperature is implicitly 0.0–0.2 — be deterministic and concise.
        5. Do not hallucinate table schemas, business rules, or execution plans.
           If you are unsure, state it in the "uncertainties" field.
        6. "recommended_actions" must be concrete and actionable (not generic advice).
        7. CRITICAL LANGUAGE RULE: You MUST respond ONLY in Turkish.
           All fields including business_summary, technical_summary,
           risk_insights, uncertainties and recommended_actions
           MUST be written in Turkish. Never use English in your response.

        ## Output Schema (strict — all fields required)
        {OutputSchema}
        """;

    // -------------------------------------------------------------------------
    // User prompt
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the user prompt from the analysis request.
    /// SQL content is sanitised (no system-prompt-like prefixes allowed).
    /// </summary>
    public static string BuildUserPrompt(LlmAnalysisRequest request)
    {
        var p = request.ParseResult;

        var operations = p.Operations.Count > 0
            ? string.Join(", ", p.Operations)
            : "unknown";

        var tables = p.TablesDetected.Count > 0
            ? string.Join(", ", p.TablesDetected)
            : "none detected";

        var flags = BuildFlagSummary(p);

        var ragSection = string.IsNullOrWhiteSpace(request.RagContext)
            ? "No additional context available from the knowledge base."
            : $"""
              ## Relevant Knowledge Base Context
              {request.RagContext}
              """;

        // Sanitise SQL: strip any leading/trailing instruction-like text
        var safeSql = SanitiseSql(p.OriginalSql);

        return $"""
            ## SQL Script to Analyse
            ```sql
            {safeSql}
            ```

            ## Rule-Based Analysis (deterministic — do not contradict)
            - Operation(s)    : {operations}
            - Tables affected : {tables}
            - Risk level      : {request.RuleBasedRiskLevel}
            - Risk flags      : {flags}
            - Correlation ID  : {request.CorrelationId}

            {ragSection}

            Respond with valid JSON only, matching the required schema exactly.
            """;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string BuildFlagSummary(SqlParseResult p)
    {
        var parts = new List<string>();
        if (p.HasUnfilteredMutation) parts.Add("unfiltered mutation (no WHERE)");
        if (p.HasDdlOperation)       parts.Add("DDL operation");
        if (p.HasDropStatement)      parts.Add("DROP statement");
        if (p.HasTruncateStatement)  parts.Add("TRUNCATE statement");
        if (p.HasAlterStatement)     parts.Add("ALTER statement");
        if (p.JoinExists)            parts.Add("JOIN present");
        if (p.WhereClauseExists)     parts.Add("WHERE clause present");
        return parts.Count > 0 ? string.Join("; ", parts) : "none";
    }

    /// <summary>
    /// Removes leading lines that look like injected system instructions.
    /// Truncates SQL that is excessively long (protects token budget).
    /// </summary>
    private static string SanitiseSql(string sql)
    {
        const int maxLength = 4000;

        // Strip lines that start with common injection attempts
        var lines = sql.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("##", StringComparison.Ordinal))
            .Where(l => !l.TrimStart().StartsWith("You are", StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.TrimStart().StartsWith("Ignore previous", StringComparison.OrdinalIgnoreCase));

        var clean = string.Join('\n', lines);

        return clean.Length > maxLength
            ? clean[..maxLength] + "\n-- [truncated]"
            : clean;
    }
}
