using Microsoft.EntityFrameworkCore;

namespace JorgenHoc.DataAccess.EfCoreNPlusOne;

/// <summary>
/// Context for the N+1 article. Named <c>AppDbContext</c> to match the article text —
/// the namespace keeps it distinct from the other articles' contexts.
/// </summary>
/// <remarks>
/// The seed script also creates a <c>Tags</c> table for the cartesian-explosion example.
/// It is intentionally not mapped here: the article declares only these three entities,
/// and an unmapped table is harmless.
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Match decimal(18,2) from public/sql/ef-core-n-plus-one-seed.sql. Without this
        // EF Core warns that no precision is configured and silently picks its default.
        modelBuilder.Entity<OrderLine>()
            .Property(l => l.UnitPrice)
            .HasPrecision(18, 2);
    }
}
