using Dapper;
using JorgenHoc.DataAccess.EfCoreVsDapper;
using JorgenHoc.EfCoreVsDapper;
using JorgenHoc.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage; // GetDbTransaction
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Demonstrates the deterministic claims in
// https://www.jorgenhoc.org/en/blog/ef-core-vs-dapper
//
//   1. EF Core and Dapper return identical rows — the difference is who writes the SQL.
//   2. Change tracking issues a targeted UPDATE containing only the changed column.
//   3. EF Core and Dapper can share one transaction and commit or roll back atomically.
//
// Timings live in benchmarks/JorgenHoc.Benchmarks (EfCoreVsDapperBenchmark), not here —
// counts and SQL text are deterministic, wall-clock numbers are not.
//
// Seed the database first — see this folder's README.
//
//   dotnet run                 summary only
//   dotnet run -- --sql        also print every statement EF executes (screenshots)

var printSql = args.Contains("--sql", StringComparer.OrdinalIgnoreCase);

var builder = Host.CreateApplicationBuilder(args);

// See samples/ef-core-n-plus-one/Program.cs for why providers are cleared and nothing
// is gated on the hosting environment.
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

await VerifySeedDataAsync();

// ── 1. Same rows, different authorship ──────────────────────────────────────────────
// The point the article opens with: for a straightforward query the two libraries are
// interchangeable in *result* — what differs is whether the SQL is generated or written.

Console.WriteLine();
Console.WriteLine("1. Simple SELECT — EF Core generates the SQL, Dapper runs yours");
Console.WriteLine("----------------------------------------------------------------");

List<Product> efProducts;
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var query = db.Products.AsNoTracking();

    Console.WriteLine("EF Core generated:");
    Console.WriteLine(Indent(query.ToQueryString()));

    efProducts = await query.ToListAsync();
}

const string selectAllSql = "SELECT Id, Name, Price, CategoryId FROM Products";
Console.WriteLine("Dapper runs exactly what you wrote:");
Console.WriteLine(Indent(selectAllSql));

List<Product> dapperProducts;
using (var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
{
    dapperProducts = (await conn.QueryAsync<Product>(selectAllSql)).ToList();
}

Check(efProducts.Count == 1000 && dapperProducts.Count == 1000,
    $"both return 1,000 rows (EF {efProducts.Count}, Dapper {dapperProducts.Count})");
Check(efProducts.Sum(p => p.Price) == dapperProducts.Sum(p => p.Price),
    "identical data — price checksums match");

// ── 2. JOIN projection — same 500 rows either way ───────────────────────────────────

Console.WriteLine();
Console.WriteLine("2. JOIN projection (Price > 10) — 500 rows, one query, both libraries");
Console.WriteLine("----------------------------------------------------------------------");

List<ProductDto> efJoined;
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var query = db.Products.AsNoTracking()
        .Where(p => p.Price > 10)
        .OrderBy(p => p.Id)
        .Select(p => new ProductDto(p.Id, p.Name, p.Price, p.Category!.Name));

    Console.WriteLine("EF Core generated:");
    Console.WriteLine(Indent(query.ToQueryString()));

    counter.Reset();
    efJoined = await query.ToListAsync();
    Check(counter.Count == 1, "EF's Select projection was a single JOIN statement");
}

const string joinSql = """
    SELECT   p.Id, p.Name, p.Price, c.Name AS CategoryName
    FROM     Products p
    JOIN     Categories c ON c.Id = p.CategoryId
    WHERE    p.Price > @MinPrice
    ORDER BY p.Id
    """;

List<ProductDto> dapperJoined;
using (var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
{
    dapperJoined = (await conn.QueryAsync<ProductDto>(joinSql, new { MinPrice = 10m })).ToList();
}

Check(efJoined.Count == 500, $"exactly 500 rows priced above 10 (got {efJoined.Count})");
Check(efJoined.SequenceEqual(dapperJoined),
    "EF Core's projection and Dapper's handwritten JOIN return element-for-element equal DTOs");

// ── 3. Change tracking: the UPDATE contains only the changed column ─────────────────

Console.WriteLine();
Console.WriteLine("3. Change tracking — modify one property, get a one-column UPDATE");
Console.WriteLine("------------------------------------------------------------------");

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Rolled back below so the sample can re-run against the same seed. The SQL is
    // still generated, executed and counted — rollback discards the effect, not the
    // evidence.
    await using var tx = await db.Database.BeginTransactionAsync();

    var product = await db.Products.FirstAsync(p => p.Id == 1);
    product.Price += 1m;                 // Name, CategoryId untouched

    counter.Reset();
    await db.SaveChangesAsync();

    Check(counter.Count == 1, "SaveChanges issued exactly one statement");
    Console.WriteLine("  run with --sql to see it: UPDATE ... SET [Price] = @p0 — no other column");

    await tx.RollbackAsync();
}

// ── 4. One transaction, both libraries ──────────────────────────────────────────────
// The article's hybrid pattern: EF Core insert + Dapper update on the same connection
// and transaction, committed or discarded as one unit. Proven here by rolling back.

Console.WriteLine();
Console.WriteLine("4. Shared transaction — EF Core write + Dapper write, atomically discarded");
Console.WriteLine("---------------------------------------------------------------------------");

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await using var tx = await db.Database.BeginTransactionAsync();

    db.Products.Add(new Product { Name = "Rollback Widget", Price = 9.99m, CategoryId = 1 });
    await db.SaveChangesAsync();

    // Dapper on the SAME connection and transaction EF Core is using.
    var conn = db.Database.GetDbConnection();
    var dbTx = db.Database.CurrentTransaction!.GetDbTransaction();

    await conn.ExecuteAsync(
        "UPDATE Categories SET Name = @Name WHERE Id = @Id",
        new { Name = "Renamed by Dapper", Id = 1 },
        transaction: dbTx);

    // Inside the transaction both writes are visible...
    var pending = await conn.ExecuteScalarAsync<int>(
        "SELECT COUNT(*) FROM Products WHERE Name = 'Rollback Widget'", transaction: dbTx);
    var renamed = await conn.ExecuteScalarAsync<string>(
        "SELECT Name FROM Categories WHERE Id = 1", transaction: dbTx);

    Check(pending == 1 && renamed == "Renamed by Dapper",
        "inside the transaction, EF's insert and Dapper's update are both visible");

    await tx.RollbackAsync();
}

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var leaked = await db.Products.CountAsync(p => p.Name == "Rollback Widget");
    var categoryName = (await db.Categories.FirstAsync(c => c.Id == 1)).Name;

    Check(leaked == 0 && categoryName == "Category 01",
        "after rollback, neither write persisted — one transaction governed both libraries");
}

Console.WriteLine();
Console.WriteLine("All checks passed.");

// Keep the window open when launched from an IDE — guarded, see the n-plus-one sample.
if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);
}

static string Indent(string sql) =>
    "    " + sql.ReplaceLineEndings(Environment.NewLine + "    ") + Environment.NewLine;

static void Check(bool condition, string claim)
{
    if (!condition)
        throw new InvalidOperationException($"CHECK FAILED: {claim}");
    Console.WriteLine($"  ok: {claim}");
}

// Fail loudly rather than demonstrating against an empty database.
async Task VerifySeedDataAsync()
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (!await db.Database.CanConnectAsync())
        throw new InvalidOperationException(
            $"Cannot connect using '{connectionString}'. Is LocalDB running?");

    var products = await db.Products.CountAsync();
    if (products != 1000)
        throw new InvalidOperationException(
            $"Expected 1,000 seeded products, found {products}. Run seed.sql first — see this folder's README.");

    Console.WriteLine($"Seed data: {products} products.");
}
