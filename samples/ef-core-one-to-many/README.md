# EF Core: one-to-many relationships

Runnable statement counts for every loading strategy and CRUD approach in
[EF Core One-to-Many Relationships — The Complete Explanation](https://www.jorgenhoc.org/en/blog/ef-core-one-to-many).

## Run it

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and SQL Server LocalDB
(installed with Visual Studio, or via the SQL Server Express installer).

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "IF DB_ID('JorgenHocSamples') IS NULL CREATE DATABASE JorgenHocSamples;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d JorgenHocSamples -E -i seed.sql
dotnet run
```

Point `appsettings.json` somewhere else if you prefer another server — any SQL Server
will do. Add `-- --sql` to print every statement as it executes:

```bash
dotnet run -- --sql
```

The sample writes a little data (two products, one move) and cleans up after itself on
the next run, so it is safe to re-run without reseeding. Re-run `seed.sql` any time you
want a factory reset — it drops and recreates its own `OneToMany` schema and touches
nothing else in the database.

## Expected output

```
| Strategy                                   | SQL statements |
|--------------------------------------------|----------------|
| Include (eager, single JOIN query)         |              1 |
| Filtered Include (Price > 50)              |              1 |
| Explicit loading (parent, then collection) |              2 |
| Lazy loading (20 products, 5 categories)   |              6 |
| Create: set the FK property                |              1 |
| Create: assign the navigation (Find + add) |              2 |
| Move to another category (Find + save)     |              2 |
```

Then a `DbUpdateException` demonstration: deleting a category that still has products is
rejected by the FK constraint, because the relationship is configured with
`DeleteBehavior.Restrict`.

You should get these exact numbers — statement counts do not depend on hardware or
provider. If yours differ, something is genuinely different and worth an issue.

## What the numbers mean

**Eager loading is flat.** `Include` costs one statement whether a category has four
products or four thousand. Filtered `Include` is still one statement — the filter goes
into the SQL, not into your loop.

**Lazy loading is 6, not 21 — and that's the trap.** Twenty products are loaded with one
query; touching `product.Category` then fires one lazy query for the *first* product of
each category, and EF Core's navigation fixup attaches that category to every other
tracked product pointing at it. Five categories → five extra queries. The count scales
with *distinct parents touched*, so with one product per parent — or with `AsNoTracking`,
which disables fixup — this becomes a textbook [N+1](../ef-core-n-plus-one).

**Creating via the FK property is the cheapest write.** Setting `CategoryId` costs a
single INSERT. Assigning the navigation property is fine too — the second statement here
is the `Find` that fetched the parent, not overhead EF Core added.

## Notes

The entities and Fluent API configuration live in
[`shared/JorgenHoc.DataAccess/EfCoreOneToMany`](../../shared/JorgenHoc.DataAccess/EfCoreOneToMany)
and mirror the article's snippets: explicit FK, `HasMaxLength`/`HasPrecision`, and
`OnDelete(DeleteBehavior.Restrict)`. Navigations are `virtual` so the lazy-loading
measurement can use proxies; every other measurement runs without
`UseLazyLoadingProxies()`, which is EF Core's default and the article's recommendation.
