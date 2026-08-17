# EF Core: the N+1 query problem

Runnable proof for the statement counts in
[EF Core Performance: Solving the N+1 Query Problem](https://www.jorgenhoc.org/en/blog/ef-core-n-plus-one).

## Run it

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and SQL Server LocalDB
(installed with Visual Studio, or via the SQL Server Express installer).

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "IF DB_ID('JorgenHocSamples') IS NULL CREATE DATABASE JorgenHocSamples;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d JorgenHocSamples -E -i seed.sql
dotnet run
```

Point `appsettings.json` somewhere else if you prefer another server — any SQL Server
will do. To use a different database without editing a tracked file, drop an
`appsettings.Local.json` next to it (gitignored).

Add `-- --sql` to print every statement as it executes:

```bash
dotnet run -- --sql
```

## Expected output

```
| Strategy                              | SQL statements |
|---------------------------------------|----------------|
| Query per row (N+1, customer only)    |            501 |
| Query per row (N+1, customer + lines) |          1,001 |
| Include (eager, single query)         |              1 |
| Include + AsSplitQuery()              |              2 |
| Select() projection                   |              1 |
```

You should get these exact numbers. Statement counts do not depend on hardware or
provider — 500 orders means 1 + 500 statements when you query per row, and 1 + 1000 when
you touch two navigations. If your numbers differ, something is genuinely different, and
it is worth opening an issue.

## What the numbers mean

**Counts, not timings, on purpose.** A timing from someone else's laptop tells you
nothing useful. The statement count is deterministic and reproducible, which is what makes
it evidence rather than an anecdote.

The flip side: this runs against a local database where a round trip costs almost nothing,
so the *time* difference here badly understates the real one. Against a managed database in
another region you pay the network round trip 1,001 times instead of once. That gap is why
N+1 passes local testing and then becomes a production incident.

**Why `AsSplitQuery()` reports 2 and not 3.** `Customer` is a reference navigation, so it
stays in the JOIN; only the `Lines` collection is split out. Include a second *collection*
and it becomes three.

## Notes

The N+1 here is produced by querying per row (`FindAsync` inside a loop) rather than by
lazy loading. That matters: with EF Core's defaults, simply reading `order.Customer` after
a bare `ToListAsync()` does **not** fire a query — the navigation is `null` and
`order.Lines` is empty. Lazy loading requires the `Proxies` package,
`UseLazyLoadingProxies()`, and `virtual` navigation properties. Query-per-row needs none of
that, and is the far more common cause in real codebases.

`seed.sql` also creates a `Tags` table used by the article's cartesian-explosion example.
It has no entity class here, which is harmless.
