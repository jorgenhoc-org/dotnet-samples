using JorgenHoc.DataAccess.EfCoreOneToMany;
using JorgenHoc.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration; // GetConnectionString
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Statement counts for every loading strategy and CRUD approach in
// https://www.jorgenhoc.org/en/blog/ef-core-one-to-many
//
// Seed the database first — see this folder's README.
//
//   dotnet run                 summary table only
//   dotnet run -- --sql        also print every statement (use this for screenshots)

var printSql = args.Contains("--sql", StringComparer.OrdinalIgnoreCase);

var builder = Host.CreateApplicationBuilder(args);

// EF Core logs through ILoggerFactory, so the host's default console provider prints
// every statement whether or not LogTo is configured. Dropping it means the output below
// is exactly what this program asked for, and statements are not printed twice.
builder.Logging.ClearProviders();

var connectionString = builder.Configuration.GetConnectionString("LocalDbConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'LocalDbConnection' is missing from appsettings.json.");

var counter = new QueryCounter();

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .CountStatements(counter, printSql)
    .EnableSensitiveDataLogging()); // parameter values in the log; never in production

using var host = builder.Build();

var categoryIds = await VerifySeedAndResetAsync();
var (firstCategoryId, secondCategoryId) = (categoryIds[0], categoryIds[1]);

var report = new StatementReport(
    "EF Core one-to-many — 5 categories, 20 products, SQL Server LocalDB");

// ---------------------------------------------------------------------------
// Loading strategies
// ---------------------------------------------------------------------------

await MeasureAsync("Include (eager, single JOIN query)", async db =>
    await db.Categories.Include(c => c.Products).ToListAsync());

await MeasureAsync("Filtered Include (Price > 50)", async db =>
    await db.Categories.Include(c => c.Products.Where(p => p.Price > 50)).ToListAsync());

await MeasureAsync("Explicit loading (parent, then collection)", async db =>
{
    var category = await db.Categories.FindAsync(firstCategoryId);   // statement 1
    await db.Entry(category!)
            .Collection(c => c.Products)
            .LoadAsync();                                            // statement 2
});

// Lazy loading needs its own options: UseLazyLoadingProxies() plus the virtual
// navigation properties on the entities. Everything else in this file runs without
// proxies — exactly the packageless default the article describes.
counter.Reset();
{
    var lazyOptions = new DbContextOptionsBuilder<AppDbContext>();
    lazyOptions.UseSqlServer(connectionString)
               .UseLazyLoadingProxies()
               .CountStatements(counter, printSql)
               .EnableSensitiveDataLogging();

    using var lazyDb = new AppDbContext(lazyOptions.Options);

    var products = await lazyDb.Products.ToListAsync();   // 1 statement, categories NOT loaded
    foreach (var product in products)
        _ = product.Category.Name;                        // triggers a query on first access

    // 6, not 21: the first product of each category triggers one lazy query, and EF Core's
    // navigation fixup then attaches that category to every other tracked product that
    // points at it. With one product per category — or AsNoTracking — this is a full N+1.
    report.Add("Lazy loading (20 products, 5 categories)", counter.Count);
}

// ---------------------------------------------------------------------------
// CRUD across the relationship — the article's three create approaches, and moving
// a product between categories.
// ---------------------------------------------------------------------------

await MeasureAsync("Create: set the FK property", async db =>
{
    db.Products.Add(new Product
    {
        Name = "Widget (FK)",
        Price = 9.99m,
        CategoryId = firstCategoryId, // just set the FK — no query for the parent
    });
    await db.SaveChangesAsync();      // 1 INSERT
});

await MeasureAsync("Create: assign the navigation (Find + add)", async db =>
{
    var category = await db.Categories.FindAsync(firstCategoryId);   // statement 1
    db.Products.Add(new Product
    {
        Name = "Widget (navigation)",
        Price = 9.99m,
        Category = category!,
    });
    await db.SaveChangesAsync();                                     // statement 2
});

await MeasureAsync("Move to another category (Find + save)", async db =>
{
    var product = await db.Products
        .FirstAsync(p => p.Name == "Widget (FK)");                   // statement 1

    // Flip between the first two categories so this is a real change on every run —
    // an unchanged FK would make SaveChanges skip the UPDATE and the count drift.
    product.CategoryId =
        product.CategoryId == firstCategoryId ? secondCategoryId : firstCategoryId;
    await db.SaveChangesAsync();                                     // statement 2
});

report.Print();

// ---------------------------------------------------------------------------
// DeleteBehavior.Restrict, demonstrated
// ---------------------------------------------------------------------------

Console.WriteLine("Deleting a category that still has products (DeleteBehavior.Restrict):");
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var category = await db.Categories.FindAsync(firstCategoryId);
    db.Categories.Remove(category!);
    try
    {
        // No dependents are tracked here, so EF Core sends the DELETE and the database
        // rejects it. (Load the products into the same context first and EF Core throws
        // client-side instead, before any SQL runs.)
        await db.SaveChangesAsync();
        Console.WriteLine("  Unexpected: the delete succeeded — is the seed data present?");
    }
    catch (DbUpdateException)
    {
        Console.WriteLine($"  DbUpdateException — FK_OneToMany_Products_Categories rejected");
        Console.WriteLine($"  the DELETE. No products were orphaned or silently removed;");
        Console.WriteLine($"  handling them first is your code's job, which is the point.");
    }
}

Console.WriteLine();
Console.WriteLine("Counts are provider- and hardware-independent, so yours should match these");
Console.WriteLine("exactly. Eager loading costs one statement no matter the row count; lazy");
Console.WriteLine("loading costs one per parent actually touched — which is why it turns into");
Console.WriteLine("N+1 the moment each row has a different parent.");

// Keep the window open when launched from an IDE, without breaking `dotnet run | tee`
// or CI — an unguarded ReadKey throws when stdin is redirected.
if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);
}

// Every measurement gets a fresh scope, and so a fresh DbContext with an empty change
// tracker. Reusing one context would let entities loaded by an earlier strategy satisfy a
// later one from memory, and the counts would collapse to nothing.
async Task MeasureAsync(string strategy, Func<AppDbContext, Task> work)
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    counter.Reset();
    await work(db);
    report.Add(strategy, counter.Count);
}

// Fail loudly rather than reporting a table of zeros against an empty database, and
// delete the widgets a previous run created so re-runs measure the same work.
async Task<List<int>> VerifySeedAndResetAsync()
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (!await db.Database.CanConnectAsync())
        throw new InvalidOperationException(
            $"Cannot connect using '{connectionString}'. Is LocalDB running?");

    var ids = await db.Categories.OrderBy(c => c.Id).Select(c => c.Id).ToListAsync();
    if (ids.Count < 2)
        throw new InvalidOperationException(
            "Seed data missing. Run seed.sql first — see this folder's README.");

    var removed = await db.Products
        .Where(p => p.Name.StartsWith("Widget"))
        .ExecuteDeleteAsync();

    Console.WriteLine($"Seed data: {ids.Count} categories" +
        (removed > 0 ? $" (removed {removed} widgets from a previous run)." : "."));
    return ids;
}
