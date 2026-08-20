using System.Threading.Channels;

namespace JorgenHoc.AsyncVsTaskRun;

/// <summary>
/// The workloads the article contrasts: a deterministic CPU computation, a "fake async"
/// method, a genuinely async one, and the Channel-based background consumer it recommends
/// for reliable fire-and-forget.
/// </summary>
public static class Workloads
{
    /// <summary>
    /// Deterministic CPU-bound work. Returns the result AND the id of the thread it ran
    /// on, so the sample can prove where the work executed.
    /// </summary>
    public static (long Result, int ThreadId) Compute(int iterations)
    {
        long acc = 0;
        for (var i = 1; i <= iterations; i++)
            acc += (long)i * i % 7;
        return (acc, Environment.CurrentManagedThreadId);
    }

    /// <summary>
    /// A genuinely async method: <see cref="Task.Yield"/> forces the continuation to be
    /// scheduled rather than run inline, so the returned task is NOT complete on return.
    /// </summary>
    public static async Task<int> RealAsync()
    {
        await Task.Yield();
        return Environment.CurrentManagedThreadId;
    }

    public sealed record EmailMessage(string To, bool FailToSend);

    /// <summary>
    /// The article's recommended reliable pattern, distilled: a Channel feeds a consumer
    /// loop that catches per-item exceptions, so one poisoned message can't stop the pump.
    /// Returns the recipients that succeeded and how many failed.
    /// </summary>
    public static async Task<(List<string> Processed, int Failures)> RunEmailQueueAsync(
        IEnumerable<EmailMessage> messages)
    {
        var channel = Channel.CreateUnbounded<EmailMessage>();
        foreach (var message in messages)
            await channel.Writer.WriteAsync(message);
        channel.Writer.Complete();

        var processed = new List<string>();
        var failures = 0;

        await foreach (var message in channel.Reader.ReadAllAsync())
        {
            try
            {
                if (message.FailToSend)
                    throw new InvalidOperationException($"SMTP refused {message.To}");

                await Task.Delay(1); // stand-in for the real async send
                processed.Add(message.To);
            }
            catch (Exception)
            {
                // Exactly the BackgroundService behaviour the article argues for: the
                // loop survives, the next message is still delivered.
                failures++;
            }
        }

        return (processed, failures);
    }
}
