using Microsoft.EntityFrameworkCore;
namespace AcceptanceTestsWebAPI.Data;

/// <summary>Persists pull requests, their runs and the jobs used to track run completion.</summary>
public class AcceptanceTestsDbContext(DbContextOptions<AcceptanceTestsDbContext> options) : DbContext(options)
{
    public DbSet<PullRequestEntity> PullRequests => Set<PullRequestEntity>();
    public DbSet<AcceptanceRunEntity> Runs => Set<AcceptanceRunEntity>();
    public DbSet<AcceptanceJobEntity> Jobs => Set<AcceptanceJobEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PullRequestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
        });
        modelBuilder.Entity<AcceptanceRunEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            // A container identifies one run for a given pull request record.
            entity.HasIndex(e => new { e.PullRequestId, e.ContainerName }).IsUnique();
            entity.HasOne(e => e.PullRequest).WithMany().HasForeignKey(e => e.PullRequestId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AcceptanceJobEntity>(entity =>
        {
            // The run and job path together identify one job for completion tracking.
            entity.HasKey(e => new { e.RunId, e.Path });
            entity.HasOne(e => e.Run).WithMany().HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
