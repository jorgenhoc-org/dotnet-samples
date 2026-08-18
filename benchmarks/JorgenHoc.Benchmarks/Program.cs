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

DarkenHtmlReports();

// The screenshots in the articles are dark console output; BenchmarkDotNet's HTML report
// ships white, which sticks out next to them. Restyle every report after each run —
// idempotent, so re-running over already-darkened reports is a no-op.
static void DarkenHtmlReports()
{
    var resultsDir = Path.Combine(Environment.CurrentDirectory, "BenchmarkDotNet.Artifacts", "results");
    if (!Directory.Exists(resultsDir))
        return;

    const string marker = "<!-- jorgenhoc-dark -->";
    const string darkCss = marker + """
        <style type="text/css">
            body { background: #0c0c0c; color: #cccccc; font-family: Consolas, monospace; }
            pre, code { color: #cccccc; }
            td, th { border: 1px solid #3a3a3a; }
            tr { background-color: #0c0c0c; border-top: 1px solid #3a3a3a; }
            tr:nth-child(even) { background-color: #161616; }
            th { background-color: #1f1f1f; color: #e6e6e6; }
        </style>
        </head>
        """;

    foreach (var file in Directory.GetFiles(resultsDir, "*-report.html"))
    {
        var html = File.ReadAllText(file);
        if (html.Contains(marker, StringComparison.Ordinal))
            continue;
        File.WriteAllText(file, html.Replace("</head>", darkCss, StringComparison.Ordinal));
    }
}
