using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using JorgenHoc.DataAccess.EfCoreVsDapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace JorgenHoc.Benchmarks;

/// <summary>
/// Backs the performance table in the "EF Core vs Dapper" article. Four scenarios,
/// grouped by category, baseline = default EF Core in each group.
///
/// Fairness decisions, stated rather than implied:
/// - Every benchmark pays its own setup the way real code does: EF benchmarks construct
///   a DbContext per invocation (one per request in a web app), Dapper and ADO.NET open
///   a pooled SqlConnection per invocation.
/// - All reads materialize a List of the same entity/DTO — nobody gets to stream.
/// - Everything runs against SQL Server LocalDB, so round trips are near-free and the
///   mapper overhead is the *largest* share of the numbers you will see here. Against a
///   remote database the network dominates and the gap shrinks — that caveat belongs in
///   any article quoting these numbers.
///
/// Run samples/ef-core-vs-dapper/seed.sql first (1,000 products, 500 priced above 10).
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EfCoreVsDapperBenchmark
{
    // Same database and rows the console sample uses — keep the two in sync.
    // Instance field rather than const: BenchmarkDotNet needs instance methods, and a
    // const here would leave the Dapper/ADO.NET benchmarks flagged CA1822 (make static).
    private readonly string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=JorgenHocSamples;Trusted_Connection=True;TrustServerCertificate=True;";

    private const string SelectAllSql = "SELECT Id, Name, Price, CategoryId FROM Products";
    private const string SelectByIdSql = "SELECT Id, Name, Price, CategoryId FROM Products WHERE Id = @Id";
    private const string JoinSql = """
        SELECT p.Id, p.Name, p.Price, c.Name AS CategoryName
        FROM   Products p
        JOIN   Categories c ON c.Id = p.CategoryId
        WHERE  p.Price > @MinPrice
        """;
    private const string InsertSql = """
        INSERT INTO Products (Name, Price, CategoryId)
        VALUES (@Name, @Price, @CategoryId);
        SELECT CAST(SCOPE_IDENTITY() AS INT);
        """;

    private const int LookupId = 500;

    private DbContextOptions<AppDbContext> _options = null!;

    // Compiled once, as the article instructs — the whole point is skipping the
    // LINQ-to-SQL translation on every call.
    private static readonly Func<AppDbContext, IAsyncEnumerable<Product>> CompiledSelectAll =
        EF.CompileAsyncQuery((AppDbContext ctx) => ctx.Products.AsNoTracking());

    private static readonly Func<AppDbContext, int, Task<Product?>> CompiledById =
        EF.CompileAsyncQuery((AppDbContext ctx, int id) =>
            ctx.Products.AsNoTracking().FirstOrDefault(p => p.Id == id));

    [GlobalSetup]
    public void Setup()
    {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        using var ctx = new AppDbContext(_options);
        var products = ctx.Products.Count();
        if (products != 1000)
            throw new InvalidOperationException(
                $"Expected 1,000 seeded products, found {products}. " +
                "Run samples/ef-core-vs-dapper/seed.sql first.");
    }

    // ---- Simple SELECT, 1,000 rows ----

    [BenchmarkCategory("Select 1,000 rows"), Benchmark(Baseline = true, Description = "EF Core (tracking)")]
    public async Task<List<Product>> EfCoreSelectAll()
    {
        using var ctx = new AppDbContext(_options);
        return await ctx.Products.ToListAsync();
    }

    [BenchmarkCategory("Select 1,000 rows"), Benchmark(Description = "EF Core AsNoTracking")]
    public async Task<List<Product>> EfCoreSelectAllNoTracking()
    {
        using var ctx = new AppDbContext(_options);
        return await ctx.Products.AsNoTracking().ToListAsync();
    }

    [BenchmarkCategory("Select 1,000 rows"), Benchmark(Description = "EF Core compiled query")]
    public async Task<List<Product>> EfCoreSelectAllCompiled()
    {
        using var ctx = new AppDbContext(_options);
        var list = new List<Product>(1024);
        await foreach (var p in CompiledSelectAll(ctx))
            list.Add(p);
        return list;
    }

    [BenchmarkCategory("Select 1,000 rows"), Benchmark(Description = "Dapper")]
    public async Task<List<Product>> DapperSelectAll()
    {
        await using var conn = new SqlConnection(ConnectionString);
        return (await conn.QueryAsync<Product>(SelectAllSql)).AsList();
    }

    [BenchmarkCategory("Select 1,000 rows"), Benchmark(Description = "Raw ADO.NET")]
    public async Task<List<Product>> AdoNetSelectAll()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(SelectAllSql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var list = new List<Product>(1024);
        while (await reader.ReadAsync())
        {
            list.Add(new Product
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Price = reader.GetDecimal(2),
                CategoryId = reader.GetInt32(3),
            });
        }
        return list;
    }

    // ---- Single-row lookup by primary key ----

    [BenchmarkCategory("PK lookup"), Benchmark(Baseline = true, Description = "EF Core FirstOrDefault")]
    public async Task<Product?> EfCoreById()
    {
        using var ctx = new AppDbContext(_options);
        return await ctx.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == LookupId);
    }

    [BenchmarkCategory("PK lookup"), Benchmark(Description = "EF Core compiled query")]
    public async Task<Product?> EfCoreByIdCompiled()
    {
        using var ctx = new AppDbContext(_options);
        return await CompiledById(ctx, LookupId);
    }

    [BenchmarkCategory("PK lookup"), Benchmark(Description = "Dapper")]
    public async Task<Product?> DapperById()
    {
        await using var conn = new SqlConnection(ConnectionString);
        return await conn.QuerySingleOrDefaultAsync<Product>(SelectByIdSql, new { Id = LookupId });
    }

    [BenchmarkCategory("PK lookup"), Benchmark(Description = "Raw ADO.NET")]
    public async Task<Product?> AdoNetById()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(SelectByIdSql, conn);
        cmd.Parameters.AddWithValue("@Id", LookupId);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new Product
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Price = reader.GetDecimal(2),
            CategoryId = reader.GetInt32(3),
        };
    }

    // ---- JOIN projection, 500 rows ----

    [BenchmarkCategory("JOIN projection, 500 rows"), Benchmark(Baseline = true, Description = "EF Core Select projection")]
    public async Task<List<ProductDto>> EfCoreJoin()
    {
        using var ctx = new AppDbContext(_options);
        return await ctx.Products.AsNoTracking()
            .Where(p => p.Price > 10)
            .Select(p => new ProductDto(p.Id, p.Name, p.Price, p.Category!.Name))
            .ToListAsync();
    }

    [BenchmarkCategory("JOIN projection, 500 rows"), Benchmark(Description = "Dapper")]
    public async Task<List<ProductDto>> DapperJoin()
    {
        await using var conn = new SqlConnection(ConnectionString);
        return (await conn.QueryAsync<ProductDto>(JoinSql, new { MinPrice = 10m })).AsList();
    }

    // ---- Single INSERT ----

    [BenchmarkCategory("Single INSERT"), Benchmark(Baseline = true, Description = "EF Core Add + SaveChanges")]
    public async Task<int> EfCoreInsert()
    {
        using var ctx = new AppDbContext(_options);
        var product = new Product { Name = "Bench Widget", Price = 9.99m, CategoryId = 1 };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product.Id;
    }

    [BenchmarkCategory("Single INSERT"), Benchmark(Description = "Dapper")]
    public async Task<int> DapperInsert()
    {
        await using var conn = new SqlConnection(ConnectionString);
        return await conn.ExecuteScalarAsync<int>(InsertSql,
            new { Name = "Bench Widget", Price = 9.99m, CategoryId = 1 });
    }

    // Deletes what the INSERT benchmarks added and reseeds the identity, so the SELECT
    // scenarios always see exactly the 1,000 seeded rows and re-runs are reproducible.
    [IterationCleanup(Targets = [nameof(EfCoreInsert), nameof(DapperInsert)])]
    public void CleanupInsertedRows()
    {
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();
        using var cmd = new SqlCommand(
            "DELETE FROM Products WHERE Id > 1000; DBCC CHECKIDENT('dbo.Products', RESEED, 1000);",
            conn);
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Matches the article's DTO; Dapper maps it via the constructor.</summary>
public record ProductDto(int Id, string Name, decimal Price, string CategoryName);
