using Microsoft.EntityFrameworkCore;
namespace AcceptanceTestsWebAPI.Data;

/// <summary>Persists pull request attempts and the tasks used to track completion.</summary>
public class AcceptanceTestsDbContext(DbContextOptions<AcceptanceTestsDbContext> options) : DbContext(options)
{
    public DbSet<AcceptanceJobEntity> PullRequests => Set<AcceptanceJobEntity>();
    public DbSet<AcceptanceTaskEntity> Tasks => Set<AcceptanceTaskEntity>();
    public IQueryable<AcceptanceJobEntity> CurrentPullRequests => PullRequests.Where(run =>
        run.Id == PullRequests.Where(other =>
                other.PullRequestNumber == run.PullRequestNumber && other.Commit == run.Commit)
            .OrderByDescending(other => other.StartTime)
            .ThenByDescending(other => other.Id)
            .Select(other => other.Id)
            .First());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AcceptanceJobEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.HasIndex(e => new { e.PullRequestNumber, e.Commit, e.StartTime });
            entity.HasIndex(e => new { e.PullRequestNumber, e.Commit, e.Name }).IsUnique();
        });
        modelBuilder.Entity<AcceptanceTaskEntity>(entity =>
        {
            entity.ToTable("Jobs");
            // The job ID and task path together identify one task.
            entity.HasKey(e => new { e.RunId, e.Path });
            entity.HasOne(e => e.Run).WithMany().HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
