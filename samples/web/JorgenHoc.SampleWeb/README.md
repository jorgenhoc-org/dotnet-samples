# Web samples host

Shared ASP.NET Core host for the article demos that only make sense over HTTP — chiefly
[MiniProfiler](https://miniprofiler.com/dotnet/), whose value is a per-request browser
overlay listing every SQL statement.

## Run it

Seed the database first (see
[`samples/ef-core-n-plus-one`](../../ef-core-n-plus-one/README.md)), then:

```bash
cd samples/web/JorgenHoc.SampleWeb
dotnet run
```

Open <http://localhost:5185>. Click a demo, then the **profiler badge in the top-left
corner** to expand the query list.

## What to look at

| Endpoint | Statements |
|---|---|
| `/ef-core-n-plus-one/orders-nplus1` | **1,001** |
| `/ef-core-n-plus-one/orders-fixed` | **1** |

Both take `?orders=N` (default 500). Load one, then the other, and compare the badge.

These counts match the table published in
[the N+1 article](https://www.jorgenhoc.org/en/blog/ef-core-n-plus-one) exactly — the
console sample in `samples/ef-core-n-plus-one` counts them with EF Core's own logging, and
MiniProfiler arrives at the same number by a completely different route. Two independent
instruments agreeing is the point.

## Adding an article

One extension method per article:

```csharp
// Endpoints/MyArticle.cs
public static class MyArticleEndpoints
{
    public static IEndpointRouteBuilder MapMyArticle(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/my-article-slug");
        group.MapGet("/demo", async (AppDbContext db, HttpContext ctx) => /* ... */);
        return routes;
    }
}
```

Then one line in `Program.cs`: `app.MapMyArticle();` — plus a link in `Page.Index`.

No interface, no DI registration, no assembly scanning. If a second article ever needs
real polymorphism, extract an abstraction then; with one implementation it would be
ceremony.

Every page must render through `Page.Shell`, which injects MiniProfiler's include script.
Without that script the overlay never appears, and the result is indistinguishable from a
profiler that isn't working.

## Three things worth knowing

**`MiniProfiler.AspNetCore.Mvc` alone is not enough.** It gives you request timings and the
overlay but knows nothing about EF Core, so the query list comes back empty.
`MiniProfiler.EntityFrameworkCore` plus `.AddEntityFramework()` is what captures SQL.

**`TrackConnectionOpenClose` is off on purpose.** With it on, each statement also logs a
connection open and close, so the overlay shows three timings per query and the count stops
matching the statement count the article quotes. Verified: 21 statements rendered as 63
timings with it enabled.

**The stack-trace snippets are useless here.** MiniProfiler records a `StackTraceSnippet`
per timing, but with EF Core every frame is framework machinery:

```
ExecuteReaderAsync > Start > Start > MoveNext > CommandReaderExecutingAsync
  > BroadcastCommandExecuting > DispatchEventData > Write
```

Identical for every query, and it never names the line of your code that caused it — which
is the one thing you would want it for. EF Core's async pipeline means the stack at
command-execution time contains none of your frames. The snippets are genuinely useful with
Dapper or raw ADO.NET, where you invoke the command directly.

What MiniProfiler *does* give you per statement: `DurationMilliseconds`,
`StartMilliseconds`, `ExecuteType`, and the full `CommandString` with parameter values.

## Not a deployment target

This host exists for profiling and needs a seeded database. The hosting articles need the
opposite — a minimal app with a small image and fast cold start. When those get ported they
should get their own project rather than reusing this one.

Nor is it useful for the async articles: ASP.NET Core has no `SynchronizationContext`, so
the classic `.Result` deadlock deliberately does not reproduce here. That needs WinForms,
WPF, or a custom context — see
[the deadlock article](https://www.jorgenhoc.org/en/blog/async-deadlocks-csharp).
