using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Proves the streaming claims in
// https://www.jorgenhoc.org/en/blog/iasyncenumerable-csharp
//
// An async stream's behaviour is observable as hard facts: how many items the producer
// had created when the consumer saw the first one, whether a finally block ran, what a
// bounded channel does when full, and — for the HTTP claims — what actually arrives on
// a real socket. Demos 9-11 start an in-process Kestrel server on a loopback port and
// assert against real HTTP responses.

Console.WriteLine("1. Async iterator basics — await and yield return CAN share a statement");
Console.WriteLine("--------------------------------------------------------------------------");
{
    var items = new List<int>();
    await foreach (var i in BasicAsync(3)) items.Add(i);
    Check(items is [0, 1, 2], "the iterator produced 0, 1, 2 in order");

    // A claim you'll read (an earlier version of this article included it): "await and
    // yield return cannot appear in the same statement". This method disproves it:
    static async IAsyncEnumerable<int> YieldReturnAwaitAsync()
    {
        yield return await Task.FromResult(42); // one statement, both keywords
    }

    var single = new List<int>();
    await foreach (var i in YieldReturnAwaitAsync()) single.Add(i);
    Check(single is [42], "'yield return await ...' compiles and runs as ONE statement");
}

Console.WriteLine();
Console.WriteLine("2. Iterators are lazy and pull-driven — nothing runs until you ask");
Console.WriteLine("---------------------------------------------------------------------");
{
    var produced = 0;

    async IAsyncEnumerable<int> Producer()
    {
        for (var i = 1; i <= 3; i++)
        {
            await Task.Yield();
            produced++;
            yield return i;
        }
    }

    var sequence = Producer();
    Check(produced == 0, "calling the iterator method executed NOTHING");

    await using var enumerator = sequence.GetAsyncEnumerator();
    await enumerator.MoveNextAsync();
    Check(produced == 1 && enumerator.Current == 1,
        "the first MoveNextAsync produced exactly one item — the consumer sets the pace");
}

Console.WriteLine();
Console.WriteLine("3. WithCancellation flows the token into [EnumeratorCancellation]");
Console.WriteLine("--------------------------------------------------------------------");
{
    using var cts = new CancellationTokenSource();
    var finallyRan = false;

    async IAsyncEnumerable<int> Stream([EnumeratorCancellation] CancellationToken ct = default)
    {
        try
        {
            yield return 1;
            // waits forever — only the injected token can end this
            await Task.Delay(Timeout.Infinite, ct);
            yield return 2;
        }
        finally
        {
            finallyRan = true;
        }
    }

    var caughtToken = CancellationToken.None;
    try
    {
        // note: no token passed to Stream() itself — only via WithCancellation
        await foreach (var item in Stream().WithCancellation(cts.Token))
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(50)); // cancel while the iterator waits
        }
    }
    catch (OperationCanceledException ex)
    {
        caughtToken = ex.CancellationToken;
    }

    Check(caughtToken == cts.Token,
        "the OCE carries the caller's token — WithCancellation injected it into the iterator");
    Check(finallyRan, "the iterator's finally block ran despite the cancellation");
}

Console.WriteLine();
Console.WriteLine("4. Breaking out of await foreach disposes the enumerator and runs finally");
Console.WriteLine("----------------------------------------------------------------------------");
{
    var cleanupRan = false;

    async IAsyncEnumerable<int> WithCleanup()
    {
        try
        {
            yield return 1;
            await Task.Yield();
            yield return 2;
        }
        finally
        {
            cleanupRan = true; // stands in for closing a DB reader or file
        }
    }

    await foreach (var item in WithCleanup())
    {
        break; // consumer abandons the stream after one item
    }

    Check(cleanupRan, "finally ran when the consumer broke out early — await foreach disposed the enumerator");
}

Console.WriteLine();
Console.WriteLine("5. Exceptions inside the iterator surface at await foreach; finally still runs");
Console.WriteLine("---------------------------------------------------------------------------------");
{
    var finallyRan = false;

    async IAsyncEnumerable<string> Risky()
    {
        try
        {
            yield return "first";
            await Task.Yield();
            throw new InvalidOperationException("something went wrong");
        }
        finally
        {
            finallyRan = true;
        }
    }

    var received = new List<string>();
    var caught = false;
    try
    {
        await foreach (var item in Risky()) received.Add(item);
    }
    catch (InvalidOperationException) { caught = true; }

    Check(received is ["first"], "items yielded before the throw were delivered");
    Check(caught, "the exception surfaced at the await foreach, typed as thrown");
    Check(finallyRan, "the iterator's finally ran on the failure path");
}

Console.WriteLine();
Console.WriteLine("6. Time to first item: buffering waits for ALL rows, streaming waits for ONE");
Console.WriteLine("--------------------------------------------------------------------------------");
{
    const int N = 5;
    var produced = 0;

    async IAsyncEnumerable<int> Produce()
    {
        for (var i = 1; i <= N; i++)
        {
            await Task.Yield();
            produced++;
            yield return i;
        }
    }

    // Buffered: materialize everything, then consume (the ToListAsync shape)
    produced = 0;
    var buffered = new List<int>();
    await foreach (var i in Produce()) buffered.Add(i);
    var producedWhenBufferedConsumerStarts = produced; // consumption starts after this line
    Check(producedWhenBufferedConsumerStarts == N,
        $"buffered: all {N} items existed before the consumer touched the first one");

    // Streaming: consume as produced
    produced = 0;
    var producedAtFirstItem = -1;
    await foreach (var i in Produce())
    {
        producedAtFirstItem = produced;
        break;
    }
    Check(producedAtFirstItem == 1, "streaming: exactly 1 item existed when the consumer got the first one");
}

Console.WriteLine();
Console.WriteLine("7. Peak memory, measured: buffering 100k rows vs streaming them");
Console.WriteLine("------------------------------------------------------------------");
{
    const int N = 100_000;

    static async IAsyncEnumerable<ProductRow> ProduceRows(int n)
    {
        for (var i = 0; i < n; i++)
        {
            if (i % 10_000 == 0) await Task.Yield();
            yield return new ProductRow(i, $"Product {i} — padded name to look like a real one {i}", i * 0.01m);
        }
    }

    // Buffered: hold all rows at once (the ToListAsync shape)
    var baseline = GC.GetTotalMemory(forceFullCollection: true);
    var list = new List<ProductRow>();
    await foreach (var row in ProduceRows(N)) list.Add(row);
    var bufferedBytes = GC.GetTotalMemory(forceFullCollection: true) - baseline;
    var checksum = list.Count;
    list = null;

    // Streaming: aggregate row by row, sampling live heap along the way
    baseline = GC.GetTotalMemory(forceFullCollection: true);
    long streamingPeak = 0, total = 0;
    var count = 0;
    await foreach (var row in ProduceRows(N))
    {
        total += (long)row.Price;
        if (++count % 10_000 == 0)
            streamingPeak = Math.Max(streamingPeak, GC.GetTotalMemory(forceFullCollection: true) - baseline);
    }

    Console.WriteLine($"  measured: buffered {bufferedBytes / (1024 * 1024.0):F1} MB live vs streaming peak {Math.Max(streamingPeak, 0) / (1024 * 1024.0):F2} MB ({checksum} rows)");
    Check(checksum == N && count == N, "both approaches saw every row");
    Check(bufferedBytes > 5 * Math.Max(streamingPeak, 1),
        "the buffered approach holds >5x more live heap than the streaming peak");
}

Console.WriteLine();
Console.WriteLine("8. LINQ over IAsyncEnumerable is in the BCL since .NET 10 — and OrderBy buffers");
Console.WriteLine("----------------------------------------------------------------------------------");
{
    var produced = 0;

    async IAsyncEnumerable<int> Produce(int n)
    {
        for (var i = 1; i <= n; i++)
        {
            await Task.Yield();
            produced++;
            yield return i;
        }
    }

    // No System.Linq.Async package referenced anywhere in this project.
    var odds = await Produce(4).Where(i => i % 2 == 1).Select(i => i * 10).ToListAsync();
    Check(odds is [10, 30], "Where/Select/ToListAsync work straight from the BCL — no NuGet package");

    // OrderBy cannot yield anything until it has seen the whole sequence:
    produced = 0;
    await foreach (var i in Produce(4).OrderByDescending(i => i))
    {
        Check(produced == 4, "OrderBy delivered its FIRST item only after ALL 4 were produced — it buffers");
        break;
    }
}

Console.WriteLine();
Console.WriteLine("9. Channel<T>: bounded capacity gives real backpressure");
Console.WriteLine("----------------------------------------------------------");
{
    var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.Wait
    });

    await channel.Writer.WriteAsync(1);          // fills the single slot
    var secondWrite = channel.Writer.WriteAsync(2);
    Check(!secondWrite.IsCompleted, "the second write is BLOCKED — the channel is full and the producer waits");

    var received = new List<int>();
    await using var reader = channel.Reader.ReadAllAsync().GetAsyncEnumerator();

    await reader.MoveNextAsync();
    received.Add(reader.Current);                // frees the slot
    await secondWrite;                            // now completes
    channel.Writer.Complete();

    while (await reader.MoveNextAsync()) received.Add(reader.Current);
    Check(received is [1, 2], "ReadAllAsync exposed the channel as an IAsyncEnumerable, ending at Complete()");
}

// ---- the HTTP demos share one in-process Kestrel server on a loopback port ----
var firstChunkRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var serverSawDisconnect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddControllers();
var app = builder.Build();

async IAsyncEnumerable<int> GatedStream([EnumeratorCancellation] CancellationToken ct = default)
{
    yield return 1;
    // item 2 is not produced until the client confirms it RECEIVED item 1 —
    // so if the client sees item 1, the response is provably streamed, not buffered.
    await firstChunkRead.Task.WaitAsync(ct);
    yield return 2;
}

async IAsyncEnumerable<int> HangingStream([EnumeratorCancellation] CancellationToken ct = default)
{
    var aborted = false;
    try
    {
        yield return 1;
        await Task.Delay(Timeout.Infinite, ct); // only a client disconnect ends this
    }
    finally
    {
        if (ct.IsCancellationRequested) aborted = true;
        if (aborted) serverSawDisconnect.TrySetResult();
    }
}

app.MapGet("/stream", (CancellationToken ct) => GatedStream(ct));
app.MapGet("/hang", (CancellationToken ct) => HangingStream(ct));
app.MapControllers();

await app.StartAsync();
var baseUrl = app.Urls.First();
using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

Console.WriteLine();
Console.WriteLine("10. ASP.NET Core streams IAsyncEnumerable: chunked, and flushed while producing");
Console.WriteLine("----------------------------------------------------------------------------------");
{
    using var response = await http.GetAsync("/stream", HttpCompletionOption.ResponseHeadersRead);
    Check(response.Headers.TransferEncodingChunked == true,
        "the response is Transfer-Encoding: chunked — no Content-Length, no full buffering");

    await using var body = await response.Content.ReadAsStreamAsync();
    var buffer = new byte[256];
    var n = await body.ReadAsync(buffer);
    var firstChunk = System.Text.Encoding.UTF8.GetString(buffer, 0, n);

    Check(firstChunk.Contains('1') && !firstChunk.Contains('2'),
        $"the client received item 1 (\"{firstChunk}\") while item 2 did not exist yet");

    firstChunkRead.SetResult(); // let the server produce item 2
    using var rest = new StreamReader(body);
    var remainder = await rest.ReadToEndAsync();
    Check((firstChunk + remainder).Replace(" ", "") == "[1,2]",
        "the full body assembled to the JSON array [1,2]");
}

Console.WriteLine();
Console.WriteLine("11. Client disconnect cancels the iterator via RequestAborted");
Console.WriteLine("----------------------------------------------------------------");
{
    var response = await http.GetAsync("/hang", HttpCompletionOption.ResponseHeadersRead);
    var body = await response.Content.ReadAsStreamAsync();
    _ = await body.ReadAsync(new byte[64]); // make sure item 1 arrived, server is now waiting
    response.Dispose();                     // hang up mid-stream

    await serverSawDisconnect.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Check(true, "the endpoint's CancellationToken fired when the client hung up — the finally observed it");
}

Console.WriteLine();
Console.WriteLine("12. A controller action CAN be an async iterator");
Console.WriteLine("----------------------------------------------------");
{
    // NumbersController below is 'public async IAsyncEnumerable<int> Get()' with yield —
    // often claimed to be impossible for controller actions. It streams fine:
    var json = await http.GetStringAsync("/controller-stream");
    Check(json.Replace(" ", "") == "[1,2,3]", "the async-iterator action returned [1,2,3]");
}

await app.StopAsync();

Console.WriteLine();
Console.WriteLine("All checks passed. Streaming is a contract: one item exists at a time, and the consumer sets the pace.");

// Keep the window open when launched from an IDE — guarded, see the n-plus-one sample.
if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);
}

static void Check(bool condition, string claim)
{
    if (!condition)
        throw new InvalidOperationException($"CHECK FAILED: {claim}");
    Console.WriteLine($"  ok: {claim}");
}

static async IAsyncEnumerable<int> BasicAsync(int count)
{
    for (var i = 0; i < count; i++)
    {
        await Task.Yield();
        yield return i;
    }
}

internal sealed record ProductRow(long Id, string Name, decimal Price);

namespace JorgenHoc.AsyncStreams
{
    [ApiController]
    public sealed class NumbersController : ControllerBase
    {
        // An action method that IS an async iterator — no separate private method needed.
        // CA1822 wants it static, but MVC actions must be instance methods.
#pragma warning disable CA1822
        [HttpGet("/controller-stream")]
        public async IAsyncEnumerable<int> Get()
        {
            yield return 1;
            await Task.Yield();
            yield return 2;
            await Task.Yield();
            yield return 3;
        }
#pragma warning restore CA1822
    }
}
