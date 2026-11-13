using Microsoft.EntityFrameworkCore;
namespace AcceptanceTestsWebAPI.Data;

/// <summary>Persists pull requests, their runs and the jobs used to track run completion.</summary>
public class AcceptanceTestsDbContext(DbContextOptions<AcceptanceTestsDbContext> options) : DbContext(options)
{
    public DbSet<PullRequestEntity> PullRequests => Set<PullRequestEntity>();
    public DbSet<AcceptanceJobEntity> Jobs => Set<AcceptanceJobEntity>();
    public DbSet<AcceptanceTaskEntity> Tasks => Set<AcceptanceTaskEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PullRequestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
        });
        modelBuilder.Entity<AcceptanceJobEntity>(entity =>
        {
            entity.ToTable("Runs");
            entity.HasKey(e => e.Id);
            // A container identifies one run for a given pull request record.
            entity.HasIndex(e => new { e.PullRequestId, e.ContainerName }).IsUnique();
            entity.HasOne(e => e.PullRequest).WithMany().HasForeignKey(e => e.PullRequestId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AcceptanceTaskEntity>(entity =>
        {
            entity.ToTable("Jobs");
            // The run and job path together identify one job for completion tracking.
            entity.HasKey(e => new { e.RunId, e.Path });
            entity.HasOne(e => e.Run).WithMany().HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
