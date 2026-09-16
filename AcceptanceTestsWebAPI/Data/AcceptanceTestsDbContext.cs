using Microsoft.EntityFrameworkCore;
namespace AcceptanceTestsWebAPI.Data;

public class AcceptanceTestsDbContext(DbContextOptions<AcceptanceTestsDbContext> options) : DbContext(options)
{
    public DbSet<PullRequestEntity> PullRequests => Set<PullRequestEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PullRequestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
        });
    }
}
