using Microsoft.EntityFrameworkCore;

namespace JorgenHoc.MigrationsWalkthrough;

/// <summary>
/// Context for the "EF Core migrations walkthrough" article. Unlike the other EF samples
/// this one is self-contained — entity, context, and the real generated Migrations/
/// folder live together, because migrations belong physically with their project. It
/// also targets its OWN database (JorgenHocSamples_Migrations) so the article's
/// destructive steps — database update 0, database drop — can never touch the shared
/// sample database.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Price).HasPrecision(18, 2);

            // Added by the AddIndexOnProductName migration.
            entity.HasIndex(p => p.Name).HasDatabaseName("IX_Products_Name");
        });
    }
}
