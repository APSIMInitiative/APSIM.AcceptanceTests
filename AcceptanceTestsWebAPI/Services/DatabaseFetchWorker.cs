using AcceptanceTestsWebAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceTestsWebAPI.Services;

/// <summary>
/// Polls for queued database downloads, claims them with renewable leases and
/// records successful fetches or retryable failures.
/// </summary>
public sealed class DatabaseFetchWorker(
    IServiceScopeFactory scopeFactory,
    IDatabaseBlobFetcher fetcher,
    ILogger<DatabaseFetchWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to process the database fetch queue.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }

    internal async Task ProcessNextAsync(CancellationToken stoppingToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
        DateTime now = DateTime.UtcNow;
        AcceptanceJobEntity? run = await ReadyToFetch(db.PullRequests.AsNoTracking(), now)
            .OrderBy(row => row.NextAttemptUtc)
            .FirstOrDefaultAsync(stoppingToken);
        if (run == null)
            return;
        if (run.ContainerName == null)
            throw new InvalidOperationException($"Database fetch for run {run.Id} has no container.");

        Guid leaseId = Guid.NewGuid();
        // Recheck eligibility in the update itself so only one worker can claim the run.
        int claimed = await ReadyToFetch(db.PullRequests.Where(row => row.Id == run.Id), now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.FetchStatus, DatabaseFetchStatus.Fetching)
                .SetProperty(row => row.LeaseId, leaseId)
                .SetProperty(row => row.LeaseExpiresUtc, now + LeaseDuration)
                .SetProperty(row => row.FetchAttempts, row => row.FetchAttempts + 1), stoppingToken);
        if (claimed == 0)
            return;

        using var fetchCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task renewal = RenewLeaseAsync(run.Id, leaseId, fetchCancellation);
        try
        {
            int count = await fetcher.FetchAsync(run.Id, run.ContainerName, fetchCancellation.Token);
            fetchCancellation.Token.ThrowIfCancellationRequested();
            // Only the holder of a still-valid lease can publish a successful result.
            int completed = await db.PullRequests.Where(row => row.Id == run.Id && row.LeaseId == leaseId &&
                    row.FetchStatus == DatabaseFetchStatus.Fetching && row.LeaseExpiresUtc > DateTime.UtcNow)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.FetchStatus, DatabaseFetchStatus.Completed)
                    .SetProperty(row => row.DownloadedFiles, count)
                    .SetProperty(row => row.FetchError, (string?)null)
                    .SetProperty(row => row.LeaseId, (Guid?)null)
                    .SetProperty(row => row.LeaseExpiresUtc, (DateTime?)null)
                    .SetProperty(row => row.NextAttemptUtc, (DateTime?)null), stoppingToken);
            if (completed == 0)
                throw new InvalidOperationException($"Database fetch lease was lost for run {run.Id}.");
            logger.LogInformation("Downloaded {Count} databases for run {RunId}.", count, run.Id);
        }
        catch (OperationCanceledException) when (fetchCancellation.IsCancellationRequested)
        {
            if (!stoppingToken.IsCancellationRequested)
                logger.LogWarning("Database fetch stopped after losing the lease for run {RunId}.", run.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database fetch failed for run {RunId}; it will be retried.", run.Id);
            // Delay successive retries by 1, 2, 4, ... minutes, capped at 64 minutes.
            TimeSpan retryDelay = TimeSpan.FromMinutes(Math.Pow(2, Math.Min(run.FetchAttempts, 6)));
            await db.PullRequests.Where(row => row.Id == run.Id && row.LeaseId == leaseId && row.FetchStatus == DatabaseFetchStatus.Fetching)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.FetchStatus, DatabaseFetchStatus.Pending)
                    .SetProperty(row => row.FetchError, "Database fetch failed; see server logs.")
                    .SetProperty(row => row.NextAttemptUtc, DateTime.UtcNow + retryDelay)
                    .SetProperty(row => row.LeaseId, (Guid?)null)
                    .SetProperty(row => row.LeaseExpiresUtc, (DateTime?)null), stoppingToken);
        }
        finally
        {
            await fetchCancellation.CancelAsync();
            await renewal;
        }
    }

    /// <summary>Finds due pending fetches and abandoned fetches whose leases have expired.</summary>
    private static IQueryable<AcceptanceJobEntity> ReadyToFetch(IQueryable<AcceptanceJobEntity> runs, DateTime now) =>
        runs.Where(row => (row.FetchStatus == DatabaseFetchStatus.Pending && (row.NextAttemptUtc == null || row.NextAttemptUtc <= now)) ||
            (row.FetchStatus == DatabaseFetchStatus.Fetching && row.LeaseExpiresUtc <= now));

    private async Task RenewLeaseAsync(Guid runId, Guid leaseId, CancellationTokenSource cancellation)
    {
        try
        {
            using var timer = new PeriodicTimer(LeaseRenewalInterval);
            while (await timer.WaitForNextTickAsync(cancellation.Token))
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();
                DateTime now = DateTime.UtcNow;
                int renewed = await db.PullRequests.Where(row => row.Id == runId && row.LeaseId == leaseId &&
                        row.FetchStatus == DatabaseFetchStatus.Fetching && row.LeaseExpiresUtc > now)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LeaseExpiresUtc, now + LeaseDuration), cancellation.Token);
                if (renewed == 0)
                {
                    // Stop the download when this worker can no longer prove it owns the lease.
                    await cancellation.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to renew database fetch lease for run {RunId}.", runId);
            await cancellation.CancelAsync();
        }
    }
}
