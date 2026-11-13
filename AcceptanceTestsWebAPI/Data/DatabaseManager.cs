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

            List<AcceptanceJobEntity> pullRequestsRunning = await db.PullRequests.AsNoTracking().Where(pr => pr.IsCurrent && pr.Status == PullRequestStatus.Running).ToListAsync(stoppingToken);

            foreach (AcceptanceJobEntity pr in pullRequestsRunning)
            {
                double minutesPassed = (DateTime.Now - pr.StartTime).TotalMinutes;
                if (pr.NumberOfTasks > 0 && pr.NumberOfTasks == pr.NumberOfTasksCompleted)
                    await CloseCompletedRunAsync(db, pr, stoppingToken);
                else if (minutesPassed > 30)
                    await TimeOutRunAsync(db, pr, stoppingToken);
            }
        }
    }

    private async Task CloseCompletedRunAsync(AcceptanceTestsDbContext db, AcceptanceJobEntity pr, CancellationToken stoppingToken)
    {
        if (pr.ContainerName == null)
        {
            _logger.LogWarning("Cannot fetch databases for job {Id}: the storage container is missing.", pr.Id);
            return;
        }

        int closed = await db.PullRequests.Where(row => row.Id == pr.Id && row.IsCurrent &&
                row.Status == PullRequestStatus.Running && row.NumberOfTasks > 0 &&
                row.NumberOfTasks == row.NumberOfTasksCompleted && row.FetchStatus == DatabaseFetchStatus.WaitingForJobs)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, PullRequestStatus.Closed)
                .SetProperty(row => row.EndTime, DateTime.Now)
                .SetProperty(row => row.FetchStatus, DatabaseFetchStatus.Pending), stoppingToken);
        if (closed == 0 && await db.PullRequests.AnyAsync(row => row.Id == pr.Id && row.IsCurrent &&
                row.Status == PullRequestStatus.Running && row.NumberOfTasks > 0 &&
                row.NumberOfTasks == row.NumberOfTasksCompleted && row.FetchStatus != DatabaseFetchStatus.WaitingForJobs, stoppingToken))
            throw new InvalidOperationException($"Could not queue database fetch for run {pr.Id}.");
    }

    private static async Task TimeOutRunAsync(AcceptanceTestsDbContext db, AcceptanceJobEntity pr, CancellationToken stoppingToken)
    {
        var incomplete = db.PullRequests.Where(row => row.Id == pr.Id && row.IsCurrent &&
            row.Status == PullRequestStatus.Running && row.NumberOfTasks != row.NumberOfTasksCompleted);
        if (pr.ContainerName == null)
        {
            await incomplete.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, PullRequestStatus.Timeout)
                .SetProperty(row => row.EndTime, DateTime.Now), stoppingToken);
            return;
        }

        int timedOut = await incomplete.Where(row => row.FetchStatus == DatabaseFetchStatus.WaitingForJobs)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, PullRequestStatus.Timeout)
                .SetProperty(row => row.EndTime, DateTime.Now)
                .SetProperty(row => row.FetchStatus, DatabaseFetchStatus.TimedOut)
                .SetProperty(row => row.FetchError, "The run timed out before all jobs reported completion."), stoppingToken);
        if (timedOut == 0 && await incomplete.AnyAsync(row => row.FetchStatus != DatabaseFetchStatus.WaitingForJobs, stoppingToken))
            throw new InvalidOperationException($"Could not record timeout for run {pr.Id}.");
    }
}
