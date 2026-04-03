using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// CRUD operations on the analysis_jobs table.
/// </summary>
public interface IAnalysisJobRepository
{
    Task<AnalysisJob?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AnalysisJob?> GetByCorrelationIdAsync(string correlationId, CancellationToken ct = default);
    Task AddAsync(AnalysisJob job, CancellationToken ct = default);
    Task UpdateAsync(AnalysisJob job, CancellationToken ct = default);
    Task<IReadOnlyList<AnalysisJob>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
}
