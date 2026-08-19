# ConfigureAwait(false), measured in Posts

Runnable measurement behind
[ConfigureAwait(false) in C# — The Complete Explanation](https://www.jorgenhoc.org/en/blog/configureawait-false-csharp).

Timing a context post is nanosecond noise, so this sample counts instead. Every plain
`await` that suspends on a `SynchronizationContext` resumes via exactly **one `Post`** to
it — and `ConfigureAwait(false)` skips exactly that Post. So a
[`CountingSynchronizationContext`](CountingSynchronizationContext.cs) (the UI-like
single-thread pump from `samples/async-deadlocks-csharp`, plus a counter on `Post`) turns
the article's claims into deterministic numbers:

| Demo | Awaits | Posts | Also asserted |
|---|---|---|---|
| 1 — plain awaits on the context | 5 | **5** | context survives, still on the UI-like thread |
| 2 — all `ConfigureAwait(false)` | 5 | **0** | `Current` is null after the first await, off the UI thread |
| 3 — `ConfigureAwait(false)` on the *first* await only | 1 + 4 plain | **0** | the propagation rule: plain awaits had no context left to capture |
| 4 — no context at all (console thread) | 2 | n/a | both forms behave identically — the ASP.NET Core case |
| 5 — exception after `ConfigureAwait(false)` | — | — | caught normally at the `await`; error flow untouched |

## Run it

```bash
cd samples/configureawait-false-csharp
dotnet run
```

No database, no configuration. Every line should start with `ok:` and the run ends in
about a second.

One measurement detail worth stealing: the harness that runs each demo on the context
thread uses `ConfigureAwait(false)` on its *own* await, so the reported counts contain
only the demo's Posts — and each demo reads the counter inside the method body, before
the harness resumes, as a second guarantee.
