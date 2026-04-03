using Microsoft.EntityFrameworkCore;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.Persistence;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly SqlMindDbContext _db;

    public AuditLogRepository(SqlMindDbContext db) => _db = db;

    public async Task AddAsync(AuditLog log, CancellationToken ct = default)
    {
        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditLog>> GetByCorrelationIdAsync(
        string correlationId, CancellationToken ct = default)
    {
        return await _db.AuditLogs
            .Where(a => a.CorrelationId == correlationId)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(ct);
    }
}
