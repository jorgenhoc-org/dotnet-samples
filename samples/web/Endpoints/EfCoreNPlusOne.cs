using JorgenHoc.DataAccess.EfCoreNPlusOne;
using Microsoft.EntityFrameworkCore;

namespace JorgenHoc.SampleWeb.Endpoints;

/// <summary>
/// Web demo for https://www.jorgenhoc.org/en/blog/ef-core-n-plus-one
///
/// One extension method per article, called from Program.cs. Adding another article is a
/// new file plus one line — no interface, no DI registration, no assembly scanning. If a
/// second article ever needs genuine polymorphism, extract an abstraction then.
/// </summary>
public static class EfCoreNPlusOneEndpoints
{
    public static IEndpointRouteBuilder MapEfCoreNPlusOne(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/ef-core-n-plus-one");

        // The bug: one round trip per row. MiniProfiler shows 1 + 2N statements.
        group.MapGet("/orders-nplus1", async (AppDbContext db, HttpContext ctx, int orders = 500) =>
        {
            var list = await db.Orders.Take(orders).ToListAsync();

            var rows = new List<string>(list.Count);
            foreach (var order in list)
            {
                // Each iteration issues two more statements.
                var customer = await db.Customers.FindAsync(order.CustomerId);
                var lineCount = await db.OrderLines.CountAsync(l => l.OrderId == order.Id);
                rows.Add($"<tr><td>{order.Reference}</td><td>{customer?.Name}</td><td>{lineCount}</td></tr>");
            }

            return Results.Content(
                Page.Shell(ctx, "N+1", Table(
                    "Query per row (N+1)",
                    $"Expect <strong>{1 + (list.Count * 2):N0}</strong> statements: 1 for the order "
                    + "list, then two per order. Open the profiler badge to count them.",
                    rows)),
                "text/html");
        });

        // The fix: one projected query, no matter how many rows.
        group.MapGet("/orders-fixed", async (AppDbContext db, HttpContext ctx, int orders = 500) =>
        {
            var list = await db.Orders
                .Take(orders)
                .Select(o => new
                {
                    o.Reference,
                    CustomerName = o.Customer.Name,
                    LineCount = o.Lines.Count,
                })
                .ToListAsync();

            var rows = list
                .Select(o => $"<tr><td>{o.Reference}</td><td>{o.CustomerName}</td><td>{o.LineCount}</td></tr>")
                .ToList();

            return Results.Content(
                Page.Shell(ctx, "Fixed", Table(
                    "Select() projection",
                    "Expect <strong>1</strong> statement, for any number of orders.",
                    rows)),
                "text/html");
        });

        return routes;
    }

    private static string Table(string heading, string hint, List<string> rows) => $"""
        <p><a href="/">&larr; all samples</a></p>
        <h1>{heading}</h1>
        <p class="hint">{hint}</p>
        <table>
          <thead><tr><th>Reference</th><th>Customer</th><th>Lines</th></tr></thead>
          <tbody>{string.Join("\n", rows.Take(25))}</tbody>
        </table>
        <p class="hint">Showing the first 25 of {rows.Count:N0} rows — the query count is the point, not the output.</p>
        """;
}
