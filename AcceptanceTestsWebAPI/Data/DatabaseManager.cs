using AcceptanceTestsWebAPI.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// This is a background task that runs to make sure pull requests are handled 
/// correctly. Such as terminating PRs that run too long and checking when all 
/// tasks have returned.
/// </summary>
public class DatabaseManager : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseManager> _logger;
    private readonly TimeSpan _period = TimeSpan.FromSeconds(15); // Set your interval here

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

    private async Task DoWorkAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Test");
        // Create a scope to safely resolve your EF DbContext
        using (IServiceScope scope = _scopeFactory.CreateScope())
        {
            AcceptanceTestsDbContext db = scope.ServiceProvider.GetRequiredService<AcceptanceTestsDbContext>();

            List<PullRequestEntity> pullRequestsRunning = await db.PullRequests.Where(pr => pr.Status == Shared.Models.PullRequestStatus.Running).ToListAsync(stoppingToken);

            _logger.LogInformation(pullRequestsRunning.Count.ToString());
            bool changes = false;
            foreach(PullRequestEntity pr in pullRequestsRunning)
            {
                int minutesPassed = (DateTime.Now - pr.StartTime).Minutes;
                if (pr.NumberOfTasks == pr.NumberOfTasksCompleted)
                {
                    pr.Status = Shared.Models.PullRequestStatus.Closed;
                    pr.EndTime = DateTime.Now;
                    changes = true;
                } else if (minutesPassed > 30)
                {
                    pr.Status = Shared.Models.PullRequestStatus.Timeout;
                    pr.EndTime = DateTime.Now;
                    changes = true;
                }
            }
            if (changes)
                await db.SaveChangesAsync(stoppingToken);
        }
    }
}
