using System.Text;

namespace MajSimai.Benchmarks;

internal static class BenchmarkInputs
{
    internal const string Hash = "MajSimai.Benchmarks";

    internal static string CreateChart(
        int timingCount,
        bool oneTimingPerLine = false,
        bool speedChangeEveryTiming = false)
    {
        if (timingCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timingCount));
        }

        var estimatedTimingLength = speedChangeEveryTiming ? 12 : 3;
        var builder = new StringBuilder(16 + timingCount * estimatedTimingLength);
        builder.Append("(180){4}");

        for (var i = 0; i < timingCount; i++)
        {
            if (speedChangeEveryTiming)
            {
                builder.Append((i & 1) == 0 ? "<SV*0.5>" : "<SV*2>");
            }

            builder.Append((char)('1' + i % 8));
            builder.Append(',');
            if (oneTimingPerLine)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    internal static string CreateMaidata(string chart)
    {
        var builder = new StringBuilder(chart.Length + 128);
        builder.Append("&title=Benchmark\n");
        builder.Append("&artist=MajSimai\n");
        builder.Append("&first=0\n");
        builder.Append("&des_5=Benchmark\n");
        builder.Append("&lv_5=14\n");
        builder.Append("&inote_5=\n");
        builder.Append(chart);
        return builder.ToString();
    }

    internal static SimaiChart ParseChart(string chart)
    {
        return SimaiParser.ParseChart(chart.AsSpan(), 0, out _);
    }

    internal static void AssertPlainChart(string chart, int expectedTimingCount)
    {
        var parsed = ParseChart(chart);
        if (parsed.NoteTimings.Length != expectedTimingCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedTimingCount} note timings, but parsed {parsed.NoteTimings.Length}.");
        }

        if (parsed.CommaTimings.Length != expectedTimingCount + 1)
        {
            throw new InvalidOperationException(
                $"Expected {expectedTimingCount + 1} comma timings, but parsed {parsed.CommaTimings.Length}.");
        }
    }
}
