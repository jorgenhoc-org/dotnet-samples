# EF Core vs Dapper

Runnable code behind
[EF Core vs Dapper — Which ORM Should You Use?](https://www.jorgenhoc.org/en/blog/ef-core-vs-dapper).

Split in two, following the repo convention:

- **This console sample** demonstrates the deterministic claims — same rows from both
  libraries, EF's generated SQL next to the handwritten equivalent, the one-column
  targeted UPDATE from change tracking, and one transaction shared by both libraries.
- **[`benchmarks/JorgenHoc.Benchmarks`](../../benchmarks/JorgenHoc.Benchmarks)**
  (`EfCoreVsDapperBenchmark`) measures the timings and allocations: SELECT of 1,000
  rows, PK lookup, JOIN projection of 500 rows, and single INSERT — EF Core default vs
  AsNoTracking vs compiled query vs Dapper vs raw ADO.NET.

## Seed first

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -d JorgenHocSamples -i seed.sql
```

Creates `Categories` (20 rows) and `Products` (1,000 rows, exactly 500 priced above 10 —
odd ids 5.99, even ids 15.99, which is what makes the checks below deterministic).
Coexists with the other articles' tables; safe to re-run.

## Run it

```bash
cd samples/ef-core-vs-dapper
dotnet run              # summary + checks
dotnet run -- --sql     # also print every statement EF executes (use for screenshots)
```

Every claim is asserted, not narrated — the run fails loudly if any of these stop being true:

1. **Same rows either way.** EF Core (`AsNoTracking().ToListAsync()`) and Dapper
   (`QueryAsync<Product>`) both return the 1,000 seeded rows with matching checksums.
   The generated SQL is printed next to the handwritten SQL.
2. **JOIN projection.** EF's `Select` projection compiles to a single JOIN (statement
   count asserted = 1) and its 500 DTOs are `SequenceEqual` to Dapper's.
3. **Targeted UPDATE.** Change one property, and `SaveChanges` issues exactly one
   statement whose SET clause contains only `[Price]`. Run with `--sql` to see it.
   Wrapped in a rolled-back transaction so re-runs stay deterministic.
4. **Shared transaction.** EF Core insert + Dapper update on the same connection and
   `DbTransaction`; both visible inside the transaction, both gone after rollback.

## Run the benchmark

```bash
cd benchmarks/JorgenHoc.Benchmarks
dotnet run -c Release -- --filter *EfCoreVsDapper*
```

The INSERT scenarios clean up after themselves (delete + identity reseed), so the
database is back to the seeded 1,000 rows after a run.

Timings are LocalDB timings: round trips are nearly free, which *maximizes* the visible
mapper overhead. Against a remote database, network latency dominates and the relative
gaps shrink. That is exactly why the console sample reports counts and SQL text instead
of milliseconds — see the repo README's "Counts over timings".
