using BenchmarkDotNet.Running;
using MajSimai.Benchmarks;

BenchmarkSwitcher
    .FromAssembly(typeof(ChartLayoutBenchmarks).Assembly)
    .Run(args);
