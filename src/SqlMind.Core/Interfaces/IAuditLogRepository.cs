using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// Append-only write to audit_logs. Reads are for debugging/reporting only.
/// </summary>
public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLog>> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default);
}
