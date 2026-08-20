using Microsoft.EntityFrameworkCore;

namespace JorgenHoc.DataAccess.EfCoreRawSql;

/// <summary>
/// Context for the "EF Core raw SQL" article. Deliberately minimal — the article is
/// about what happens when you step OUTSIDE the model.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Own SQL schema per article — matches samples/ef-core-raw-sql/seed.sql. Raw SQL
        // strings must name it explicitly: SELECT * FROM RawSql.Products.
        modelBuilder.HasDefaultSchema("RawSql");

        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Name).HasMaxLength(200);
            entity.Property(p => p.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Category>()
            .Property(c => c.Name).HasMaxLength(200);
    }
}
