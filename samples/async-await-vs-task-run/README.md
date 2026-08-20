# async-await-vs-task-run

Backs [async/await vs Task.Run in C#](https://www.jorgenhoc.org/en/blog/async-await-vs-task-run).

13 checks that throw on failure, making the article's I/O-vs-CPU distinction observable
through **thread ids and task completion state** rather than prose:

```bash
dotnet run
```

| Demo | Asserted |
|------|----------|
| CPU: direct vs `Task.Run` | a direct call runs the work on the **caller's own thread**; `Task.Run` moves the identical work to a **different pool thread** |
| Fake async vs real async | `Task.FromResult` returns an already-completed task whose body ran **synchronously on the calling thread**; a method awaiting `Task.Yield()` returns **before** completing |
| Fire-and-forget | an unawaited faulting task leaves the caller none the wiser; awaiting it later surfaces the exception; `ContinueWith(TaskScheduler.Default)` observes the fault |
| Channel + consumer loop | one poisoned message throws, the loop **survives** and still delivers the other two |
| Parallel CPU | `Task.WhenAll(Task.Run(...))` genuinely spreads 8 tasks across **8 distinct pool threads**, none of them the caller |

The parallelism check calls `ThreadPool.SetMinThreads` and uses a `Barrier` so the eight
tasks are forced to run simultaneously — otherwise the pool could satisfy them one after
another on a reused thread and the distinct-thread count would lie.

There is no database and no seed step — everything is in-process and deterministic.
