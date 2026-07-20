using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Autohand.CodeAgentSdk;

namespace Autohand.CodeAgentSdk.StartupBenchmark;

internal static class Program
{
    private const int Warmups = 5;
    private const int Samples = 50;
    private const double BudgetMs = 50.0;

    public static async Task<int> Main(string[] args)
    {
        var fixture = await CompileFixtureAsync().ConfigureAwait(false);
        try
        {
            var sdkAssembly = typeof(AutohandSdk).Assembly.Location;
            var publicImport = await SamplePublicImportsAsync(sdkAssembly, ProbeAssembly()).ConfigureAwait(false);
            var startup = await SampleStartupAsync(fixture).ConfigureAwait(false);
            var metrics = new Dictionary<string, MetricResult>
            {
                ["publicImportMs"] = MetricResult.Create(publicImport, BudgetMs),
                ["sdkStartReturnMs"] = MetricResult.Create(startup.StartReturnMs, BudgetMs),
                ["fixtureSpawnToFirstRpcMs"] = MetricResult.Create(startup.FirstRpcMs, BudgetMs),
            };
            var result = new BenchmarkResult(
                "csharp",
                BudgetMs,
                metrics,
                metrics.Values.All(metric => metric.Passed));
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
            }));
            return result.Passed ? 0 : 1;
        }
        finally
        {
            File.Delete(fixture);
        }
    }

    private static async Task<IReadOnlyList<double>> SamplePublicImportsAsync(
        string sdkAssembly,
        string probeAssembly)
    {
        var values = new List<double>(Samples);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        for (var index = 0; index < Warmups + Samples; index++)
        {
            var process = StartProcess(dotnet, probeAssembly, sdkAssembly);
            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Public import probe failed: {error}{output}");
            }

            if (index >= Warmups)
            {
                values.Add(double.Parse(output.Trim(), CultureInfo.InvariantCulture));
            }
        }

        return values;
    }

    private static string ProbeAssembly()
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../Autohand.CodeAgentSdk.StartupProbe/bin",
            configuration,
            "net8.0",
            "Autohand.CodeAgentSdk.StartupProbe.dll"));
    }

    private static async Task<StartupSamples> SampleStartupAsync(string fixture)
    {
        var startReturn = new List<double>(Samples);
        var firstRpc = new List<double>(Samples);
        for (var index = 0; index < Warmups + Samples; index++)
        {
            await using var sdk = new AutohandSdk(new AutohandOptions
            {
                CliPath = fixture,
                RequestTimeout = TimeSpan.FromSeconds(5),
            });
            var timer = Stopwatch.StartNew();
            await sdk.StartAsync().ConfigureAwait(false);
            var startReturnedMs = timer.Elapsed.TotalMilliseconds;
            var state = await sdk.GetStateAsync().ConfigureAwait(false);
            var firstRpcMs = timer.Elapsed.TotalMilliseconds;
            if (state.GetProperty("status").GetString() != "idle")
            {
                throw new InvalidOperationException("The benchmark fixture did not return a usable getState result.");
            }

            if (index >= Warmups)
            {
                startReturn.Add(startReturnedMs);
                firstRpc.Add(firstRpcMs);
            }
        }

        return new StartupSamples(startReturn, firstRpc);
    }

    private static async Task<string> CompileFixtureAsync()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "fake_rpc_cli.c");
        var output = Path.Combine(Path.GetTempPath(), $"autohand-csharp-startup-{Environment.ProcessId}");
        var process = StartProcess("cc", "-O2", source, "-o", output);
        var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to compile benchmark fixture: {stdout}{stderr}");
        }

        return output;
    }

    private static Process StartProcess(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");
    }

    private sealed record StartupSamples(
        IReadOnlyList<double> StartReturnMs,
        IReadOnlyList<double> FirstRpcMs);

    private sealed record BenchmarkResult(
        string Language,
        double BudgetMs,
        IReadOnlyDictionary<string, MetricResult> Metrics,
        bool Passed);

    private sealed record MetricResult(
        int Samples,
        double MedianMs,
        double P95Ms,
        double MaxMs,
        bool Passed)
    {
        public static MetricResult Create(IReadOnlyList<double> source, double budgetMs)
        {
            var sorted = source.Order().ToArray();
            var median = Percentile(sorted, 0.50);
            var p95 = Percentile(sorted, 0.95);
            return new MetricResult(sorted.Length, median, p95, Math.Round(sorted[^1], 3), p95 < budgetMs);
        }

        private static double Percentile(IReadOnlyList<double> sorted, double percentile)
        {
            var index = Math.Max(0, (int)Math.Ceiling(percentile * sorted.Count) - 1);
            return Math.Round(sorted[index], 3);
        }
    }
}
