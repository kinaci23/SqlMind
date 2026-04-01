namespace SqlMind.Core.Interfaces;

/// <summary>
/// Abstracts background job scheduling. Default implementation uses Hangfire.
/// All async analysis work is enqueued here — the API returns job_id immediately.
/// </summary>
public interface IBackgroundJobService
{
    /// <summary>
    /// Enqueues a fire-and-forget background job.
    /// </summary>
    /// <typeparam name="T">Type containing the job method.</typeparam>
    /// <param name="methodCall">Expression pointing to the method to invoke.</param>
    /// <returns>Opaque job ID that can be used for status queries.</returns>
    string Enqueue<T>(System.Linq.Expressions.Expression<Action<T>> methodCall);

    /// <summary>
    /// Enqueues an async fire-and-forget background job.
    /// </summary>
    string Enqueue<T>(System.Linq.Expressions.Expression<Func<T, Task>> methodCall);

    /// <summary>
    /// Schedules a job to run at a specific point in the future.
    /// </summary>
    string Schedule<T>(System.Linq.Expressions.Expression<Func<T, Task>> methodCall, TimeSpan delay);

    /// <summary>
    /// Retrieves the current state of a job by its ID.
    /// </summary>
    /// <param name="jobId">Job ID returned by Enqueue/Schedule.</param>
    /// <returns>State string (e.g., "Enqueued", "Processing", "Succeeded", "Failed").</returns>
    string? GetJobState(string jobId);
}
