# IAsyncEnumerable, proven claim by claim

Runnable proof behind
[IAsyncEnumerable&lt;T&gt; in C# — Streaming Data the Right Way](https://www.jorgenhoc.org/en/blog/iasyncenumerable-csharp).

A stream's behaviour is observable as hard facts: how many items the producer had created
when the consumer saw the first one, whether a finally block ran, what a bounded channel
does when full — and, for the HTTP claims, what actually arrives on a real socket. Demos
10–12 start an in-process Kestrel server on a loopback port and assert against real HTTP
responses. Gates (`TaskCompletionSource`) make the ordering deterministic: the server
cannot produce item 2 until the client proves it received item 1.

| Demo | Asserts |
|---|---|
| 1 — iterator basics | produces in order; `yield return await ...` is legal as one statement (a widely repeated claim says otherwise) |
| 2 — lazy pull | calling the iterator method runs nothing; the first `MoveNextAsync` produces exactly one item |
| 3 — cancellation | `WithCancellation` injects the token into `[EnumeratorCancellation]`; the OCE carries the caller's token; finally runs |
| 4 — early break | breaking out of `await foreach` disposes the enumerator and runs the iterator's finally |
| 5 — exceptions | items before the throw are delivered; the exception surfaces at `await foreach` typed as thrown; finally runs |
| 6 — time to first item | buffered: all N items exist before consumption starts; streaming: exactly 1 |
| 7 — memory, measured | 100k rows: buffered holds the whole graph live (~18 MB here), the streaming peak rounds to zero |
| 8 — LINQ in the BCL | `Where`/`Select`/`ToListAsync` with **no** `System.Linq.Async` package (.NET 10+); `OrderBy` yields its first item only after all are produced |
| 9 — `Channel<T>` | a bounded channel blocks the writer when full — real backpressure; `ReadAllAsync` ends at `Complete()` |
| 10 — HTTP streaming | `Transfer-Encoding: chunked`; the client holds item 1 in hand while item 2 provably does not exist yet |
| 11 — disconnect | dropping the connection mid-stream fires the endpoint's `CancellationToken` (`RequestAborted`) inside the iterator |
| 12 — controller action | an action declared `async IAsyncEnumerable<int>` with `yield` works — another claim that says "impossible" disproven |

## Run it

```bash
cd samples/iasyncenumerable-csharp
dotnet run
```

No database, no configuration, no external network — the HTTP demos talk to a Kestrel
instance the sample itself starts on `127.0.0.1`. Every line should start with `ok:`.
