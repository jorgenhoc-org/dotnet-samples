# CancellationToken, proven claim by claim

Runnable proof behind
[CancellationToken in C# — Practical Patterns](https://www.jorgenhoc.org/en/blog/cancellationtoken-csharp).

Cancellation is cooperative, so every claim the article makes is observable as a hard
fact: did the loop stop, what state did the task end in, which exception type came out,
whose token is on it. Each demo asserts one of those outcomes — no timings, no races
(every token is cancelled *before* the work it should stop, or given an infinite wait it
must interrupt).

| Demo | Asserts |
|---|---|
| 1 — cooperative | a loop that never looks at the token processes 5/5 items *after* `Cancel()`; the observing version processes 0/5 and ends `Canceled`, not `Faulted` |
| 2 — graceful drain | `while (!token.IsCancellationRequested)` falls through: caller sees `RanToCompletion`, no exception |
| 3 — linked token | `CreateLinkedTokenSource` + `CancelAfter`: the `when (callerToken.IsCancellationRequested)` filter classifies both branches correctly |
| 4 — `Register` | fires exactly once across two `Cancel()` calls; a disposed registration never fires; registering on an already-cancelled token runs the callback synchronously on the current thread |
| 5 — `Task.Run` + pre-cancelled token | the delegate never executes; `await` throws `TaskCanceledException` (which *is* an `OperationCanceledException`) carrying the caller's token |
| 6 — `HttpClient` | against a local stalled server: `HttpClient.Timeout` → `TaskCanceledException` with **inner `TimeoutException`** (.NET 5+); a caller token → no inner `TimeoutException` and the caller's token reports cancelled |
| 7 — disposal | `Cancel()` after `Dispose()` throws `ObjectDisposedException` |

Demo 6 starts a `TcpListener` on a loopback port that accepts connections and never
responds — a deterministic way to make an HTTP request hang without touching the network.

## Run it

```bash
cd samples/cancellationtoken-csharp
dotnet run
```

No database, no configuration, no external network. Every line should start with `ok:`
and the run ends in about two seconds (most of it demo 6's 500 ms + 100 ms hangs).
