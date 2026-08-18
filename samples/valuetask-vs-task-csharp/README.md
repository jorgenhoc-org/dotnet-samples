# ValueTask vs Task

Runnable evidence for
[ValueTask vs Task in C#](https://www.jorgenhoc.org/en/blog/valuetask-vs-task-csharp).

Two repositories with the **same method name and the same shape** — only the return type
differs — so `Task<T>` and `ValueTask<T>` can be compared line for line:

- [`TaskUserRepository`](TaskUserRepository.cs) — `async Task<User?> GetUserAsync(int id)`
- [`ValueTaskUserRepository`](ValueTaskUserRepository.cs) — `ValueTask<User?> GetUserAsync(int id)`

They cannot share an interface, because the return type is what changed. That is fine —
two interfaces would be ceremony for nothing.

## Run it

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "IF DB_ID('JorgenHocSamples') IS NULL CREATE DATABASE JorgenHocSamples;"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d JorgenHocSamples -E -i seed.sql
dotnet run
```

Ten users, deliberately. Nothing here scales with row count — the claim is about what a
single call allocates on a hit versus a miss, so the **hit/miss ratio** is what matters,
not table size.

## Expected output

```
Cache hit — bytes allocated per call, over 10,000 calls
| Task<User?>      |        160 |
| ValueTask<User?> |          0 |

Cache miss — bytes allocated per call, over 100 calls
| Task<User?>      |     15,509 |
| ValueTask<User?> |     15,465 |

  cache hit  -> second await SUCCEEDED
  cache miss -> second await SUCCEEDED
  pooled     -> second await threw InvalidOperationException
  AsTask()   -> second await SUCCEEDED
```

## What the numbers say

**On a cache hit, `ValueTask<User?>` allocates nothing and `Task<User?>` allocates 160
bytes.** That is the case `ValueTask` exists for, and it delivers completely.

**On a cache miss the two are within 0.3% of each other** — about 15.5 KB either way,
because the EF Core query pipeline dwarfs anything the wrapper does. Note the ratio: a miss
costs roughly a hundred times a hit. That is the whole decision. At a 95% hit rate the
saving is real; at 30% you have taken on the single-await restriction below for a rounding
error.

**Why 160 bytes here and 72 in the article's benchmark?** They measure different scopes.
BenchmarkDotNet's `[MemoryDiagnoser]` isolates allocations inside the benchmarked method;
`GC.GetTotalAllocatedBytes` counts everything the measuring loop does, including the
caller's own await machinery. The direction and the ratio reproduce; the absolute number
depends on where you draw the boundary. Neither is wrong, and quoting either without saying
which is how misleading benchmarks get published.

## Why not GetAllocatedBytesForCurrentThread

It is per-thread. A cache miss suspends at `await`, so its continuation resumes on a
different thread-pool thread and those allocations are never counted — the counter silently
under-reports any path that actually awaits. Using it here produced 1,520 B for the Task
miss against 20,967 B for the ValueTask miss, a 14x gap that is pure measurement artifact.
`GC.GetTotalAllocatedBytes(precise: true)` is process-wide and gives the honest answer.

## Why a Dictionary and not IMemoryCache

`IMemoryCache` allocates internally on both hit and miss, and evicts on its own schedule.
That noise would swamp the ~160 bytes under observation and make the result
non-reproducible. A plain `Dictionary` keeps the measurement about the thing being measured.

## The double-await result is the interesting one

The article calls awaiting a `ValueTask` twice undefined behaviour that "may read garbage or
crash". What actually happens is more useful to know:

With the **default** `AsyncValueTaskMethodBuilder`, a second await **succeeds** — on both
the synchronous and asynchronous paths. The state machine box stays alive after completion,
so nothing detects the violation. That is precisely why this rule is so easy to break
without noticing.

With `PoolingAsyncValueTaskMethodBuilder` ([PooledValueTaskSource.cs](PooledValueTaskSource.cs))
the box is returned to a pool once consumed, the second await reads a recycled object, and
the runtime throws `InvalidOperationException`. Anything backed by an `IValueTaskSource`
that recycles tokens behaves the same way — `Socket`, `System.IO.Pipelines`,
`SemaphoreSlim.WaitAsync`.

So "it worked when I tested it" proves nothing here. Whether your double await breaks
depends on the callee's builder, which is an implementation detail you do not control and
which can change under you. `AsTask()` is always safe.
