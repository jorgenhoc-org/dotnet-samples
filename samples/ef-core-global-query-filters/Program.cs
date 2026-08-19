using JorgenHoc.DataAccess.EfCoreGlobalQueryFilters;
using JorgenHoc.Diagnostics;
using JorgenHoc.GlobalQueryFiltersSample;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Linq.Expressions;

// Asserted behaviour for every claim in
// https://www.jorgenhoc.org/en/blog/ef-core-global-query-filters
//
// Every line of output is a passing check — a failed check throws. Contexts are built
// by hand (no host) because two of the experiments need several context instances with
// DIFFERENT tenant providers, which is exactly where the interesting bugs live.
//
//   dotnet run                 checks only
//   dotnet run -- --sql        also print every statement (use this for screenshots)

var printSql = args.Contains("--sql", StringComparer.OrdinalIgnoreCase);

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .Build();

var connectionString = config.GetConnectionString("LocalDbConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'LocalDbConnection' is missing from appsettings.json.");

var tenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
var tenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

var counter = new QueryCounter();
var checksPassed = 0;

DbContextOptions<T> Options<T>() where T : DbContext
{
    var optionsBuilder = new DbContextOptionsBuilder<T>();
    optionsBuilder.UseSqlServer(connectionString)
                  .CountStatements(counter, printSql)
                  .EnableSensitiveDataLogging();
    return optionsBuilder.Options;
}

await VerifySeedAndResetAsync();

// ---------------------------------------------------------------------------
// Soft delete: the filter is invisible in application code
// ---------------------------------------------------------------------------

Console.WriteLine("Soft delete");
{
    using var db = new AppDbContext(Options<AppDbContext>(), new MutableTenantProvider(tenantA));

    var posts = await db.Posts.ToListAsync();
    Check(posts.Count == 2, "plain Posts query returns 2 rows — the soft-deleted one is filtered out");

    var blogs = await db.Blogs.Include(b => b.Posts).ToListAsync();
    Check(blogs.Count == 1, "deleted blog filtered from the Blogs query itself");
    Check(blogs[0].Posts.Count == 2,
        "Include(b => b.Posts) filtered the JOIN too — navigation never sees deleted posts");

    var all = await db.Posts.IgnoreQueryFilters().ToListAsync();
    Check(all.Count == 3 && all.Count(p => p.IsDeleted) == 1,
        "IgnoreQueryFilters() returns all 3, exactly 1 flagged IsDeleted");

    // The fixup trap: that IgnoreQueryFilters() call left the deleted post TRACKED.
    // Re-running the Include query filters the JOIN exactly as before — but change
    // tracker fixup attaches the already-tracked deleted post to the navigation anyway.
    var again = await db.Blogs.Include(b => b.Posts).FirstAsync();
    Check(again.Posts.Count == 3,
        "fixup trap: after IgnoreQueryFilters() in the same context, the navigation shows 3 posts — " +
        "the filter is SQL-level, but tracked entities are fixed up regardless");
}

// Remove() becomes an UPDATE, and the restore pattern brings the row back.
{
    using var db = new AppDbContext(Options<AppDbContext>(), new MutableTenantProvider(tenantA));

    var post = await db.Posts.FirstAsync(p => p.Title == "Post Two");
    db.Posts.Remove(post);
    counter.Reset();
    await db.SaveChangesAsync();
    Check(counter.Count == 1, "Remove() + SaveChanges executed exactly 1 statement (an UPDATE, not a DELETE)");

    Check(await db.Posts.CountAsync() == 1, "the soft-deleted post vanished from filtered queries");

    var stillThere = await db.Posts.IgnoreQueryFilters().FirstAsync(p => p.Id == post.Id);
    Check(stillThere.IsDeleted && stillThere.DeletedAt is not null,
        "the row survived with IsDeleted = true and DeletedAt stamped");

    // The article's restore pattern.
    stillThere.IsDeleted = false;
    stillThere.DeletedAt = null;
    stillThere.DeletedBy = null;
    db.Entry(stillThere).State = EntityState.Modified;
    await db.SaveChangesAsync();
    Check(await db.Posts.CountAsync() == 2, "restore: IsDeleted = false makes it visible again");
}

// ---------------------------------------------------------------------------
// Multi-tenancy: isolation, navigations, and query-time evaluation
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("Multi-tenancy");
{
    var provider = new MutableTenantProvider(tenantA);
    using var db = new AppDbContext(Options<AppDbContext>(), provider);

    var invoices = await db.Invoices.Include(i => i.LineItems).ToListAsync();
    Check(invoices.Count == 1 && invoices[0].Amount == 100.00m,
        "tenant A sees exactly its 1 active invoice (the deleted one and tenant B's are filtered)");
    Check(invoices[0].LineItems.Count == 2,
        "the line item deliberately stamped with tenant B is filtered out of Include() — no cross-tenant leak");

    Check(await db.Invoices.IgnoreQueryFilters().CountAsync() == 3,
        "IgnoreQueryFilters() drops BOTH filters: all tenants and the deleted invoice, 3 rows");

    // EF Core 10 named filters: drop ONLY the soft-delete filter, keep tenant isolation.
    var withDeleted = await db.Invoices.IgnoreQueryFilters(["SoftDelete"]).ToListAsync();
    Check(withDeleted.Count == 2 && withDeleted.All(i => i.TenantId == tenantA),
        "IgnoreQueryFilters([\"SoftDelete\"]) shows tenant A's deleted invoice but still hides tenant B (named filters, EF Core 10)");

    // The filter reads the provider at query time, not at construction time.
    provider.TenantId = tenantB;
    var nowB = await db.Invoices.ToListAsync();
    Check(nowB.Count == 1 && nowB[0].Amount == 300.00m,
        "flipping the provider on the SAME context switches results to tenant B — evaluated per query");

    // New tenant entities are stamped automatically in SaveChanges.
    provider.TenantId = tenantA;
    db.Invoices.Add(new Invoice { Amount = 1.00m, Currency = "TST", IssuedAt = DateTime.UtcNow });
    await db.SaveChangesAsync();
    var stamped = await db.Invoices.FirstAsync(i => i.Currency == "TST");
    Check(stamped.TenantId == tenantA, "Added invoice was auto-stamped with the current tenant");

    // Clean up immediately — and note that ExecuteDelete goes straight to SQL, so the
    // SaveChanges soft-delete interception never sees it: this is a REAL delete.
    await db.Invoices.IgnoreQueryFilters().Where(i => i.Currency == "TST").ExecuteDeleteAsync();
    Check(await db.Invoices.IgnoreQueryFilters().CountAsync(i => i.Currency == "TST") == 0,
        "ExecuteDelete bypasses the SaveChanges interception — a hard DELETE, gone even from IgnoreQueryFilters()");
}

// A SECOND context instance with a different provider must see tenant B. This is the
// control for the experiment below.
{
    using var db = new AppDbContext(Options<AppDbContext>(), new MutableTenantProvider(tenantB));
    var invoices = await db.Invoices.ToListAsync();
    Check(invoices.Count == 1 && invoices[0].Amount == 300.00m,
        "new AppDbContext with a tenant-B provider sees tenant B (filter re-bound per instance)");
}

// ---------------------------------------------------------------------------
// The trap: building the filter with Expression.Constant(provider)
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("The Expression.Constant(provider) trap");
{
    // BuggyTenantContext builds its tenant filter around the provider INSTANCE instead
    // of reaching it through the context. EF Core caches the model on first use, so the
    // first provider is baked into the cached filter forever.
    using (var first = new BuggyTenantContext(Options<BuggyTenantContext>(), new MutableTenantProvider(tenantA)))
    {
        var rows = await first.Invoices.ToListAsync();
        Check(rows.Count == 1 && rows[0].Amount == 100.00m, "first buggy context (provider A) sees tenant A — looks fine");
    }

    using (var second = new BuggyTenantContext(Options<BuggyTenantContext>(), new MutableTenantProvider(tenantB)))
    {
        var rows = await second.Invoices.ToListAsync();
        Check(rows.Count == 1 && rows[0].Amount == 100.00m,
            "SECOND buggy context (provider B!) STILL sees tenant A — the first provider is baked into the cached model");
    }
}

// ---------------------------------------------------------------------------
// Owned entities cannot carry their own filter
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("Owned entities");
{
    using var db = new OwnedFilterContext(Options<OwnedFilterContext>());
    try
    {
        _ = await db.Customers.ToListAsync(); // first use builds the model
        Check(false, "unreachable — the model build should have thrown");
    }
    catch (InvalidOperationException ex)
    {
        Check(true, $"HasQueryFilter on an owned type throws InvalidOperationException:");
        Console.WriteLine($"       \"{ex.Message[..Math.Min(100, ex.Message.Length)]}...\"");
    }

    // And the supported shape: the owner's filter covers the owned type.
    using var good = new AppDbContext(Options<AppDbContext>(), new MutableTenantProvider(tenantA));
    var customers = await good.Customers.ToListAsync();
    Check(customers.Count == 1 && customers[0].BillingAddress.City == "Springfield",
        "owned Address rides along under the Customer filter — deleted customer (and its address) hidden");
}

// ---------------------------------------------------------------------------
// The partial index is real
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("Index");
{
    using var db = new AppDbContext(Options<AppDbContext>(), new MutableTenantProvider(tenantA));
    var definition = await db.Database
        .SqlQuery<string>($@"SELECT CONCAT(i.name, ' -> ', i.filter_definition) AS [Value]
                             FROM sys.indexes i
                             WHERE i.name = 'IX_QueryFilters_Posts_Active' AND i.has_filter = 1")
        .FirstAsync();
    Check(true, $"partial index read back from sys.indexes: {definition}");
}

Console.WriteLine();
Console.WriteLine($"All {checksPassed} checks passed. Filters are SQL-level: rows you should not");
Console.WriteLine("see are never loaded — but the tenant filter must reference the provider");
Console.WriteLine("THROUGH the context, or the cached model quietly pins your first tenant.");

// Keep the window open when launched from an IDE, without breaking `dotnet run | tee`
// or CI — an unguarded ReadKey throws when stdin is redirected.
if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);
}

void Check(bool condition, string claim)
{
    if (!condition)
        throw new InvalidOperationException($"CHECK FAILED: {claim}");
    checksPassed++;
    Console.WriteLine($"  [OK] {claim}");
}

// Fail loudly rather than checking claims against an empty database, and undo what the
// demos above write so every run starts from the seeded state.
async Task VerifySeedAndResetAsync()
{
    using var db = new AppDbContext(Options<AppDbContext>(), new MutableTenantProvider(tenantA));

    if (!await db.Database.CanConnectAsync())
        throw new InvalidOperationException(
            $"Cannot connect using '{connectionString}'. Is LocalDB running?");

    if (!await db.Posts.IgnoreQueryFilters().AnyAsync())
        throw new InvalidOperationException(
            "Seed data missing. Run seed.sql first — see this folder's README.");

    await db.Database.ExecuteSqlRawAsync("""
        UPDATE QueryFilters.Posts SET IsDeleted = 0, DeletedAt = NULL, DeletedBy = NULL WHERE Title = 'Post Two';
        DELETE FROM QueryFilters.Invoices WHERE Currency = 'TST';
        """);

    Console.WriteLine("Seed data verified; rows mutated by previous runs reset.");
    Console.WriteLine();
}

namespace JorgenHoc.GlobalQueryFiltersSample
{

/// <summary>Mutable so the query-time-evaluation claim is observable.</summary>
public class MutableTenantProvider(Guid tenantId) : ITenantProvider
{
    public Guid TenantId { get; set; } = tenantId;
}

/// <summary>
/// The broken pattern, kept alive on purpose: the tenant filter references the provider
/// instance directly (<c>Expression.Constant(provider)</c>). EF Core caches the model
/// for this context type on first use — with the first provider baked into the filter.
/// Every later instance silently filters by tenant of whoever constructed the first one.
/// </summary>
public class BuggyTenantContext(DbContextOptions<BuggyTenantContext> options, ITenantProvider tenantProvider)
    : DbContext(options)
{
    private readonly ITenantProvider _tenantProvider = tenantProvider;

    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("QueryFilters");
        modelBuilder.Entity<Invoice>().Property(i => i.Amount).HasPrecision(18, 2);
        modelBuilder.Ignore<InvoiceLineItem>();

        var param = Expression.Parameter(typeof(Invoice), "e");
        var isDeleted = Expression.Property(param, nameof(SoftDeletableEntity.IsDeleted));
        var tenantId = Expression.Property(param, nameof(TenantEntity.TenantId));

        // The bug: a constant of the PROVIDER, not of the context. Nothing rewrites
        // this per instance, so the cached model keeps the first provider forever.
        var currentTenant = Expression.Property(
            Expression.Constant(_tenantProvider), nameof(ITenantProvider.TenantId));

        var filter = Expression.AndAlso(
            Expression.Not(isDeleted), Expression.Equal(tenantId, currentTenant));
        modelBuilder.Entity<Invoice>().HasQueryFilter(Expression.Lambda(filter, param));
    }
}

/// <summary>The article's owned-entity claim: HasQueryFilter on an owned type throws.</summary>
public class OwnedFilterContext(DbContextOptions<OwnedFilterContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("QueryFilters");
        modelBuilder.Entity<Customer>().OwnsOne(c => c.BillingAddress);
        modelBuilder.Entity<Address>().HasQueryFilter(a => a.City != "");
    }
}

}
