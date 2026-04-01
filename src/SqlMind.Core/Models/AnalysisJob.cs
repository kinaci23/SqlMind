using SqlMind.Core.Enums;

namespace SqlMind.Core.Models;

/// <summary>
/// Represents an async analysis job queued via IBackgroundJobService.
/// The API returns the JobId immediately; status is polled separately.
/// </summary>
public sealed class AnalysisJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    public string SqlContent { get; init; } = string.Empty;
    public string InputHash { get; init; } = string.Empty;
    public string Status { get; set; } = "Enqueued";
    public string? BackgroundJobId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? ResultId { get; set; }
}
