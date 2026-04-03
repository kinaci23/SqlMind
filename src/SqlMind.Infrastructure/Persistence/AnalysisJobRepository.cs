using Microsoft.EntityFrameworkCore;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.Persistence;

public sealed class AnalysisJobRepository : IAnalysisJobRepository
{
    private readonly SqlMindDbContext _db;

    public AnalysisJobRepository(SqlMindDbContext db) => _db = db;

    public Task<AnalysisJob?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.AnalysisJobs.FirstOrDefaultAsync(j => j.Id == id, ct);

    public Task<AnalysisJob?> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default)
        => _db.AnalysisJobs.FirstOrDefaultAsync(j => j.CorrelationId == correlationId, ct);

    public async Task AddAsync(AnalysisJob job, CancellationToken ct = default)
    {
        _db.AnalysisJobs.Add(job);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(AnalysisJob job, CancellationToken ct = default)
    {
        _db.AnalysisJobs.Update(job);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AnalysisJob>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        return await _db.AnalysisJobs
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }
}
