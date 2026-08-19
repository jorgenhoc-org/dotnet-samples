using System.Net;
using System.Net.Sockets;

// Proves the cancellation claims in
// https://www.jorgenhoc.org/en/blog/cancellationtoken-csharp
//
// Cancellation is cooperative, so every claim here is observable as a hard fact:
// did the loop stop, what state did the task end in, which exception type came out,
// whose token is on it. No timings — each demo asserts a deterministic outcome.

Console.WriteLine("1. Cancellation is cooperative — a loop that ignores the token cannot be stopped");
Console.WriteLine("----------------------------------------------------------------------------------");
{
    using var cts = new CancellationTokenSource();
    cts.Cancel(); // cancelled BEFORE the work even starts

    var processed = 0;
    await Task.Run(async () =>
    {
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(10); // no token anywhere — nothing observes the cancellation
            processed++;
        }
    });
    Check(processed == 5, $"token cancelled up front, loop ignored it: {processed}/5 items processed anyway");

    var observedProcessed = 0;
    var observing = Task.Run(async () =>
    {
        for (var i = 0; i < 5; i++)
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10);
            observedProcessed++;
        }
    });
    var caughtToken = CancellationToken.None;
    try { await observing; }
    catch (OperationCanceledException ex) { caughtToken = ex.CancellationToken; }

    Check(observedProcessed == 0, "same loop observing the token: 0/5 items processed");
    Check(caughtToken == cts.Token, "OperationCanceledException.CancellationToken is the token we cancelled");
    Check(observing.Status == TaskStatus.Canceled, "the task ends Canceled, not Faulted — OCE is special-cased by the state machine");
}

Console.WriteLine();
Console.WriteLine("2. Graceful drain — IsCancellationRequested as loop condition ends RanToCompletion");
Console.WriteLine("-------------------------------------------------------------------------------------");
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var draining = Task.Run(async () =>
    {
        while (!cts.Token.IsCancellationRequested)
            await Task.Delay(10);
        // falls through — no exception, room for cleanup here
    });
    await draining;
    Check(draining.Status == TaskStatus.RanToCompletion,
        "bool-check loop exits normally: caller sees RanToCompletion, not Canceled");
}

Console.WriteLine();
Console.WriteLine("3. Linked token — telling MY timeout apart from the CALLER's cancellation");
Console.WriteLine("----------------------------------------------------------------------------");
{
    // Branch A: the operation's own timeout fires, the caller never cancelled.
    using var callerCts = new CancellationTokenSource();
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerCts.Token);
    linkedCts.CancelAfter(TimeSpan.FromMilliseconds(50));

    var classified = "";
    try { await Task.Delay(Timeout.Infinite, linkedCts.Token); }
    catch (OperationCanceledException) when (callerCts.Token.IsCancellationRequested) { classified = "caller"; }
    catch (OperationCanceledException) { classified = "timeout"; }
    Check(classified == "timeout", "linked token fired but caller token is clean -> classified as our timeout");

    // Branch B: the caller cancels; the same catch order classifies it correctly.
    using var callerCts2 = new CancellationTokenSource();
    using var linkedCts2 = CancellationTokenSource.CreateLinkedTokenSource(callerCts2.Token);
    linkedCts2.CancelAfter(TimeSpan.FromSeconds(30)); // generous timeout that never fires
    callerCts2.Cancel();

    try { await Task.Delay(Timeout.Infinite, linkedCts2.Token); classified = "none"; }
    catch (OperationCanceledException) when (callerCts2.Token.IsCancellationRequested) { classified = "caller"; }
    catch (OperationCanceledException) { classified = "timeout"; }
    Check(classified == "caller", "caller token fired -> classified as caller cancellation, not timeout");
}

Console.WriteLine();
Console.WriteLine("4. Register — when callbacks run, and what disposing the registration does");
Console.WriteLine("-----------------------------------------------------------------------------");
{
    using var cts = new CancellationTokenSource();

    var fired = 0;
    using var registration = cts.Token.Register(() => fired++);

    var disposedFired = 0;
    var disposedRegistration = cts.Token.Register(() => disposedFired++);
    disposedRegistration.Dispose(); // unregistered before Cancel

    cts.Cancel();
    cts.Cancel(); // second Cancel is a no-op
    Check(fired == 1, "callback fired exactly once, even with Cancel() called twice");
    Check(disposedFired == 0, "a disposed registration never fires — that is the leak-prevention story");

    // Registering on an already-cancelled token runs the callback synchronously, right here.
    var lateThread = -1;
    using var late = cts.Token.Register(() => lateThread = Environment.CurrentManagedThreadId);
    Check(lateThread == Environment.CurrentManagedThreadId,
        "Register on an already-cancelled token ran the callback synchronously on this thread");
}

Console.WriteLine();
Console.WriteLine("5. Task.Run with a pre-cancelled token — the delegate never executes");
Console.WriteLine("-----------------------------------------------------------------------");
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var ran = false;
    var task = Task.Run(() => { ran = true; }, cts.Token);

    var isTce = false;
    var tokenMatches = false;
    try { await task; }
    catch (OperationCanceledException ex)
    {
        isTce = ex is TaskCanceledException; // TCE derives from OCE — one catch handles both
        tokenMatches = ex.CancellationToken == cts.Token;
    }
    Check(!ran, "the delegate never ran — the token cancelled the scheduling itself");
    Check(task.Status == TaskStatus.Canceled, "task status is Canceled");
    Check(isTce && tokenMatches, "await threw TaskCanceledException (an OperationCanceledException) carrying our token");
}

Console.WriteLine();
Console.WriteLine("6. HttpClient — Timeout vs caller token produce distinguishable exceptions");
Console.WriteLine("------------------------------------------------------------------------------");
{
    // A local server that accepts the connection and then never responds. Loopback only,
    // no network flakiness — the request WILL hang until something cancels it.
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var stalledClients = new List<TcpClient>(); // keep connections open so nothing resets
    _ = Task.Run(async () =>
    {
        while (true)
            stalledClients.Add(await listener.AcceptTcpClientAsync());
    });

    // Case A: HttpClient.Timeout fires -> TaskCanceledException with inner TimeoutException (.NET 5+).
    using (var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) })
    {
        var innerIsTimeout = false;
        try { await http.GetAsync($"http://127.0.0.1:{port}/"); }
        catch (TaskCanceledException ex) { innerIsTimeout = ex.InnerException is TimeoutException; }
        Check(innerIsTimeout, "HttpClient.Timeout -> TaskCanceledException with InnerException TimeoutException");
    }

    // Case B: the caller's token fires -> no TimeoutException inside, and the caller's
    // token reports cancelled. THAT is the robust way to tell the two apart.
    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
    using (var callerCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
    {
        var innerIsTimeout = true;
        var callerWasCancelled = false;
        try { await http.GetAsync($"http://127.0.0.1:{port}/", callerCts.Token); }
        catch (TaskCanceledException ex)
        {
            innerIsTimeout = ex.InnerException is TimeoutException;
            callerWasCancelled = callerCts.Token.IsCancellationRequested;
        }
        Check(!innerIsTimeout && callerWasCancelled,
            "caller token -> no inner TimeoutException, and the caller's token is cancelled");
    }

    listener.Stop();
}

Console.WriteLine();
Console.WriteLine("7. Cancel() after Dispose() throws — ownership of the source matters");
Console.WriteLine("-----------------------------------------------------------------------");
{
    var cts = new CancellationTokenSource();
    cts.Dispose();

    var threw = false;
    try { cts.Cancel(); }
    catch (ObjectDisposedException) { threw = true; }
    Check(threw, "Cancel() on a disposed source throws ObjectDisposedException");
}

Console.WriteLine();
Console.WriteLine("All checks passed. The token never stops anything — code that observes it does.");

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
