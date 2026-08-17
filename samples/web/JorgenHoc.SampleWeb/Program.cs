using JorgenHoc.DataAccess.EfCoreNPlusOne;
using JorgenHoc.SampleWeb;
using JorgenHoc.SampleWeb.Endpoints;
using Microsoft.EntityFrameworkCore;

// Shared host for the article demos that only make sense over HTTP — chiefly MiniProfiler,
// whose value is a per-request browser overlay showing every SQL statement.
//
// Seed the database first: see samples/ef-core-n-plus-one/README.md.

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("LocalDbConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'LocalDbConnection' is missing from appsettings.json.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

builder.Services
    .AddMiniProfiler(options =>
    {
        options.RouteBasePath = "/profiler";
        options.PopupShowTimeWithChildren = true;

        // Off deliberately. With it on, every statement also logs a connection open and
        // close, so the overlay shows three timings per query and the count no longer
        // matches the statement count the article quotes. The point of this demo is that
        // one number — keep it readable.
        options.TrackConnectionOpenClose = false;
    })
    // Without this the overlay renders but lists no queries — MiniProfiler.AspNetCore
    // alone knows nothing about EF Core.
    .AddEntityFramework();

var app = builder.Build();

// Must run before the endpoints it profiles.
app.UseMiniProfiler();

// Fail loudly on an unseeded database rather than rendering empty tables.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (!await db.Database.CanConnectAsync())
        throw new InvalidOperationException($"Cannot connect using '{connectionString}'.");
    if (!await db.Orders.AnyAsync())
        throw new InvalidOperationException(
            "No orders found. Run samples/ef-core-n-plus-one/seed.sql first.");
}

app.MapGet("/", (HttpContext ctx) => Results.Content(Page.Index(ctx), "text/html"));

// One line per article.
app.MapEfCoreNPlusOne();

app.Run();
