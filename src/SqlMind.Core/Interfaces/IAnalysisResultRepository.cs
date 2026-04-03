using SqlMind.Core.Models;

namespace SqlMind.Core.Interfaces;

/// <summary>
/// Persistence for analysis_results.
/// </summary>
public interface IAnalysisResultRepository
{
    Task<AnalysisResult?> GetByJobIdAsync(Guid jobId, CancellationToken ct = default);
    Task AddAsync(AnalysisResult result, CancellationToken ct = default);
    Task<IReadOnlyList<AnalysisResult>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
}
