# jorgenhoc.org — .NET samples

Runnable code behind the articles on [jorgenhoc.org](https://www.jorgenhoc.org). Every
performance number published in an article is produced by something in this repo, so you
can reproduce it rather than take it on trust.

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/jorgenhoc-org/dotnet-samples.git
cd dotnet-samples
dotnet build
```

## Layout

```
samples/<article-slug>/         one console project per article; folder name = article URL slug
samples/web/                    shared ASP.NET Core host for MiniProfiler demos
shared/JorgenHoc.DataAccess/    entities + DbContext for the EF Core articles
shared/JorgenHoc.Diagnostics/   statement counting and reporting used across EF samples
benchmarks/                     BenchmarkDotNet projects
```

Each `samples/` folder has its own README with what to run and what output to expect.

## Samples

| Article | Code |
|---|---|
| [Solving the N+1 query problem](https://www.jorgenhoc.org/en/blog/ef-core-n-plus-one) | [`samples/ef-core-n-plus-one`](samples/ef-core-n-plus-one) (console) · [`samples/web`](samples/web) (MiniProfiler) |
| [ValueTask vs Task in C#](https://www.jorgenhoc.org/en/blog/valuetask-vs-task-csharp) | [`samples/valuetask-vs-task-csharp`](samples/valuetask-vs-task-csharp) (console) · [`benchmarks/JorgenHoc.Benchmarks`](benchmarks/JorgenHoc.Benchmarks) |
| [Dockerizing a .NET application](https://www.jorgenhoc.org/en/blog/dotnet-docker-container) | [`samples/dotnet-docker-container`](samples/dotnet-docker-container) |
| [Azure App Service for .NET](https://www.jorgenhoc.org/en/blog/azure-app-service-dotnet) | [`samples/azure-app-service-dotnet`](samples/azure-app-service-dotnet) |
| [EF Core vs Dapper](https://www.jorgenhoc.org/en/blog/ef-core-vs-dapper) | [`samples/ef-core-vs-dapper`](samples/ef-core-vs-dapper) (console) · [`benchmarks/JorgenHoc.Benchmarks`](benchmarks/JorgenHoc.Benchmarks) |
| [How to avoid async deadlocks](https://www.jorgenhoc.org/en/blog/async-deadlocks-csharp) | [`samples/async-deadlocks-csharp`](samples/async-deadlocks-csharp) |
| [ConfigureAwait(false) explained](https://www.jorgenhoc.org/en/blog/configureawait-false-csharp) | [`samples/configureawait-false-csharp`](samples/configureawait-false-csharp) |
| [CancellationToken practical patterns](https://www.jorgenhoc.org/en/blog/cancellationtoken-csharp) | [`samples/cancellationtoken-csharp`](samples/cancellationtoken-csharp) |
| [Async exception handling](https://www.jorgenhoc.org/en/blog/async-exception-handling-csharp) | [`samples/async-exception-handling-csharp`](samples/async-exception-handling-csharp) |
| [IAsyncEnumerable — streaming data](https://www.jorgenhoc.org/en/blog/iasyncenumerable-csharp) | [`samples/iasyncenumerable-csharp`](samples/iasyncenumerable-csharp) |

More being ported — one per article.

**Samples vs benchmarks:** a sample is a console app you read top to bottom that
demonstrates a behaviour and reports deterministic counts. A benchmark measures timing and
allocation, needs `-c Release` and statistical machinery, and lives under `benchmarks/`.
Counts belong with the sample; timings belong with the benchmark.

## Conventions

**Folder name matches the article slug.** `samples/ef-core-n-plus-one` backs
`/blog/ef-core-n-plus-one`. No lookup table needed.

**Counts over timings.** Where a claim can be demonstrated with a statement count, an
allocation figure, or a row count, that is what gets measured. Those are deterministic and
reproduce on your machine. Wall-clock timings depend on hardware and network latency, so
they are reported with the environment attached and treated as illustrative.

**Samples read as much as they run.** Each one is a single `Program.cs` you can follow
top to bottom, with comments explaining the traps rather than just the happy path.

**Data layer is shared, entities are namespaced per article.** The articles model
different domains on purpose — `Order`/`Customer` here, `Post`/`Tag` for many-to-many — so
names collide by design. `JorgenHoc.DataAccess` keeps one folder and namespace per article
to prevent that, and one `DbContext` per article so a sample's schema contains only what
its article discusses. See [that project's README](shared/JorgenHoc.DataAccess/README.md).

## Found a discrepancy?

If a sample produces different numbers than the article claims, that is a bug worth
knowing about — open an issue with your `dotnet --info` output and what you saw.

## License

MIT — see [LICENSE](LICENSE).
