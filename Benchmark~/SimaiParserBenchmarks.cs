using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace MajSimai.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Declared)]
public class ChartLayoutBenchmarks
{
    private string _compactChart = string.Empty;
    private string _oneTimingPerLineChart = string.Empty;

    [Params(256, 1024, 4096)]
    public int TimingCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _compactChart = BenchmarkInputs.CreateChart(TimingCount);
        _oneTimingPerLineChart = BenchmarkInputs.CreateChart(TimingCount, oneTimingPerLine: true);

        BenchmarkInputs.AssertPlainChart(_compactChart, TimingCount);
        BenchmarkInputs.AssertPlainChart(_oneTimingPerLineChart, TimingCount);
    }

    [Benchmark(Baseline = true)]
    public SimaiChart Compact()
    {
        return BenchmarkInputs.ParseChart(_compactChart);
    }

    [Benchmark]
    public SimaiChart OneTimingPerLine()
    {
        return BenchmarkInputs.ParseChart(_oneTimingPerLineChart);
    }
}

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Declared)]
public class HSpeedDensityBenchmarks
{
    private string _plainChart = string.Empty;
    private string _speedChangeEveryTimingChart = string.Empty;

    [Params(128, 512, 2048)]
    public int TimingCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _plainChart = BenchmarkInputs.CreateChart(TimingCount);
        _speedChangeEveryTimingChart = BenchmarkInputs.CreateChart(
            TimingCount,
            speedChangeEveryTiming: true);

        BenchmarkInputs.AssertPlainChart(_plainChart, TimingCount);
        var speedChart = BenchmarkInputs.ParseChart(_speedChangeEveryTimingChart);
        if (speedChart.NoteTimings.Length < TimingCount)
        {
            throw new InvalidOperationException("The dense HSpeed benchmark did not produce the expected timings.");
        }
    }

    [Benchmark(Baseline = true)]
    public SimaiChart Plain()
    {
        return BenchmarkInputs.ParseChart(_plainChart);
    }

    [Benchmark]
    public SimaiChart SpeedChangeEveryTiming()
    {
        return BenchmarkInputs.ParseChart(_speedChangeEveryTimingChart);
    }
}

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Declared)]
public class ParserApiBenchmarks
{
    private string _chart = string.Empty;
    private string _maidata = string.Empty;

    [Params(64, 1024)]
    public int TimingCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _chart = BenchmarkInputs.CreateChart(TimingCount);
        _maidata = BenchmarkInputs.CreateMaidata(_chart);

        BenchmarkInputs.AssertPlainChart(_chart, TimingCount);
        var metadata = SimaiParser.ParseMetadata(_maidata.AsSpan(), BenchmarkInputs.Hash);
        if (metadata.Fumens[4].Length == 0)
        {
            throw new InvalidOperationException("The benchmark maidata did not contain the expected chart.");
        }
    }

    [Benchmark(Baseline = true)]
    public SimaiChart ParseChart()
    {
        return BenchmarkInputs.ParseChart(_chart);
    }

    [Benchmark]
    public Task<SimaiChart> ParseChartAsync()
    {
        return SimaiParser.ParseChartAsync(_chart);
    }

    [Benchmark]
    public SimaiMetadata ParseMetadata()
    {
        return SimaiParser.ParseMetadata(_maidata.AsSpan(), BenchmarkInputs.Hash);
    }

    [Benchmark]
    public SimaiFile ParseWholeFile()
    {
        return SimaiParser.Parse(_maidata.AsSpan(), BenchmarkInputs.Hash);
    }
}
