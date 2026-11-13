using Microsoft.EntityFrameworkCore;
namespace AcceptanceTestsWebAPI.Data;

/// <summary>Persists pull request attempts and the tasks used to track completion.</summary>
public class AcceptanceTestsDbContext(DbContextOptions<AcceptanceTestsDbContext> options) : DbContext(options)
{
    public DbSet<AcceptanceJobEntity> PullRequests => Set<AcceptanceJobEntity>();
    public DbSet<AcceptanceTaskEntity> Tasks => Set<AcceptanceTaskEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AcceptanceJobEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.HasIndex(e => e.Name).IsUnique().HasFilter("\"IsCurrent\" = 1");
            entity.HasIndex(e => new { e.PullRequest, e.Commit }).IsUnique().HasFilter("\"IsCurrent\" = 1");
            entity.HasIndex(e => new { e.PullRequest, e.Commit, e.ContainerName }).IsUnique();
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
