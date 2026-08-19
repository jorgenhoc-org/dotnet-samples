# EF Core: global query filters

Asserted behaviour for every claim in
[EF Core Global Query Filters — Soft Delete and Multi-Tenancy](https://www.jorgenhoc.org/en/blog/ef-core-global-query-filters):
soft delete, multi-tenancy, filtered navigations, `IgnoreQueryFilters()`, owned entities,
and the partial index. Every line of output is a passing check — a failed check throws.

## Run it

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and SQL Server LocalDB
(installed with Visual Studio, or via the SQL Server Express installer).

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "IF DB_ID('JorgenHocSamples') IS NULL CREATE DATABASE JorgenHocSamples;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d JorgenHocSamples -E -i seed.sql
dotnet run
```

Point `appsettings.json` somewhere else if you prefer another server — any SQL Server
will do. Add `-- --sql` to print every statement as it executes. The demos mutate rows
on purpose; the program resets them at startup, so it is safe to re-run without
reseeding.

## Expected output

22 `[OK]` checks across five groups: soft delete, multi-tenancy, the
`Expression.Constant(provider)` trap, owned entities, and the partial index. Four of
them are the reason this sample exists:

```
[OK] fixup trap: after IgnoreQueryFilters() in the same context, the navigation shows
     3 posts — the filter is SQL-level, but tracked entities are fixed up regardless
[OK] SECOND buggy context (provider B!) STILL sees tenant A — the first provider is
     baked into the cached model
[OK] ExecuteDelete bypasses the SaveChanges interception — a hard DELETE, gone even
     from IgnoreQueryFilters()
[OK] IgnoreQueryFilters(["SoftDelete"]) shows tenant A's deleted invoice but still
     hides tenant B (named filters, EF Core 10)
```

## What the checks prove

**The filter is SQL-level and covers navigations.** A plain query, an `Include()` JOIN,
and a count all exclude soft-deleted rows with no application-code filtering. A line
item deliberately seeded with the wrong `TenantId` never surfaces through
`Include(i => i.LineItems)` — cross-tenant leaks are stopped even through corrupted data.

**...but the change tracker doesn't care about your filters.** After an
`IgnoreQueryFilters()` query in the same context, the deleted post is *tracked*, and the
next `Include()` query attaches it to the navigation via fixup even though the SQL
filtered it. Use a fresh context (or `AsNoTracking`) after bypassing filters.

**The `Expression.Constant(provider)` trap.** Building the tenant filter around the
provider *instance* works in the first context and silently breaks in every context
after it: EF Core caches the model per context type, and the first provider is baked
into the cached filter. `BuggyTenantContext` demonstrates the failure; `AppDbContext`
builds the same filter through `Expression.Constant(this)` + field access, which EF Core
rewrites to the executing context on every query — a second instance with a tenant-B
provider correctly sees tenant B, and flipping a mutable provider mid-context switches
results per query.

**SaveChanges interception has edges.** `Remove()` + `SaveChangesAsync()` executes
exactly one statement, an UPDATE — but only because the context overrides the
`(bool, CancellationToken)` overload that *all four* public entry points funnel through.
Overriding only `SaveChangesAsync(CancellationToken)` leaves synchronous `SaveChanges()`
producing real DELETEs. And `ExecuteDelete` never enters SaveChanges at all: the check
proves it hard-deletes straight through the soft-delete regime.

**Named filters (EF Core 10) end the all-or-nothing era.** The context registers
soft-delete and tenant isolation as two named filters (`"SoftDelete"`, `"Tenant"`) that
EF Core ANDs together. `IgnoreQueryFilters(["SoftDelete"])` then surfaces a tenant's own
deleted rows *without* dropping tenant isolation — the admin-restore scenario that used
to require a second context type or a flag hack.

**Owned entities.** `HasQueryFilter` on an owned type throws
`InvalidOperationException`; the owner's filter covers the owned columns.

**The partial index is real** — read back from `sys.indexes`:
`IX_QueryFilters_Posts_Active -> ([IsDeleted]=(0))`. Note that `seed.sql` sets
`QUOTED_IDENTIFIER ON` explicitly: sqlcmd defaults it OFF, and filtered indexes refuse
to be created without it.

## Notes

Entities and the corrected `AppDbContext` live in
[`shared/JorgenHoc.DataAccess/EfCoreGlobalQueryFilters`](../../shared/JorgenHoc.DataAccess/EfCoreGlobalQueryFilters).
The deliberately broken `BuggyTenantContext` and the throwing `OwnedFilterContext` live
in this folder's `Program.cs` — they are the counterexamples, not patterns to copy.
