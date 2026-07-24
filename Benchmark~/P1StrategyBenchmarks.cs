using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace MajSimai.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Declared)]
public class P1TextPositionStrategyBenchmarks
{
    private int[] _lineOffsets = Array.Empty<int>();
    private int[] _queryOffsets = Array.Empty<int>();

    [Params(256, 1024, 4096)]
    public int TimingCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var chart = BenchmarkInputs.CreateChart(TimingCount, oneTimingPerLine: true);
        var lineOffsets = new List<int>(TimingCount);
        var commaOffsets = new List<int>(TimingCount);
        for (var i = 0; i < chart.Length; i++)
        {
            if (chart[i] == '\n')
            {
                lineOffsets.Add(i);
            }
            else if (chart[i] == ',')
            {
                commaOffsets.Add(i);
            }
        }

        _lineOffsets = lineOffsets.ToArray();
        _queryOffsets = new int[commaOffsets.Count * 2 + 1];
        for (var i = 0; i < commaOffsets.Count; i++)
        {
            // ParseChart queries the note position and comma position separately.
            _queryOffsets[i * 2] = commaOffsets[i];
            _queryOffsets[i * 2 + 1] = commaOffsets[i];
        }
        _queryOffsets[^1] = chart.Length;

        var expected = TextPositionStrategies.CurrentLinearScan(_lineOffsets, _queryOffsets);
        AssertEqual(expected, TextPositionStrategies.BinarySearch(_lineOffsets, _queryOffsets));
        AssertEqual(expected, TextPositionStrategies.SequentialCursor(_lineOffsets, _queryOffsets));
    }

    [Benchmark(Baseline = true)]
    public long CurrentLinearScan()
    {
        return TextPositionStrategies.CurrentLinearScan(_lineOffsets, _queryOffsets);
    }

    [Benchmark]
    public long BinarySearch()
    {
        return TextPositionStrategies.BinarySearch(_lineOffsets, _queryOffsets);
    }

    [Benchmark]
    public long SequentialCursor()
    {
        return TextPositionStrategies.SequentialCursor(_lineOffsets, _queryOffsets);
    }

    private static void AssertEqual(long expected, long actual)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"Text position checksum mismatch: {expected} != {actual}.");
        }
    }
}

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Declared)]
public class P1HSpeedStrategyBenchmarks
{
    private BenchmarkHSpeedEvent[] _events = Array.Empty<BenchmarkHSpeedEvent>();
    private BenchmarkHSpeedQuery[] _queries = Array.Empty<BenchmarkHSpeedQuery>();
    private Dictionary<int, BenchmarkHSpeedEvent[]> _prebuiltGroupIndex = new();

    [Params(128, 512, 2048)]
    public int EventCount { get; set; }

    [Params(1, 8)]
    public int GroupCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (EventCount % GroupCount != 0)
        {
            throw new InvalidOperationException("EventCount must be divisible by GroupCount.");
        }

        (_events, _queries) = HSpeedStrategies.CreateBenchmarkData(EventCount, GroupCount);

        _prebuiltGroupIndex = HSpeedStrategies.BuildGroupIndex(_events);
        var expected = HSpeedStrategies.CurrentFullTableScan(_events, _queries);
        AssertEqual(expected, HSpeedStrategies.GroupBinarySearch(_prebuiltGroupIndex, _queries));
        AssertEqual(expected, HSpeedStrategies.BuildGroupIndexAndSearch(_events, _queries));
        AssertEqual(expected, HSpeedStrategies.SortedSweep(_events, _queries, GroupCount));
    }

    [Benchmark(Baseline = true)]
    public long CurrentFullTableScan()
    {
        return HSpeedStrategies.CurrentFullTableScan(_events, _queries);
    }

    [Benchmark]
    public long GroupBinary_PreBuiltIndex()
    {
        return HSpeedStrategies.GroupBinarySearch(_prebuiltGroupIndex, _queries);
    }

    [Benchmark]
    public long GroupBinary_BuildAndLookup()
    {
        return HSpeedStrategies.BuildGroupIndexAndSearch(_events, _queries);
    }

    [Benchmark]
    public long SortedSweep()
    {
        return HSpeedStrategies.SortedSweep(_events, _queries, GroupCount);
    }

    private static void AssertEqual(long expected, long actual)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"HSpeed checksum mismatch: {expected} != {actual}.");
        }
    }
}

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Declared)]
public class P1HSpeedFinalizationStrategyBenchmarks
{
    private BenchmarkHSpeedEvent[] _events = Array.Empty<BenchmarkHSpeedEvent>();
    private BenchmarkHSpeedQuery[] _queries = Array.Empty<BenchmarkHSpeedQuery>();

    [Params(128, 512, 2048)]
    public int EventCount { get; set; }

    [Params(1, 8)]
    public int GroupCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (_events, _queries) = HSpeedStrategies.CreateBenchmarkData(EventCount, GroupCount);

        var current = HSpeedStrategies.CurrentParallelFinalization(_events, _queries);
        var binary = HSpeedStrategies.GroupBinaryParallelFinalization(_events, _queries);
        var sweep = HSpeedStrategies.SortedSweepPrecompute(_events, _queries, GroupCount);
        if (current != binary || current != sweep)
        {
            throw new InvalidOperationException("HSpeed finalization strategy output mismatch.");
        }
    }

    [Benchmark(Baseline = true)]
    public long CurrentParallelFullTableScan()
    {
        return HSpeedStrategies.CurrentParallelFinalization(_events, _queries);
    }

    [Benchmark]
    public long GroupBinary_BuildThenParallelLookup()
    {
        return HSpeedStrategies.GroupBinaryParallelFinalization(_events, _queries);
    }

    [Benchmark]
    public long SortedSweep_PrecomputeForParallelParse()
    {
        return HSpeedStrategies.SortedSweepPrecompute(_events, _queries, GroupCount);
    }
}

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.Declared)]
public class P1RawContentStrategyBenchmarks
{
    private string[] _rawContents = Array.Empty<string>();
    private string[] _noteContents = Array.Empty<string>();
    private string[] _expectedTimingContents = Array.Empty<string>();

    [Params(128, 1024, 4096)]
    public int TimingCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _rawContents = new string[TimingCount];
        _noteContents = new string[TimingCount];
        _expectedTimingContents = new string[TimingCount];

        for (var i = 0; i < TimingCount; i++)
        {
            switch (i & 3)
            {
                case 0:
                    _rawContents[i] = "1c";
                    _noteContents[i] = "1";
                    _expectedTimingContents[i] = "1";
                    break;
                case 1:
                    _rawContents[i] = "1c@600-3[8:1]";
                    _noteContents[i] = "1-3[8:1]";
                    _expectedTimingContents[i] = "1@600-3[8:1]";
                    break;
                case 2:
                    _rawContents[i] = " B1c \n";
                    _noteContents[i] = "B1";
                    _expectedTimingContents[i] = "B1";
                    break;
                default:
                    _rawContents[i] = "1c-3[8:1] \r\n";
                    _noteContents[i] = "1-3[8:1]";
                    _expectedTimingContents[i] = "1-3[8:1]";
                    break;
            }
        }

        var current = CurrentPipeline();
        var preserveHSpeed = PreserveNormalizedContentDuringHSpeed();
        var trustTiming = TrustNormalizedContentInFinalTiming();
        AssertEquivalent(current, preserveHSpeed);
        AssertEquivalent(current, trustTiming);

        for (var i = 0; i < TimingCount; i++)
        {
            if (!string.Equals(current.TimingContents[i], _expectedTimingContents[i], StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected normalized timing content at index {i}.");
            }
        }
    }

    [Benchmark(Baseline = true)]
    public RawContentBatch CurrentPipeline()
    {
        var timingContents = new string[TimingCount];
        var noteContents = new string[TimingCount];
        for (var i = 0; i < TimingCount; i++)
        {
            var initialRawTiming = RawContentStrategies.NormalizeRawTiming(_rawContents[i]);
            var hSpeedUpdatedRawTiming = RawContentStrategies.NormalizeRawTiming(initialRawTiming);
            timingContents[i] = RawContentStrategies.NormalizeFinalTiming(hSpeedUpdatedRawTiming);
            noteContents[i] = new string(_noteContents[i].AsSpan());
        }
        return new RawContentBatch(timingContents, noteContents);
    }

    [Benchmark]
    public RawContentBatch PreserveNormalizedContentDuringHSpeed()
    {
        var timingContents = new string[TimingCount];
        var noteContents = new string[TimingCount];
        for (var i = 0; i < TimingCount; i++)
        {
            var normalizedRawTiming = RawContentStrategies.NormalizeRawTiming(_rawContents[i]);
            timingContents[i] = RawContentStrategies.NormalizeFinalTiming(normalizedRawTiming);
            noteContents[i] = new string(_noteContents[i].AsSpan());
        }
        return new RawContentBatch(timingContents, noteContents);
    }

    [Benchmark]
    public RawContentBatch TrustNormalizedContentInFinalTiming()
    {
        var timingContents = new string[TimingCount];
        var noteContents = new string[TimingCount];
        for (var i = 0; i < TimingCount; i++)
        {
            var normalizedRawTiming = RawContentStrategies.NormalizeRawTiming(_rawContents[i]);
            timingContents[i] = normalizedRawTiming;
            noteContents[i] = new string(_noteContents[i].AsSpan());
        }
        return new RawContentBatch(timingContents, noteContents);
    }

    private static void AssertEquivalent(RawContentBatch expected, RawContentBatch actual)
    {
        if (!expected.TimingContents.AsSpan().SequenceEqual(actual.TimingContents) ||
            !expected.NoteContents.AsSpan().SequenceEqual(actual.NoteContents))
        {
            throw new InvalidOperationException("RawContent strategy output mismatch.");
        }
    }
}

internal static class TextPositionStrategies
{
    internal static long CurrentLinearScan(int[] lineOffsets, int[] queryOffsets)
    {
        var checksum = 17L;
        foreach (var offset in queryOffsets)
        {
            var y = 1;
            var lastLineOffset = 0;
            foreach (var lineOffset in lineOffsets)
            {
                if (offset < lineOffset)
                {
                    break;
                }
                y++;
                lastLineOffset = lineOffset;
            }
            checksum = Mix(checksum, offset - lastLineOffset, y);
        }
        return checksum;
    }

    internal static long BinarySearch(int[] lineOffsets, int[] queryOffsets)
    {
        var checksum = 17L;
        foreach (var offset in queryOffsets)
        {
            var low = 0;
            var high = lineOffsets.Length;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (lineOffsets[middle] <= offset)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            var lastLineOffset = low == 0 ? 0 : lineOffsets[low - 1];
            checksum = Mix(checksum, offset - lastLineOffset, low + 1);
        }
        return checksum;
    }

    internal static long SequentialCursor(int[] lineOffsets, int[] queryOffsets)
    {
        var checksum = 17L;
        var lineIndex = 0;
        var y = 1;
        var lastLineOffset = 0;
        foreach (var offset in queryOffsets)
        {
            while (lineIndex < lineOffsets.Length && lineOffsets[lineIndex] <= offset)
            {
                lastLineOffset = lineOffsets[lineIndex++];
                y++;
            }
            checksum = Mix(checksum, offset - lastLineOffset, y);
        }
        return checksum;
    }

    private static long Mix(long checksum, int x, int y)
    {
        return unchecked((checksum * 397) ^ ((long)y << 32) ^ (uint)x);
    }
}

internal static class HSpeedStrategies
{
    internal static (BenchmarkHSpeedEvent[] Events, BenchmarkHSpeedQuery[] Queries) CreateBenchmarkData(
        int eventCount,
        int groupCount)
    {
        if (eventCount % groupCount != 0)
        {
            throw new InvalidOperationException("EventCount must be divisible by GroupCount.");
        }

        var events = new BenchmarkHSpeedEvent[eventCount];
        for (var i = 0; i < eventCount; i++)
        {
            var timeSlot = i / groupCount;
            var group = i % groupCount;
            var hSpeed = ((i * 17) % 19 - 9) * 0.25f;
            events[i] = new BenchmarkHSpeedEvent(timeSlot * 4L, group, hSpeed, i);
        }

        var queries = new BenchmarkHSpeedQuery[eventCount * 2];
        for (var i = 0; i < queries.Length; i++)
        {
            var timeSlot = i / groupCount;
            var group = i % groupCount;
            queries[i] = new BenchmarkHSpeedQuery(timeSlot * 2L, group);
        }
        return (events, queries);
    }

    internal static long CurrentFullTableScan(
        BenchmarkHSpeedEvent[] events,
        BenchmarkHSpeedQuery[] queries)
    {
        var checksum = 17L;
        foreach (var query in queries)
        {
            checksum = Mix(checksum, CurrentLookup(events, query));
        }
        return checksum;
    }

    internal static Dictionary<int, BenchmarkHSpeedEvent[]> BuildGroupIndex(
        BenchmarkHSpeedEvent[] events)
    {
        var groupLists = new Dictionary<int, List<BenchmarkHSpeedEvent>>();
        foreach (var speedEvent in events)
        {
            if (!groupLists.TryGetValue(speedEvent.Group, out var list))
            {
                list = new List<BenchmarkHSpeedEvent>();
                groupLists.Add(speedEvent.Group, list);
            }
            list.Add(speedEvent);
        }

        var result = new Dictionary<int, BenchmarkHSpeedEvent[]>(groupLists.Count);
        foreach (var pair in groupLists)
        {
            result.Add(pair.Key, pair.Value.ToArray());
        }
        return result;
    }

    internal static long GroupBinarySearch(
        Dictionary<int, BenchmarkHSpeedEvent[]> groupIndex,
        BenchmarkHSpeedQuery[] queries)
    {
        var checksum = 17L;
        foreach (var query in queries)
        {
            checksum = Mix(checksum, GroupBinaryLookup(groupIndex, query));
        }
        return checksum;
    }

    internal static long BuildGroupIndexAndSearch(
        BenchmarkHSpeedEvent[] events,
        BenchmarkHSpeedQuery[] queries)
    {
        return GroupBinarySearch(BuildGroupIndex(events), queries);
    }

    internal static long SortedSweep(
        BenchmarkHSpeedEvent[] events,
        BenchmarkHSpeedQuery[] queries,
        int groupCount)
    {
        var currentSpeeds = new Dictionary<int, float>(groupCount);
        var eventIndex = 0;
        var checksum = 17L;
        foreach (var query in queries)
        {
            while (eventIndex < events.Length && events[eventIndex].TimeKey <= query.TimeKey)
            {
                var speedEvent = events[eventIndex++];
                currentSpeeds[speedEvent.Group] = speedEvent.HSpeed;
            }
            var hSpeed = currentSpeeds.TryGetValue(query.Group, out var current) ? current : 1f;
            checksum = Mix(checksum, hSpeed);
        }
        return checksum;
    }

    internal static long CurrentParallelFinalization(
        BenchmarkHSpeedEvent[] events,
        BenchmarkHSpeedQuery[] queries)
    {
        var results = new float[queries.Length];
        Parallel.For(0, queries.Length, i => results[i] = CurrentLookup(events, queries[i]));
        return Checksum(results);
    }

    internal static long GroupBinaryParallelFinalization(
        BenchmarkHSpeedEvent[] events,
        BenchmarkHSpeedQuery[] queries)
    {
        var groupIndex = BuildGroupIndex(events);
        var results = new float[queries.Length];
        Parallel.For(0, queries.Length, i => results[i] = GroupBinaryLookup(groupIndex, queries[i]));
        return Checksum(results);
    }

    internal static long SortedSweepPrecompute(
        BenchmarkHSpeedEvent[] events,
        BenchmarkHSpeedQuery[] queries,
        int groupCount)
    {
        var precomputedSpeeds = new float[queries.Length];
        var currentSpeeds = new Dictionary<int, float>(groupCount);
        var eventIndex = 0;
        for (var i = 0; i < queries.Length; i++)
        {
            var query = queries[i];
            while (eventIndex < events.Length && events[eventIndex].TimeKey <= query.TimeKey)
            {
                var speedEvent = events[eventIndex++];
                currentSpeeds[speedEvent.Group] = speedEvent.HSpeed;
            }
            precomputedSpeeds[i] = currentSpeeds.TryGetValue(query.Group, out var current) ? current : 1f;
        }

        // The production parser still parses notes in parallel after this prepass.
        var results = new float[queries.Length];
        Parallel.For(0, queries.Length, i => results[i] = precomputedSpeeds[i]);
        return Checksum(results);
    }

    private static float CurrentLookup(
        BenchmarkHSpeedEvent[] events,
        BenchmarkHSpeedQuery query)
    {
        var hSpeed = 1f;
        var bestTimeKey = long.MinValue;
        var bestOrder = int.MinValue;
        foreach (var speedEvent in events)
        {
            if (speedEvent.Group != query.Group || speedEvent.TimeKey > query.TimeKey)
            {
                continue;
            }
            if (speedEvent.TimeKey > bestTimeKey ||
                (speedEvent.TimeKey == bestTimeKey && speedEvent.Order > bestOrder))
            {
                hSpeed = speedEvent.HSpeed;
                bestTimeKey = speedEvent.TimeKey;
                bestOrder = speedEvent.Order;
            }
        }
        return hSpeed;
    }

    private static float GroupBinaryLookup(
        Dictionary<int, BenchmarkHSpeedEvent[]> groupIndex,
        BenchmarkHSpeedQuery query)
    {
        if (!groupIndex.TryGetValue(query.Group, out var groupEvents))
        {
            return 1f;
        }

        var low = 0;
        var high = groupEvents.Length;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (groupEvents[middle].TimeKey <= query.TimeKey)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low == 0 ? 1f : groupEvents[low - 1].HSpeed;
    }

    private static long Checksum(float[] hSpeeds)
    {
        var checksum = 17L;
        foreach (var hSpeed in hSpeeds)
        {
            checksum = Mix(checksum, hSpeed);
        }
        return checksum;
    }

    private static long Mix(long checksum, float hSpeed)
    {
        return unchecked((checksum * 397) ^ (uint)BitConverter.SingleToInt32Bits(hSpeed));
    }
}

internal static class RawContentStrategies
{
    internal static string NormalizeRawTiming(string rawContent)
    {
        if (rawContent.Length == 0)
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[rawContent.Length];
        var contentLength = 0;
        foreach (var current in rawContent)
        {
            if (current == 'c')
            {
                continue;
            }
            buffer[contentLength++] = current == '\n' ? ' ' : current;
        }

        var normalizedContent = buffer[..contentLength];
        if (normalizedContent.Contains('@') && !IsFixedSoflanModifierSpacingValid(normalizedContent))
        {
            throw new InvalidOperationException("Invalid FixedSoflan modifier in benchmark input.");
        }

        var writeIndex = 0;
        for (var i = 0; i < normalizedContent.Length; i++)
        {
            var current = normalizedContent[i];
            if (!char.IsWhiteSpace(current))
            {
                buffer[writeIndex++] = current;
            }
        }
        return new string(buffer[..writeIndex]);
    }

    internal static string NormalizeFinalTiming(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[rawContent.Length];
        var writeIndex = 0;
        foreach (var current in rawContent)
        {
            var replaced = current == '\n' ? ' ' : current;
            if (!char.IsWhiteSpace(replaced))
            {
                buffer[writeIndex++] = replaced;
            }
        }
        return new string(buffer[..writeIndex]);
    }

    private static bool IsFixedSoflanModifierSpacingValid(ReadOnlySpan<char> rawContent)
    {
        for (var i = 0; i < rawContent.Length; i++)
        {
            if (rawContent[i] != '@')
            {
                continue;
            }

            var tokenStart = i - 1;
            while (tokenStart >= 0 &&
                   rawContent[tokenStart] != '/' &&
                   rawContent[tokenStart] != '`' &&
                   rawContent[tokenStart] != '*')
            {
                if (char.IsWhiteSpace(rawContent[tokenStart]))
                {
                    return false;
                }
                tokenStart--;
            }

            var hasSpeedChar = false;
            var seenTrailingWhitespace = false;
            for (var j = i + 1; j < rawContent.Length; j++)
            {
                var current = rawContent[j];
                if (current == '/' || current == '`' || current == '*' || IsSlideMark(current))
                {
                    break;
                }
                if (char.IsWhiteSpace(current))
                {
                    if (!hasSpeedChar)
                    {
                        return false;
                    }
                    seenTrailingWhitespace = true;
                    continue;
                }
                if (seenTrailingWhitespace)
                {
                    return false;
                }
                hasSpeedChar = true;
            }
        }
        return true;
    }

    private static bool IsSlideMark(char c)
    {
        return c is '-' or '^' or 'v' or '<' or '>' or 'V' or 'p' or 'q' or 's' or 'z' or 'w';
    }
}

public sealed class RawContentBatch
{
    public string[] TimingContents { get; }
    public string[] NoteContents { get; }

    public RawContentBatch(string[] timingContents, string[] noteContents)
    {
        TimingContents = timingContents;
        NoteContents = noteContents;
    }
}

internal readonly record struct BenchmarkHSpeedEvent(
    long TimeKey,
    int Group,
    float HSpeed,
    int Order);

internal readonly record struct BenchmarkHSpeedQuery(long TimeKey, int Group);
