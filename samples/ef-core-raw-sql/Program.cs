using JorgenHoc.DataAccess.EfCoreRawSql;
using JorgenHoc.Diagnostics;
using JorgenHoc.RawSqlSample;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Data;

// Asserted behaviour for every claim in
// https://www.jorgenhoc.org/en/blog/ef-core-raw-sql
//
// Every line of output is a passing check — a failed check throws. The injection demo
// runs a real (read-only) injection against the vulnerable pattern so the difference
// between FromSqlRaw concatenation and FromSqlInterpolated is data, not prose.
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

var counter = new QueryCounter();
var checksPassed = 0;

AppDbContext NewDb()
{
    var builder = new DbContextOptionsBuilder<AppDbContext>();
    builder.UseSqlServer(connectionString)
           .CountStatements(counter, printSql)
           .EnableSensitiveDataLogging();
    return new AppDbContext(builder.Options);
}

await VerifySeedAndResetAsync();

// ---------------------------------------------------------------------------
// FromSqlRaw with parameters, and composing LINQ on top
// ---------------------------------------------------------------------------

Console.WriteLine("FromSqlRaw");
{
    using var db = NewDb();

    var positional = await db.Products
        .FromSqlRaw("SELECT * FROM RawSql.Products WHERE CategoryId = {0}", 1)
        .ToListAsync();
    Check(positional.Count == 4, "positional parameter {0}: 4 products in category 1");

    var named = await db.Products
        .FromSqlRaw("SELECT * FROM RawSql.Products WHERE CategoryId = @categoryId",
            new SqlParameter("@categoryId", 1))
        .ToListAsync();
    Check(named.Count == 4, "named SqlParameter: same 4 products");

    var composed = db.Products
        .FromSqlRaw("SELECT * FROM RawSql.Products WHERE CategoryId = {0}", 1)
        .Where(p => p.Price > 50)
        .OrderBy(p => p.Name)
        .Include(p => p.Category);

    Check(composed.ToQueryString().Contains("FROM ("),
        "composing LINQ wraps the raw SQL in a subquery — visible in ToQueryString()");

    counter.Reset();
    var rows = await composed.ToListAsync();
    Check(rows.Count == 2 && counter.Count == 1 && rows.All(p => p.Category.Name == "Boards"),
        "raw base + Where + OrderBy + Include ran as ONE statement, 2 rows over $50, join populated");
}

// ---------------------------------------------------------------------------
// The injection demo: same input, two APIs, opposite outcomes
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("SQL injection: input = \"' OR '1'='1\"");
{
    using var db = NewDb();
    var evil = "' OR '1'='1";

    // The vulnerable pattern from the article's warning — DO NOT do this. The input
    // terminates the string literal and the OR makes the predicate always true. Note
    // that the EF Core analyzer flags this line with EF1003; the suppression is here
    // because being vulnerable is this demo's job.
#pragma warning disable EF1003
    var leaked = await db.Products
        .FromSqlRaw("SELECT * FROM RawSql.Products WHERE Name = '" + evil + "'")
        .ToListAsync();
#pragma warning restore EF1003
    Check(leaked.Count == 12,
        "concatenated FromSqlRaw: the injection SUCCEEDED — all 12 rows leaked, not 0");

    // Identical input through FromSqlInterpolated: it becomes @p0, matched literally.
    var safe = await db.Products
        .FromSqlInterpolated($"SELECT * FROM RawSql.Products WHERE Name = {evil}")
        .ToListAsync();
    Check(safe.Count == 0,
        "FromSqlInterpolated with the SAME input: 0 rows — the value became parameter @p0");
}

// ---------------------------------------------------------------------------
// SqlQueryRaw<T>: window functions and CTEs to non-entity types
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("SqlQueryRaw<T> — window functions and CTEs");
{
    using var db = NewDb();

    // ROW_NUMBER() returns bigint on SQL Server. Materializing it into an int property
    // does NOT coerce silently — prove what actually happens, then do it right.
    try
    {
        _ = await db.Database
            .SqlQueryRaw<RankedProduct>("""
                SELECT p.Id, p.Name, p.Price, p.CategoryId,
                       ROW_NUMBER() OVER (PARTITION BY p.CategoryId ORDER BY p.Price DESC) AS PriceRank
                FROM RawSql.Products p
                """)
            .ToListAsync();
        Check(false, "unreachable — bigint into int should not materialize");
    }
    catch (Exception ex) when (ex is not InvalidOperationException { Message: var m }
                               || !m.StartsWith("CHECK", StringComparison.Ordinal))
    {
        Check(true, $"ROW_NUMBER (bigint) into an int property throws {ex.GetType().Name} — CAST it in the SQL");
    }

    var ranked = await db.Database
        .SqlQueryRaw<RankedProduct>("""
            SELECT p.Id, p.Name, p.Price, p.CategoryId,
                   CAST(ROW_NUMBER() OVER (PARTITION BY p.CategoryId ORDER BY p.Price DESC) AS int) AS PriceRank
            FROM RawSql.Products p
            """)
        .ToListAsync();

    var winners = ranked.Where(r => r.PriceRank == 1).Select(r => r.Name).Order().ToList();
    Check(ranked.Count == 12
          && winners.SequenceEqual(["Cable Active", "Dev Board Max", "Display Ultra"]),
        "with CAST(... AS int): 12 rows, rank 1 = the most expensive product of each category");

    var summaries = await db.Database
        .SqlQueryRaw<CategorySummary>("""
            WITH ProductCounts AS (
                SELECT CategoryId, COUNT(*) AS ProductCount, AVG(Price) AS AvgPrice
                FROM RawSql.Products
                GROUP BY CategoryId
            )
            SELECT c.Id, c.Name, pc.ProductCount, pc.AvgPrice
            FROM RawSql.Categories c
            JOIN ProductCounts pc ON c.Id = pc.CategoryId
            ORDER BY pc.AvgPrice DESC
            """)
        .ToListAsync();
    Check(summaries.Count == 3
          && summaries[0] is { Name: "Displays", ProductCount: 4, AvgPrice: 105.00m }
          && summaries[^1] is { Name: "Cables", AvgPrice: 40.00m },
        "CTE into a DTO: Displays averages 105.00, Cables 40.00, four products each");
}

// ---------------------------------------------------------------------------
// Stored procedures
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("Stored procedures");
{
    using var db = NewDb();

    var fromProc = await db.Products
        .FromSqlRaw("EXEC RawSql.GetProductsByCategory @CategoryId = {0}", 1)
        .ToListAsync();
    Check(fromProc.Count == 4, "FromSqlRaw over EXEC materializes 4 tracked Product entities");

    try
    {
        _ = await db.Products
            .FromSqlRaw("EXEC RawSql.GetProductsByCategory @CategoryId = {0}", 1)
            .Where(p => p.Price > 50)
            .ToListAsync();
        Check(false, "unreachable — EXEC cannot be wrapped in a subquery");
    }
    catch (InvalidOperationException)
    {
        Check(true, "composing LINQ on EXEC throws InvalidOperationException — procs can't be subqueried");
    }

    var totalCount = new SqlParameter("@TotalCount", SqlDbType.Int)
    {
        Direction = ParameterDirection.Output,
    };
    await db.Database.ExecuteSqlRawAsync(
        "EXEC RawSql.CountProductsAbove @MinPrice = {0}, @TotalCount = @TotalCount OUTPUT",
        50m, totalCount);
    Check((int)totalCount.Value == 6, "OUTPUT parameter came back: 6 products above $50");
}

// ---------------------------------------------------------------------------
// Tracking, ToQueryString, and non-query SQL
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("Tracking and non-queries");
{
    using (var db = NewDb())
    {
        _ = await db.Products
            .FromSqlInterpolated($"SELECT * FROM RawSql.Products WHERE Price > {10m}")
            .AsNoTracking()
            .ToListAsync();
        Check(!db.ChangeTracker.Entries().Any(),
            "raw SQL + AsNoTracking(): change tracker stays empty");
    }

    using (var db = NewDb())
    {
        _ = await db.Products
            .FromSqlInterpolated($"SELECT * FROM RawSql.Products WHERE Price > {10m}")
            .ToListAsync();
        Check(db.ChangeTracker.Entries().Count() == 11,
            "the same raw query WITHOUT AsNoTracking tracks all 11 returned entities");
    }

    using (var db = NewDb())
    {
        var query = db.Products
            .Where(p => p.CategoryId == 1)
            .OrderBy(p => p.Price)
            .Select(p => new { p.Id, p.Name, p.Price });

        counter.Reset();
        var sql = query.ToQueryString();
        Check(sql.Contains("ORDER BY") && counter.Count == 0,
            "ToQueryString() shows the SQL without executing anything — 0 statements");
    }

    using (var db = NewDb())
    {
        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE RawSql.Products SET Price = Price * {0.9m} WHERE CategoryId = {2}");
        var discounted = await db.Products.SingleAsync(p => p.Name == "Cable Basic");
        Check(affected == 4 && discounted.Price == 4.50m,
            "ExecuteSqlInterpolatedAsync bulk discount: 4 rows affected, 5.00 became 4.50");

        await RestoreCanonicalPricesAsync(db);
        var restored = await db.Products.AsNoTracking().SingleAsync(p => p.Name == "Cable Basic");
        Check(restored.Price == 5.00m, "prices restored to the seeded values for the next demos");
    }

    using (var db = NewDb())
    {
        // The MERGE upsert from the article, both paths. Matching on Name because Id is
        // an IDENTITY column — merging on it would need IDENTITY_INSERT.
        async Task MergeWidgetAsync(decimal price) =>
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                MERGE RawSql.Products AS target
                USING (SELECT {"Merged Widget"} AS Name, {price} AS Price, {1} AS CategoryId) AS source
                ON target.Name = source.Name
                WHEN MATCHED THEN UPDATE SET Price = source.Price
                WHEN NOT MATCHED THEN INSERT (Name, Price, CategoryId) VALUES (source.Name, source.Price, source.CategoryId);
                """);

        async Task<decimal> MergedPriceAsync() =>
            (await db.Products.AsNoTracking().SingleAsync(p => p.Name == "Merged Widget")).Price;

        await MergeWidgetAsync(10.00m);
        Check(await MergedPriceAsync() == 10.00m, "MERGE upsert: WHEN NOT MATCHED inserted it at 10.00");

        await MergeWidgetAsync(20.00m);
        Check(await MergedPriceAsync() == 20.00m, "MERGE upsert: WHEN MATCHED updated it to 20.00");
    }
}

Console.WriteLine();
Console.WriteLine($"All {checksPassed} checks passed. Raw SQL and LINQ are not rivals: parameterize");
Console.WriteLine("everything, compose where it helps, and reach for SqlQueryRaw<T> when the");
Console.WriteLine("shape stops being an entity.");

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

static Task<int> RestoreCanonicalPricesAsync(AppDbContext db) =>
    db.Database.ExecuteSqlRawAsync("""
        UPDATE p SET p.Price = v.Price
        FROM RawSql.Products p
        JOIN (VALUES
            (N'Dev Board A', 25.00), (N'Dev Board B', 45.00), (N'Dev Board Pro', 75.00), (N'Dev Board Max', 120.00),
            (N'Cable Basic', 5.00), (N'Cable Braided', 15.00), (N'Cable Optical', 55.00), (N'Cable Active', 85.00),
            (N'Display 24', 35.00), (N'Display 27', 50.00), (N'Display 32', 95.00), (N'Display Ultra', 240.00)
        ) v(Name, Price) ON p.Name = v.Name;
        """);

// Fail loudly rather than checking claims against an empty database, and undo what the
// demos above write so every run starts from the seeded state.
async Task VerifySeedAndResetAsync()
{
    using var db = NewDb();

    if (!await db.Database.CanConnectAsync())
        throw new InvalidOperationException(
            $"Cannot connect using '{connectionString}'. Is LocalDB running?");

    if (!await db.Products.AnyAsync())
        throw new InvalidOperationException(
            "Seed data missing. Run seed.sql first — see this folder's README.");

    await db.Database.ExecuteSqlRawAsync(
        "DELETE FROM RawSql.Products WHERE Name = N'Merged Widget';");
    await RestoreCanonicalPricesAsync(db);

    Console.WriteLine("Seed data verified; rows mutated by previous runs reset.");
    Console.WriteLine();
}

namespace JorgenHoc.RawSqlSample
{
    // The article's DTOs for SqlQueryRaw<T>.
    public record RankedProduct(int Id, string Name, decimal Price, int CategoryId, int PriceRank);
    public record CategorySummary(int Id, string Name, int ProductCount, decimal AvgPrice);
}
