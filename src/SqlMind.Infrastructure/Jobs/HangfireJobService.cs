using Hangfire;
using SqlMind.Core.Interfaces;
using System.Linq.Expressions;

namespace SqlMind.Infrastructure.Jobs;

/// <summary>
/// IBackgroundJobService implementation using Hangfire.
/// All async analysis work is enqueued here; the API returns job_id immediately.
/// </summary>
public sealed class HangfireJobService : IBackgroundJobService
{
    public string Enqueue<T>(Expression<Action<T>> methodCall)
        => BackgroundJob.Enqueue(methodCall);

    public string Enqueue<T>(Expression<Func<T, Task>> methodCall)
        => BackgroundJob.Enqueue(methodCall);

    public string Schedule<T>(Expression<Func<T, Task>> methodCall, TimeSpan delay)
        => BackgroundJob.Schedule(methodCall, delay);

    public string? GetJobState(string jobId)
    {
        var connection = JobStorage.Current.GetConnection();
        var jobData    = connection.GetJobData(jobId);
        return jobData?.State;
    }
}
