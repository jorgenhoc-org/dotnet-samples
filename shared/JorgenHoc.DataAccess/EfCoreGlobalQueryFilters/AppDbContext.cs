using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace JorgenHoc.DataAccess.EfCoreGlobalQueryFilters;

/// <summary>
/// Context for the "EF Core global query filters" article: soft delete on everything
/// that inherits <see cref="SoftDeletableEntity"/>, tenant isolation on everything that
/// inherits <see cref="TenantEntity"/>, both built as expression trees in one loop.
///
/// The tenant filter reaches the provider THROUGH the context instance
/// (<c>Expression.Constant(this)</c> → field → property). That detail is load-bearing:
/// EF Core caches the model per context type and rewrites references to the
/// model-building context instance to the currently executing one. Referencing the
/// provider directly (<c>Expression.Constant(_tenantProvider)</c>) bakes the FIRST
/// instance's provider into the cached model — every later context instance silently
/// keeps filtering by it. The sample proves both behaviours.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenantProvider)
    : DbContext(options)
{
    private readonly ITenantProvider _tenantProvider = tenantProvider;

    public DbSet<Blog> Blogs => Set<Blog>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Own SQL schema per article — matches samples/ef-core-global-query-filters/seed.sql.
        modelBuilder.HasDefaultSchema("QueryFilters");

        modelBuilder.Entity<Invoice>().Property(i => i.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<InvoiceLineItem>().Property(li => li.UnitPrice).HasPrecision(18, 2);

        // Owned types
        modelBuilder.Entity<Customer>().OwnsOne(c => c.BillingAddress);

        // One loop configures every filter; new entities inherit a base class and are done.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Skip owned entities — they cannot have independent filters
            if (entityType.IsOwned())
                continue;

            var clrType = entityType.ClrType;
            var param = Expression.Parameter(clrType, "e");

            if (typeof(SoftDeletableEntity).IsAssignableFrom(clrType))
            {
                // Named filter (EF Core 10): !e.IsDeleted. The name lets a query drop
                // JUST this filter — IgnoreQueryFilters(["SoftDelete"]) — while tenant
                // isolation below stays applied.
                var isDeleted = Expression.Property(
                    Expression.Convert(param, typeof(SoftDeletableEntity)),
                    nameof(SoftDeletableEntity.IsDeleted));
                modelBuilder.Entity(clrType).HasQueryFilter(
                    "SoftDelete", Expression.Lambda(Expression.Not(isDeleted), param));
            }

            if (typeof(TenantEntity).IsAssignableFrom(clrType))
            {
                // Second named filter on the same entity; EF Core ANDs them together:
                // e.TenantId == _tenantProvider.TenantId
                var tenantId = Expression.Property(
                    Expression.Convert(param, typeof(TenantEntity)),
                    nameof(TenantEntity.TenantId));

                // Through the context constant on purpose — see the class remarks.
                var provider = Expression.Field(Expression.Constant(this), nameof(_tenantProvider));
                var currentTenant = Expression.Property(provider, nameof(ITenantProvider.TenantId));

                modelBuilder.Entity(clrType).HasQueryFilter(
                    "Tenant",
                    Expression.Lambda(Expression.Equal(tenantId, currentTenant), param));
            }
        }

        // Performance indexes, mirrored in seed.sql (schema is created there, not by
        // migrations — the sample reads the partial index back from sys.indexes).
        modelBuilder.Entity<Post>()
            .HasIndex(p => p.IsDeleted)
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_QueryFilters_Posts_Active");

        modelBuilder.Entity<Invoice>()
            .HasIndex(i => new { i.TenantId, i.IsDeleted })
            .HasDatabaseName("IX_QueryFilters_Invoices_Tenant_Active");
    }

    // All four public SaveChanges entry points funnel through these two overloads.
    // Overriding only SaveChangesAsync(CancellationToken) — as the article originally
    // did — leaves the synchronous SaveChanges() path unintercepted, and a plain
    // Remove() there becomes a real DELETE.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditRules()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            // Auto-assign tenant on new tenant entities
            if (entry.Entity is TenantEntity tenantEntity
                && entry.State == EntityState.Added)
            {
                tenantEntity.TenantId = _tenantProvider.TenantId;
            }

            // Intercept hard deletes, convert to soft deletes
            if (entry.Entity is SoftDeletableEntity softEntity
                && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                softEntity.IsDeleted = true;
                softEntity.DeletedAt = now;
            }

            // Audit timestamps
            if (entry.Entity is AuditableEntity auditEntity)
            {
                if (entry.State == EntityState.Added)
                    auditEntity.CreatedAt = now;
                if (entry.State is EntityState.Added or EntityState.Modified)
                    auditEntity.UpdatedAt = now;
            }
        }
    }
}
