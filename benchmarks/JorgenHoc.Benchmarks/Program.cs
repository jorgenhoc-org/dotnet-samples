using BenchmarkDotNet.Running;
using JorgenHoc.Benchmarks;

// Release configuration is required — BenchmarkDotNet refuses a Debug build.
//
//   dotnet run -c Release                          run every benchmark
//   dotnet run -c Release -- --filter *ValueTask*   run a subset
//
// A switcher rather than BenchmarkRunner.Run<T>() so newly added benchmark classes are
// picked up without editing this file. RunAll() when no args are given, because the
// switcher would otherwise prompt interactively and hang a scripted run.

var switcher = BenchmarkSwitcher.FromAssembly(typeof(TaskVsValueTaskBenchmark).Assembly);

if (args.Length == 0)
    switcher.RunAll();
else
    switcher.Run(args);
