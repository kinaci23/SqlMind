using SqlMind.Core.Enums;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.SqlParsing;

/// <summary>
/// Character-based SQL tokenizer and parser. Produces an AST-like SqlParseResult
/// without using regex — processes the input character by character.
/// </summary>
public sealed class CustomSqlAnalyzer : ISqlAnalyzer
{
    // -------------------------------------------------------------------------
    // Token model
    // -------------------------------------------------------------------------

    private enum TokenKind
    {
        Keyword,
        Identifier,
        StringLiteral,
        NumberLiteral,
        Punctuation
    }

    private readonly record struct SqlToken(TokenKind Kind, string Value);

    // -------------------------------------------------------------------------
    // SQL keyword set (uppercased for O(1) lookup)
    // -------------------------------------------------------------------------

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "SELECT", "FROM", "WHERE", "UPDATE", "DELETE", "INSERT", "INTO",
        "DROP", "TABLE", "TRUNCATE", "ALTER", "CREATE", "JOIN",
        "LEFT", "RIGHT", "INNER", "OUTER", "FULL", "CROSS",
        "ON", "SET", "AS", "WITH", "HAVING", "GROUP", "BY", "ORDER",
        "LIMIT", "OFFSET", "UNION", "INTERSECT", "EXCEPT", "EXISTS",
        "NOT", "AND", "OR", "IN", "BETWEEN", "LIKE", "IS", "NULL",
        "INDEX", "VIEW", "DATABASE", "SCHEMA", "ADD", "COLUMN",
        "CONSTRAINT", "PRIMARY", "KEY", "FOREIGN", "REFERENCES",
        "DEFAULT", "UNIQUE", "CHECK", "MODIFY", "RENAME", "PROCEDURE",
        "FUNCTION", "TRIGGER", "RETURNING", "USING", "TOP", "DISTINCT",
        "ALL", "VALUES"
    };

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public Task<SqlParseResult> ParseAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Task.FromResult(new SqlParseResult
            {
                OriginalSql = sql ?? string.Empty,
                NormalizedSql = string.Empty,
                ParseWarnings = ["Input SQL was empty or whitespace."]
            });
        }

        var tokens = Tokenize(sql);
        var statements = SplitStatements(tokens);

        var operations = new List<OperationType>();
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        bool hasUnfilteredMutation = false;
        bool hasDdlOperation = false;
        bool whereClauseExists = false;
        bool joinExists = false;
        bool hasDropStatement = false;
        bool hasTruncateStatement = false;
        bool hasAlterStatement = false;

        foreach (var stmtTokens in statements)
        {
            if (stmtTokens.Count == 0) continue;

            AnalyzeStatement(
                stmtTokens,
                operations,
                tables,
                warnings,
                ref hasUnfilteredMutation,
                ref hasDdlOperation,
                ref whereClauseExists,
                ref joinExists,
                ref hasDropStatement,
                ref hasTruncateStatement,
                ref hasAlterStatement);
        }

        return Task.FromResult(new SqlParseResult
        {
            OriginalSql = sql,
            NormalizedSql = sql.Trim(),
            Operations = operations.AsReadOnly(),
            TablesDetected = tables.ToList().AsReadOnly(),
            HasUnfilteredMutation = hasUnfilteredMutation,
            HasDdlOperation = hasDdlOperation,
            ParseWarnings = warnings.AsReadOnly(),
            WhereClauseExists = whereClauseExists,
            JoinExists = joinExists,
            HasDropStatement = hasDropStatement,
            HasTruncateStatement = hasTruncateStatement,
            HasAlterStatement = hasAlterStatement
        });
    }

    // -------------------------------------------------------------------------
    // Tokenizer — character-by-character, no regex
    // -------------------------------------------------------------------------

    private static List<SqlToken> Tokenize(string sql)
    {
        var tokens = new List<SqlToken>();
        int i = 0;

        while (i < sql.Length)
        {
            char c = sql[i];

            // Whitespace — skip silently
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Line comment  --
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                continue;
            }

            // Block comment  /* ... */
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                    i++;
                if (i + 1 < sql.Length) i += 2; // consume '*/'
                continue;
            }

            // String literal  '...'  (handles '' escape)
            if (c == '\'')
            {
                int start = i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'')
                    {
                        i++;
                        if (i < sql.Length && sql[i] == '\'') i++; // escaped ''
                        else break;
                    }
                    else i++;
                }
                tokens.Add(new SqlToken(TokenKind.StringLiteral, sql[start..i]));
                continue;
            }

            // Quoted identifier  "..."
            if (c == '"')
            {
                int start = ++i;
                while (i < sql.Length && sql[i] != '"') i++;
                string name = sql[start..i];
                if (i < sql.Length) i++;
                tokens.Add(new SqlToken(TokenKind.Identifier, name.ToUpperInvariant()));
                continue;
            }

            // Quoted identifier  [...]  (SQL Server / SQLite)
            if (c == '[')
            {
                int start = ++i;
                while (i < sql.Length && sql[i] != ']') i++;
                string name = sql[start..i];
                if (i < sql.Length) i++;
                tokens.Add(new SqlToken(TokenKind.Identifier, name.ToUpperInvariant()));
                continue;
            }

            // Quoted identifier  `...`  (MySQL)
            if (c == '`')
            {
                int start = ++i;
                while (i < sql.Length && sql[i] != '`') i++;
                string name = sql[start..i];
                if (i < sql.Length) i++;
                tokens.Add(new SqlToken(TokenKind.Identifier, name.ToUpperInvariant()));
                continue;
            }

            // Word: keyword or identifier
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                string word = sql[start..i].ToUpperInvariant();
                TokenKind kind = Keywords.Contains(word) ? TokenKind.Keyword : TokenKind.Identifier;
                tokens.Add(new SqlToken(kind, word));
                continue;
            }

            // Number
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < sql.Length && (char.IsDigit(sql[i]) || sql[i] == '.')) i++;
                tokens.Add(new SqlToken(TokenKind.NumberLiteral, sql[start..i]));
                continue;
            }

            // Everything else is punctuation (operators, semicolons, commas, parens, etc.)
            tokens.Add(new SqlToken(TokenKind.Punctuation, c.ToString()));
            i++;
        }

        return tokens;
    }

    // -------------------------------------------------------------------------
    // Statement splitter — splits on ; (semicolons)
    // -------------------------------------------------------------------------

    private static List<List<SqlToken>> SplitStatements(List<SqlToken> tokens)
    {
        var statements = new List<List<SqlToken>>();
        var current = new List<SqlToken>();

        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Punctuation && token.Value == ";")
            {
                if (current.Count > 0)
                {
                    statements.Add(current);
                    current = [];
                }
            }
            else
            {
                current.Add(token);
            }
        }

        if (current.Count > 0)
            statements.Add(current);

        return statements;
    }

    // -------------------------------------------------------------------------
    // Per-statement analysis
    // -------------------------------------------------------------------------

    private static void AnalyzeStatement(
        List<SqlToken> tokens,
        List<OperationType> operations,
        HashSet<string> tables,
        List<string> warnings,
        ref bool hasUnfilteredMutation,
        ref bool hasDdlOperation,
        ref bool whereClauseExists,
        ref bool joinExists,
        ref bool hasDropStatement,
        ref bool hasTruncateStatement,
        ref bool hasAlterStatement)
    {
        // First keyword determines the statement type
        var firstKw = tokens.FirstOrDefault(t => t.Kind == TokenKind.Keyword);
        if (firstKw.Value is null) return;

        OperationType? opType = firstKw.Value switch
        {
            "SELECT"   => OperationType.SELECT,
            "INSERT"   => OperationType.INSERT,
            "UPDATE"   => OperationType.UPDATE,
            "DELETE"   => OperationType.DELETE,
            "DROP" or "TRUNCATE" or "ALTER" or "CREATE" => OperationType.DDL,
            _ => null
        };

        if (opType is null)
        {
            warnings.Add($"Unrecognized statement starting with '{firstKw.Value}'.");
            return;
        }

        operations.Add(opType.Value);

        // Scan for structural keywords
        bool stmtHasWhere = tokens.Any(t => t.Kind == TokenKind.Keyword && t.Value == "WHERE");
        bool stmtHasJoin  = tokens.Any(t => t.Kind == TokenKind.Keyword && t.Value == "JOIN");

        if (stmtHasWhere) whereClauseExists = true;
        if (stmtHasJoin)  joinExists = true;

        // Unfiltered mutation flag
        if (opType is OperationType.UPDATE or OperationType.DELETE && !stmtHasWhere)
            hasUnfilteredMutation = true;

        // DDL sub-type flags
        if (opType == OperationType.DDL)
        {
            hasDdlOperation = true;

            switch (firstKw.Value)
            {
                case "DROP":     hasDropStatement     = true; break;
                case "TRUNCATE": hasTruncateStatement = true; break;
                case "ALTER":    hasAlterStatement    = true; break;
            }
        }

        // Table extraction
        ExtractTables(tokens, opType.Value, tables);
    }

    // -------------------------------------------------------------------------
    // Table name extraction
    // -------------------------------------------------------------------------

    private static void ExtractTables(
        List<SqlToken> tokens,
        OperationType opType,
        HashSet<string> tables)
    {
        bool expectTable = false;
        bool seenDrop    = false;
        bool seenAlter   = false;
        bool seenTrunc   = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Kind == TokenKind.Keyword)
            {
                // Reset expectation before re-evaluating this keyword
                bool prevExpect = expectTable;
                expectTable = false;

                switch (token.Value)
                {
                    case "FROM":
                    case "JOIN":
                        expectTable = true;
                        break;

                    case "UPDATE":
                        if (opType == OperationType.UPDATE)
                            expectTable = true;
                        break;

                    case "INTO":
                        if (opType == OperationType.INSERT)
                            expectTable = true;
                        break;

                    case "DROP":
                        seenDrop = true;
                        break;

                    case "ALTER":
                        seenAlter = true;
                        break;

                    case "TRUNCATE":
                        seenTrunc = true;
                        expectTable = true; // handles: TRUNCATE tablename (no TABLE keyword)
                        break;

                    case "TABLE":
                        if (seenDrop || seenAlter || seenTrunc)
                            expectTable = true;
                        break;

                    default:
                        // Other keywords break the table-name expectation already cleared above
                        seenDrop  = false;
                        seenAlter = false;
                        seenTrunc = false;
                        _ = prevExpect; // suppress unused warning
                        break;
                }
            }
            else if (token.Kind == TokenKind.Identifier && expectTable)
            {
                // Handle schema-qualified names: schema.table or [schema].[table]
                // If the next two tokens are "." + Identifier, the second is the table name.
                if (i + 2 < tokens.Count
                    && tokens[i + 1].Kind == TokenKind.Punctuation && tokens[i + 1].Value == "."
                    && tokens[i + 2].Kind == TokenKind.Identifier)
                {
                    tables.Add(tokens[i + 2].Value);
                    i += 2; // consume "." and table identifier
                }
                else
                {
                    tables.Add(token.Value);
                }
                expectTable = false;
            }
            else if (token.Kind == TokenKind.Punctuation || token.Kind == TokenKind.StringLiteral)
            {
                expectTable = false;
            }
        }
    }
}
