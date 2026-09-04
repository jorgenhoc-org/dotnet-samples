using JorgenHoc.DataAccess.EfCoreNPlusOne;
using JorgenHoc.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration; // GetConnectionString
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;

// Reproduces the statement counts published in
// https://www.jorgenhoc.org/en/blog/ef-core-hybridcache
//
// Reuses the N+1 article's schema and seed (500 orders) — seed the database first, see
// this folder's README.
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
    .CountStatements(counter, printSql));

// L2 is optional and off by default. HybridCache without a registered IDistributedCache
// runs L1-only (in-memory), and every count below is identical either way — stampede
// protection and tag invalidation are HybridCache features, not Redis features. L2 buys
// you entries that survive restarts and are shared across processes; set the "Redis"
// connection string in appsettings.json to turn it on.
var redis = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redis))
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redis);

builder.Services.AddHybridCache();

using var host = builder.Build();
var cache = host.Services.GetRequiredService<HybridCache>();

await VerifySeedDataAsync();
Console.WriteLine(string.IsNullOrEmpty(redis)
    ? "L2: none — HybridCache is running L1-only (in-memory)."
    : $"L2: Redis at '{redis}'.");

var report = new StatementReport("HybridCache vs no cache — 500 orders, SQL Server LocalDB");

// Reading through the cache: one key, one tag, five-minute expiration. GetOrCreateAsync
// only invokes the factory on a miss, and coalesces concurrent callers of the same key so
// the factory runs once (stampede protection) — that is the whole pitch.
Task<List<OrderSummary>> GetCachedAsync() =>
    cache.GetOrCreateAsync(
        "orders:summaries",
        async ct => await LoadSummariesAsync(ct),
        new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) },
        tags: ["orders"]).AsTask();

await MeasureAsync("5 sequential reads, no cache", async () =>
{
    for (var i = 0; i < 5; i++)
        await LoadSummariesAsync();
});

await MeasureAsync("5 sequential reads, HybridCache", async () =>
{
    for (var i = 0; i < 5; i++)
        await GetCachedAsync();
});

// The stampede scenario: a popular key expires and every in-flight request hits the
// database at once. This is the case a plain IMemoryCache.GetOrCreateAsync does NOT
// protect you from — its factory runs once per concurrent caller.
await MeasureAsync("20 concurrent reads, no cache", () =>
    Task.WhenAll(Enumerable.Range(0, 20).Select(_ => LoadSummariesAsync())));

await cache.RemoveAsync("orders:summaries"); // start the cached run from a cold key

await MeasureAsync("20 concurrent reads, HybridCache", () =>
    Task.WhenAll(Enumerable.Range(0, 20).Select(_ => GetCachedAsync())));

// Tag-based invalidation (new in the .NET 10 wave): flush every entry tagged "orders" —
// no key bookkeeping — and the next read pays exactly one query to repopulate.
await cache.RemoveByTagAsync("orders");

await MeasureAsync("1 read after RemoveByTagAsync(\"orders\")", GetCachedAsync);

report.Print();

Console.WriteLine("The counts, not timings, are the point: 5 reads cost 5 queries without a");
Console.WriteLine("cache and 1 with it, on any hardware. Locally a query is nearly free, so");
Console.WriteLine("caching looks pointless in dev — against a managed database in another");
Console.WriteLine("region, every avoided round trip is real latency off a request.");

// Keep the window open when launched from an IDE, without breaking `dotnet run | tee`
// or CI. Unguarded, Console.ReadKey() throws the moment stdin is not interactive.
if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);
}

// The query being cached — a projection, not entities. Cached values round-trip through
// HybridCache's serializer (System.Text.Json by default), so cache small immutable
// shapes; never an entity graph dragging change-tracker state with it. Each call gets a
// fresh scope so concurrent callers never share a DbContext (it is not thread-safe).
async Task<List<OrderSummary>> LoadSummariesAsync(CancellationToken ct = default)
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    return await db.Orders
        .Select(o => new OrderSummary(o.Reference, o.Customer.Name, o.Lines.Count))
        .ToListAsync(ct);
}

async Task MeasureAsync(string strategy, Func<Task> work)
{
    counter.Reset();
    await work();
    report.Add(strategy, counter.Count);
}

// Fail loudly rather than reporting a table of zeros against an empty database.
async Task VerifySeedDataAsync()
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (!await db.Database.CanConnectAsync())
        throw new InvalidOperationException(
            $"Cannot connect using '{connectionString}'. Is LocalDB running?");

    var orders = await db.Orders.CountAsync();
    if (orders == 0)
        throw new InvalidOperationException(
            "No orders found. Run the seed script first — see this folder's README.");

    Console.WriteLine($"Seed data: {orders} orders.");
}

internal sealed record OrderSummary(string Reference, string CustomerName, int LineCount);
