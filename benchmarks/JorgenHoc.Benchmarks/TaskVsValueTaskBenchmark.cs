using BenchmarkDotNet.Attributes;

namespace JorgenHoc.Benchmarks;

/// <summary>
/// Backs the "ValueTask vs Task" article. The claim under test is narrow and specific:
/// on a path that completes synchronously, Task&lt;T&gt; allocates and ValueTask&lt;T&gt;
/// does not. Read the Allocated column, not Mean — the allocation difference is the
/// point, and the time difference at this scale is mostly noise.
///
/// The async-path benchmarks are included deliberately. They are what shows that
/// ValueTask buys nothing once you actually await, which is the half of the story that
/// gets left out when people convert every API in a codebase.
/// </summary>
[MemoryDiagnoser]
public class TaskVsValueTaskBenchmark
{
    private readonly Dictionary<int, string> _cache = new() { [1] = "cached" };

    // ---- Synchronous path: the case ValueTask exists for ----

    [Benchmark(Baseline = true, Description = "Task<T>, cache hit")]
    public async Task<string?> TaskCacheHit()
    {
        if (_cache.TryGetValue(1, out var val))
            return val;

        return await SlowTaskAsync();
    }

    [Benchmark(Description = "ValueTask<T>, cache hit")]
    public async ValueTask<string?> ValueTaskCacheHit()
    {
        if (_cache.TryGetValue(1, out var val))
            return val;

        return await SlowValueTaskAsync();
    }

    // ---- Asynchronous path: the case where ValueTask buys nothing ----

    [Benchmark(Description = "Task<T>, cache miss")]
    public async Task<string?> TaskCacheMiss()
    {
        if (_cache.TryGetValue(999, out var val))
            return val;

        return await SlowTaskAsync();
    }

    [Benchmark(Description = "ValueTask<T>, cache miss")]
    public async ValueTask<string?> ValueTaskCacheMiss()
    {
        if (_cache.TryGetValue(999, out var val))
            return val;

        return await SlowValueTaskAsync();
    }

    // Task.Yield rather than Task.Delay: Delay would measure the timer, not the
    // allocation behaviour we care about, and would make every column milliseconds.
    private static async Task<string?> SlowTaskAsync()
    {
        await Task.Yield();
        return null;
    }

    private static async ValueTask<string?> SlowValueTaskAsync()
    {
        await Task.Yield();
        return null;
    }
}
