using JorgenHoc.DataAccess.EfCoreNPlusOne;
using JorgenHoc.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration; // GetConnectionString
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;

// Reproduces the statement counts published in
// https://www.jorgenhoc.org/en/blog/ef-core-n-plus-one
//
// Seed the database first — see this folder's README.
//
//   dotnet run                 summary table only
//   dotnet run -- --sql        also print every statement (use this for screenshots)

var printSql = args.Contains("--sql", StringComparer.OrdinalIgnoreCase);

var builder = Host.CreateApplicationBuilder(args);

// Nothing here is gated on the hosting environment, deliberately. A sample that behaves
// differently depending on how you launched it is a sample that wastes your afternoon:
// launchSettings.json only applies to `dotnet run` and IDE profiles, never to the built
// executable, so an IsDevelopment() gate would silently disable logging for anyone who
// ran bin/Debug/*.exe. (For the record, the generic host reads DOTNET_ENVIRONMENT —
// ASPNETCORE_ENVIRONMENT is only honoured by WebApplicationBuilder.)

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

await VerifySeedDataAsync();

var report = new StatementReport("EF Core statement counts — 500 orders, SQL Server LocalDB");

// N+1: one query per row. This is what the mistake looks like in real code — no lazy
// loading required, which is why it survives code review.
await MeasureAsync("Query per row (N+1, customer only)", async db =>
{
    var orders = await db.Orders.ToListAsync();
    foreach (var order in orders)
        _ = await db.Customers.FindAsync(order.CustomerId);
});

await MeasureAsync("Query per row (N+1, customer + lines)", async db =>
{
    var orders = await db.Orders.ToListAsync();
    foreach (var order in orders)
    {
        _ = await db.Customers.FindAsync(order.CustomerId);
        _ = await db.OrderLines.Where(l => l.OrderId == order.Id).ToListAsync();
    }
});

// The fixes.
await MeasureAsync("Include (eager, single query)", async db =>
    await db.Orders.Include(o => o.Customer).Include(o => o.Lines).ToListAsync());

await MeasureAsync("Include + AsSplitQuery()", async db =>
    await db.Orders.Include(o => o.Customer).Include(o => o.Lines)
                   .AsSplitQuery().ToListAsync());

await MeasureAsync("Select() projection", async db =>
    await db.Orders.Select(o => new
    {
        o.Reference,
        CustomerName = o.Customer.Name,
        LineCount = o.Lines.Count,
    }).ToListAsync());

report.Print();

Console.WriteLine("Counts are provider- and hardware-independent: N+1 over N rows is always");
Console.WriteLine("1 + N statements, or 1 + 2N when you touch two navigations. What varies is");
Console.WriteLine("the cost per statement — locally a round trip is nearly free, so this passes");
Console.WriteLine("testing and then falls over against a database in another region.");

// Keep the window open when launched from an IDE, without breaking `dotnet run | tee`
// or CI. Unguarded, Console.ReadKey() throws "Cannot read keys ... console input has been
// redirected" the moment stdin is not interactive — which kills the process and closes the
// very window you were trying to keep open.
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
