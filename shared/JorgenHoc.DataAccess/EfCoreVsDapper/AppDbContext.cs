using Microsoft.EntityFrameworkCore;

namespace JorgenHoc.DataAccess.EfCoreVsDapper;

/// <summary>
/// Context for the "EF Core vs Dapper" article. Named <c>AppDbContext</c> to match the
/// article text — the namespace keeps it distinct from the other articles' contexts.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Match decimal(18,2) from samples/ef-core-vs-dapper/seed.sql. Without this
        // EF Core warns that no precision is configured and silently picks its default.
        modelBuilder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);
    }
}
