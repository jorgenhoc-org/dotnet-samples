# ef-core-raw-sql

Asserted behaviour for every claim in
[EF Core Raw SQL Queries](https://www.jorgenhoc.org/en/blog/ef-core-raw-sql):
19 checks that throw on failure. EF Core 10, SQL Server LocalDB.

## Run it

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "IF DB_ID('JorgenHocSamples') IS NULL CREATE DATABASE JorgenHocSamples;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d JorgenHocSamples -E -i seed.sql
dotnet run
```

`dotnet run -- --sql` also prints every SQL statement (what the article screenshots use).
Safe to re-run — mutated prices and the merged row are reset at startup.

## What it proves

**The injection demo is a real injection.** The same input — `' OR '1'='1` — goes through
both APIs. Concatenated into `FromSqlRaw`, it leaks all 12 rows (the analyzer flags the
line with `EF1003`; the suppression is there because being vulnerable is the demo's job).
Through `FromSqlInterpolated`, it becomes parameter `@p0` and matches 0 rows.

**`ROW_NUMBER()` needs a CAST.** SQL Server returns it as `bigint`; materializing into an
`int` DTO property throws `InvalidCastException` — asserted, then done right with
`CAST(... AS int)`. Positional C# records work fine as `SqlQueryRaw<T>` targets.

**Composition has a boundary.** LINQ over a raw `SELECT` wraps it in a subquery and runs
as one statement (`Where` + `OrderBy` + `Include` asserted). LINQ over `EXEC` throws
`InvalidOperationException` — stored procedure results cannot be subqueried; filter
inside the proc or in memory.

**The rest:** named `SqlParameter`s, a CTE into a DTO, an OUTPUT parameter round-trip,
`AsNoTracking` (0 tracked vs 11), `ToQueryString()` executing nothing, a bulk
`ExecuteSqlInterpolatedAsync` UPDATE, and a `MERGE` upsert hitting both the insert and
update paths.

Full-text search and the PostgreSQL JSONB example from the article are not asserted here:
LocalDB has no full-text engine and this repo runs on SQL Server only.

## Expected output

Nineteen `[OK]` lines ending with:

```
All 19 checks passed. Raw SQL and LINQ are not rivals: parameterize
everything, compose where it helps, and reach for SqlQueryRaw<T> when the
shape stops being an entity.
```
