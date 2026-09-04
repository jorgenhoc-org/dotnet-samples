# Caching EF Core queries with HybridCache

Runnable proof for the statement counts in the upcoming article
*Caching EF Core Queries with HybridCache in .NET 10*
(will publish at https://www.jorgenhoc.org/en/blog/ef-core-hybridcache).

## Run it

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and SQL Server LocalDB.
Reuses the schema and seed data from [`../ef-core-n-plus-one`](../ef-core-n-plus-one) —
if you already seeded that sample, just `dotnet run`. Otherwise:

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "IF DB_ID('JorgenHocSamples') IS NULL CREATE DATABASE JorgenHocSamples;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d JorgenHocSamples -E -i ../ef-core-n-plus-one/seed.sql
dotnet run
```

No Redis required — with the `Redis` connection string left empty, HybridCache runs
L1-only (in-memory) and every count below is identical. To exercise the L2 tier:

```bash
docker run -d --name redis -p 6379:6379 redis:7
```

then set `"Redis": "localhost:6379"` in `appsettings.json` (or in a gitignored
`appsettings.Local.json`).

Add `-- --sql` to print every statement as it executes.

## Expected output

```
| Strategy                                | SQL statements |
|-----------------------------------------|----------------|
| 5 sequential reads, no cache            |              5 |
| 5 sequential reads, HybridCache         |              1 |
| 20 concurrent reads, no cache           |             20 |
| 20 concurrent reads, HybridCache        |              1 |
| 1 read after RemoveByTagAsync("orders") |              1 |
```

The concurrent HybridCache row is the stampede-protection guarantee: callers of the same
key coalesce onto one factory execution, so it reads 1 in practice. (Strictly, a caller
that arrives after the factory already completed starts a fresh one — with 20 tasks
launched together against a query that takes milliseconds, that window is not hit.)

## What the numbers mean

**`IMemoryCache` does not give you the 1.** Its `GetOrCreateAsync` runs the factory once
per concurrent caller — 20 concurrent misses mean up to 20 identical queries. Stampede
protection is the headline reason HybridCache replaces the hand-rolled
`IMemoryCache` + `SemaphoreSlim` pattern.

**Tag invalidation replaces key bookkeeping.** Entries are tagged (`"orders"` here);
`RemoveByTagAsync` flushes all of them without tracking keys. Call it where the data
changes — after `SaveChangesAsync` on anything that touches orders.

**Counts, not timings, on purpose.** Statement counts are deterministic and reproduce on
your machine. Locally a query is nearly free, which is exactly why caching looks pointless
in dev and then matters against a managed database in another region.

## Notes

Cached values round-trip through HybridCache's serializer (System.Text.Json by default),
so the sample caches a small immutable record (`OrderSummary`) — never tracked entities.

L1 vs L2: L1 is per-process memory, L2 is the distributed backend (`IDistributedCache` —
Redis, SQL Server, Azure Cache). HybridCache checks L1, then L2, then runs the factory,
and populates both on the way back. With no L2 registered it is still a better
`IMemoryCache`: stampede protection and tags work regardless.
