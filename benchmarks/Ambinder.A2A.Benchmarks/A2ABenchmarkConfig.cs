using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;

namespace Ambinder.A2A.Benchmarks;

internal static class A2ABenchmarkConfig
{
    // A2A_PERF_JOB=short → PR job. Anything else → default job (nightly, release).
    public static IConfig Create()
    {
        var shortJob = string.Equals(Environment.GetEnvironmentVariable("A2A_PERF_JOB"), "short", StringComparison.OrdinalIgnoreCase);
        var job = shortJob
            // 5 iterations, not 3: with 3 per side a Mann-Whitney test can never reach p < 0.05 (min p = 0.1).
            ? Job.Default.WithWarmupCount(3).WithIterationCount(5).WithId("Short")
            : Job.Default.WithId("Default");
        return ManualConfig.CreateEmpty()
            .AddJob(job.WithGcServer(false))
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(JsonExporter.Full)
            .AddExporter(MarkdownExporter.GitHub)
            .AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance)
            .AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default)
            .WithOptions(ConfigOptions.JoinSummary);
    }
}
