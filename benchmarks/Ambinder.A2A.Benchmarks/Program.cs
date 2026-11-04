using BenchmarkDotNet.Running;

namespace Ambinder.A2A.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, A2ABenchmarkConfig.Create());
}
