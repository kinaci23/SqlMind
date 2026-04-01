namespace SqlMind.Core.Enums;

/// <summary>
/// Categorizes the SQL statement type detected by ISqlAnalyzer.
/// DDL operations (DROP, ALTER, TRUNCATE) always warrant elevated scrutiny.
/// </summary>
public enum OperationType
{
    SELECT,
    INSERT,
    UPDATE,
    DELETE,
    DDL
}
