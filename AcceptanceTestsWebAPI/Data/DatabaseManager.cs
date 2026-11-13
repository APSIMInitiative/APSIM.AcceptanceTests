using Microsoft.EntityFrameworkCore;
using Shared.Enums;

namespace AcceptanceTestsWebAPI.Data;

/// <summary>
/// Monitors running pull requests, closing completed runs and queuing their
/// database downloads, or marking runs that exceed the timeout.
/// </summary>
public class DatabaseManager : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseManager> _logger;
    private readonly TimeSpan _period = TimeSpan.FromSeconds(15);

    public DatabaseManager(IServiceScopeFactory scopeFactory, ILogger<DatabaseManager> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Database Manager started.");
        using PeriodicTimer timer = new PeriodicTimer(_period);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DoWorkAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating the database.");
            }
        }
    }

    internal async Task DoWorkAsync(CancellationToken stoppingToken)
    {
        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            AcceptanceTestsDbContext db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();

            List<PullRequestEntity> pullRequestsRunning = await db.PullRequests.AsNoTracking().Where(pr => pr.Status == PullRequestStatus.Running).ToListAsync(stoppingToken);

            foreach(PullRequestEntity pr in pullRequestsRunning)
            {
                double minutesPassed = (DateTime.Now - pr.StartTime).TotalMinutes;
                if (pr.NumberOfTasks > 0 && pr.NumberOfTasks == pr.NumberOfTasksCompleted)
                    await CloseCompletedRunAsync(db, pr, stoppingToken);
                else if (minutesPassed > 30)
                    await TimeOutRunAsync(db, pr, stoppingToken);
            }
        }
    }

    private async Task CloseCompletedRunAsync(AcceptanceTestsDbContext db, PullRequestEntity pr, CancellationToken stoppingToken)
    {
        if (pr.CurrentRunId == null)
        {
            _logger.LogWarning("Cannot fetch databases for legacy pull request {Id}: run and job information is missing.", pr.Id);
            return;
        }

        // The snapshot may be stale; recheck completion and run identity in the update.
        // Closing the pull request and queuing its fetch must commit together.
        await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
        int closed = await db.PullRequests.Where(row => row.Id == pr.Id &&
                row.CurrentRunId == pr.CurrentRunId && row.Status == PullRequestStatus.Running &&
                row.NumberOfTasks > 0 && row.NumberOfTasks == row.NumberOfTasksCompleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, PullRequestStatus.Closed)
                .SetProperty(row => row.EndTime, DateTime.Now), stoppingToken);
        if (closed == 1)
        {
            int queued = await db.Runs.Where(run => run.Id == pr.CurrentRunId && run.FetchStatus == DatabaseFetchStatus.WaitingForJobs)
                .ExecuteUpdateAsync(setters => setters.SetProperty(run => run.FetchStatus, DatabaseFetchStatus.Pending), stoppingToken);
            if (queued != 1)
                throw new InvalidOperationException($"Could not queue database fetch for run {pr.CurrentRunId}.");
        }
        await transaction.CommitAsync(stoppingToken);
    }

    private static async Task TimeOutRunAsync(AcceptanceTestsDbContext db, PullRequestEntity pr, CancellationToken stoppingToken)
    {
        // Recheck the counts so a run whose final job just completed is not timed out.
        await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
        int timedOut = await db.PullRequests.Where(row => row.Id == pr.Id && row.CurrentRunId == pr.CurrentRunId &&
                row.Status == PullRequestStatus.Running && row.NumberOfTasks != row.NumberOfTasksCompleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, PullRequestStatus.Timeout)
                .SetProperty(row => row.EndTime, DateTime.Now), stoppingToken);
        if (timedOut == 1 && pr.CurrentRunId != null)
        {
            int updated = await db.Runs.Where(run => run.Id == pr.CurrentRunId && run.FetchStatus == DatabaseFetchStatus.WaitingForJobs)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.FetchStatus, DatabaseFetchStatus.TimedOut)
                    .SetProperty(run => run.FetchError, "The run timed out before all jobs reported completion."), stoppingToken);
            if (updated != 1)
                throw new InvalidOperationException($"Could not record timeout for run {pr.CurrentRunId}.");
        }
        await transaction.CommitAsync(stoppingToken);
    }
}
