# Async exception handling, proven claim by claim

Runnable proof behind
[Async Exception Handling in C# — The Complete Guide](https://www.jorgenhoc.org/en/blog/async-exception-handling-csharp).

Exception routing is fully observable: which catch block ran, what type came out of the
await, what state the task ended in, what a stack trace contains. Each demo asserts one
of those outcomes. Demo 4 re-launches this executable as a child process so an async
void crash can be observed from the outside without killing the run.

| Demo | Asserts |
|---|---|
| 1 — await re-throw | `await` throws the original exception type (not `AggregateException`); the task ends `Faulted`; `task.Exception` is the `AggregateException` wrapper |
| 2 — throw before first await | calling the async method does NOT throw; the exception surfaces at the `await`; a non-async validating wrapper throws at the call site instead |
| 3 — async void + SyncContext | a `try/catch` around the async void call catches nothing; a custom `SynchronizationContext` receives the exception via `Post` |
| 4 — async void, no SyncContext | child process dies with a nonzero exit code; `AppDomain.UnhandledException` fires with `IsTerminating=true` — the normal unhandled-exception path, **not** `Environment.FailFast` (FailFast would skip that handler) |
| 5 — `Task.WhenAll` | `await` re-throws only the FIRST exception; the healthy task still ran to completion; `whenAll.Exception.InnerExceptions` holds every failure |
| 6 — nested aggregates | attached child tasks produce `AggregateException` inside `AggregateException`; `Flatten()` collapses it; WhenAll-of-WhenAll stays flat (all 3 leaf exceptions, no nesting) |
| 7 — `Task.WhenAny` | an already-faulted task wins the race; `await Task.WhenAny` itself never throws; the exception surfaces only when you await the winner |
| 8 — exception filters | a `when` filter that returns `false` observes the exception without catching it; the same instance keeps propagating |
| 9 — Canceled vs Faulted | an async method throwing `OperationCanceledException` ends `Canceled` even if its token was never cancelled; a sync `Task.Run` delegate throwing OCE ends `Faulted` unless the OCE's token matches the one passed to `Task.Run` |
| 10 — stack traces | `throw;` and `ExceptionDispatchInfo.Throw()` keep the original frame; `throw ex;` erases it; the `--- End of stack trace from previous location ---` seam renders in synchronous callers but not inside async methods |
| 11 — unobserved exceptions | `TaskScheduler.UnobservedTaskException` fires during garbage collection (not at fault time) and delivers an `AggregateException` |

## Run it

```bash
cd samples/async-exception-handling-csharp
dotnet run
```

No configuration and no network. Every line should start with `ok:`; the run takes a few
seconds (demo 4 spawns a child process, demo 11 forces GC cycles).
