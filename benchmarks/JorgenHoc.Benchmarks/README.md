# Benchmarks

BenchmarkDotNet projects backing the performance figures in the articles. If a `Mean` or
`Allocated` number appears in a post, it came from here.

```bash
cd benchmarks/JorgenHoc.Benchmarks
dotnet run -c Release                           # all benchmarks
dotnet run -c Release -- --filter *ValueTask*   # one of them
```

`-c Release` is mandatory. BenchmarkDotNet will refuse to run a Debug build.

## What's here

### `TaskVsValueTaskBenchmark`

Backs [ValueTask vs Task in C#](https://www.jorgenhoc.org/en/blog/valuetask-vs-task-csharp).

Four cases: `Task<T>` and `ValueTask<T>`, each on a synchronously-completing path (cache
hit) and a genuinely async path (cache miss).

**Read the `Allocated` column, not `Mean`.** The claim under test is about allocation. At
15–25 ns the timing differences are mostly scheduling noise — BenchmarkDotNet flags both
cache-hit distributions as bimodal, so a "1.7x faster" reading of those rows would be
wrong.

The cache-miss cases are included on purpose: they show that `ValueTask` allocates *more*
than `Task` once the method actually suspends, which is the half of the story usually
missing when people convert every API in a codebase.

`Task.Yield()` is used rather than `Task.Delay(1)` for the async path — `Delay` would
measure the OS timer instead of allocation behaviour and push every column into
milliseconds.

## Why there is no N+1 benchmark here

Statement counts for the N+1 article live in
[`samples/ef-core-n-plus-one`](../../samples/ef-core-n-plus-one), measured against real
SQL Server. An earlier version of this project measured them against in-memory SQLite;
that was removed rather than maintained in parallel, since two implementations of the same
claim can drift apart and the SQL Server one is closer to what readers actually run.

Statement counts also aren't a benchmark — they're deterministic and need no statistical
machinery. Timings belong here; counts belong with the sample.

## Reporting a discrepancy

Different numbers on your hardware are expected for `Mean`. The *shape* of the result
should reproduce, and `Allocated` should match closely. If it doesn't, open an issue with
your `dotnet --info` output and the results you saw.
