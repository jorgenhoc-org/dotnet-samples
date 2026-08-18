# Async deadlocks in C#

Runnable reproduction behind
[How to Avoid Async Deadlocks in C#](https://www.jorgenhoc.org/en/blog/async-deadlocks-csharp).

The article's claim is precise: the classic `.Result` deadlock needs **both** a
`SynchronizationContext` on the calling thread **and** that thread blocked. This sample
toggles one ingredient at a time and asserts the outcome of each combination:

| Demo | Context? | Blocking? | Outcome |
|---|---|---|---|
| 1 — plain console thread | no | yes | completes (bad practice, but no deadlock) |
| 2 — UI-like thread | yes | yes | **deadlock**, observed via timeout |
| 3 — UI-like thread + `ConfigureAwait(false)` | yes | yes | completes — continuation goes to the pool |
| 4 — UI-like thread + `Task.Run` | yes | yes | completes — the await captures nothing |

A console app has no `SynchronizationContext`, which is exactly why the deadlock never
reproduces in one — so [`UiLikeSynchronizationContext.cs`](UiLikeSynchronizationContext.cs)
(~40 lines) recreates the WinForms/WPF rule: all continuations come back to one dedicated
thread.

Two details worth stealing:

- **Nothing here hangs.** Every blocking call is the article's own diagnostic pattern —
  `task.Wait(TimeSpan)` — so the deadlock is *observed* (the wait times out) rather than
  suffered. No network either: `Task.Delay` stands in for the HTTP call, because the
  deadlock is about who resumes after the await, not what is awaited.
- **Check 2c is the proof of mechanism.** The moment the timed-out `Wait` releases the
  UI-like thread, the queued continuation runs and the task completes on its own —
  demonstrating the task was never stuck, it was only waiting for the thread that was
  waiting for it.

## Run it

```bash
cd samples/async-deadlocks-csharp
dotnet run
```

No database, no configuration. Expect every line to start with `ok:` and the run to end
with "All checks passed" in roughly five seconds (two of those are the deadlock timeout).
