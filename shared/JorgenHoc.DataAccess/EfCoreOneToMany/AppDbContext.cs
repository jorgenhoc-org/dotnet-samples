using Microsoft.EntityFrameworkCore;

namespace JorgenHoc.DataAccess.EfCoreOneToMany;

/// <summary>
/// Context for the "EF Core one-to-many relationships" article. Named
/// <c>AppDbContext</c> to match the article text — the namespace keeps it distinct from
/// the other articles' contexts.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The ef-core-vs-dapper article already owns dbo.Products/dbo.Categories in the
        // shared JorgenHocSamples database, so this article's tables live in their own
        // SQL schema. Matches samples/ef-core-one-to-many/seed.sql.
        modelBuilder.HasDefaultSchema("OneToMany");

        // The article's Fluent API section, verbatim.
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Name)
                  .HasMaxLength(200)
                  .IsRequired();

            entity.Property(p => p.Price)
                  .HasPrecision(18, 2);

            entity.HasOne(p => p.Category)           // Product has one Category
                  .WithMany(c => c.Products)          // Category has many Products
                  .HasForeignKey(p => p.CategoryId)   // FK is CategoryId
                  .IsRequired()                       // Cannot be null
                  .OnDelete(DeleteBehavior.Restrict); // Don't cascade delete
        });
    }
}
