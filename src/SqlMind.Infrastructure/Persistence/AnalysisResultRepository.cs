using Microsoft.EntityFrameworkCore;
using SqlMind.Core.Interfaces;
using SqlMind.Core.Models;

namespace SqlMind.Infrastructure.Persistence;

public sealed class AnalysisResultRepository : IAnalysisResultRepository
{
    private readonly SqlMindDbContext _db;

    public AnalysisResultRepository(SqlMindDbContext db) => _db = db;

    public Task<AnalysisResult?> GetByJobIdAsync(Guid jobId, CancellationToken ct = default)
        => _db.AnalysisResults.FirstOrDefaultAsync(r => r.JobId == jobId, ct);

    public async Task AddAsync(AnalysisResult result, CancellationToken ct = default)
    {
        _db.AnalysisResults.Add(result);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AnalysisResult>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        return await _db.AnalysisResults
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }
}
