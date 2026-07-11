using MajSimai.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MajSimai
{
    /// <summary>
    /// Provides methods to parse simai file
    /// </summary>
    public static class SimaiParser
    {
        readonly static Task<SimaiChart> SimaiChartCompletedTask = Task.FromResult(SimaiChart.Empty);
        #region Parse
        /// <summary>
        /// Read simai text from <paramref name="content"/> and parse it into <seealso cref="SimaiFile"/>.
        /// </summary>
        /// <param name="content">Simai text</param>
        /// <returns></returns>
        public static SimaiFile Parse(in ReadOnlySpan<char> content, string hash)
        {
            var metadata = ParseMetadata(content, hash);
            return Parse(metadata);
        }
        /// <summary>
        /// Parse simai <paramref name="metadata"/> into <seealso cref="SimaiFile"/>.
        /// </summary>
        /// <param name="metadata">Simai metadata</param>
        /// <returns></returns>
        public static SimaiFile Parse(SimaiMetadata metadata)
        {
            var rentedArrayForCharts = ArrayPool<SimaiChart>.Shared.Rent(7);
            try
            {
                Parallel.For(0, 7, i =>
                {
                    var fumen = metadata.Fumens[i];
                    var designer = metadata.Designers[i];
                    var level = metadata.Levels[i];
                    try
                    {
                        rentedArrayForCharts[i] = ParseChart(level, designer, fumen, default, out _);
                    }
                    catch (Exception ex)
                    {
                        rentedArrayForCharts[i] = new SimaiChart(metadata.Levels[i], metadata.Designers[i], metadata.Fumens[i], Array.Empty<SimaiTimingPoint>());
                        Console.WriteLine(ex);
                    }
                });
                var simaiFile = new SimaiFile(metadata.Title, metadata.Artist, metadata.Offset, metadata.Hash, rentedArrayForCharts, null);
                var cmds = simaiFile.Commands;
                var cmdCount = metadata.Commands.Length;
                for (var i = 0; i < cmdCount; i++)
                {
                    cmds.Add(metadata.Commands[i]);
                }

                return simaiFile;
            }
            finally
            {
                ArrayPool<SimaiChart>.Shared.Return(rentedArrayForCharts);
            }
        }
        /// <summary>
        /// Read simai text from <paramref name="contentStream"/> using UTF-8 encoding and parse it into <seealso cref="SimaiFile"/>. The Stream will be read to completion.
        /// </summary>
        /// <param name="contentStream">Provide simai content</param>
        /// <returns></returns>
        public static SimaiFile Parse(Stream contentStream)
        {
            return Parse(contentStream, Encoding.UTF8);
        }
        /// <summary>
        /// Read simai text from <paramref name="contentStream"/> using <paramref name="encoding"/> and parse it into <seealso cref="SimaiFile"/>. The Stream will be read to completion.
        /// </summary>
        /// <param name="contentStream">Provide simai content</param>
        /// <returns></returns>
        public static SimaiFile Parse(Stream contentStream, Encoding encoding)
        {
            var (contentBuffer, hash) = DecodeAndHash(contentStream, encoding);
            try
            {
                return Parse(contentBuffer.AsSpan(), hash);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(contentBuffer.Array!);
            }
        }
        /// <summary>
        /// Read simai text from <paramref name="content"/> and parse it into <seealso cref="SimaiFile"/>.
        /// </summary>
        /// <param name="content">Simai text</param>
        /// <returns></returns>
        public static Task<SimaiFile> ParseAsync(string content, string hash)
        {
            var buffer = ArrayPool<char>.Shared.Rent(content.Length);
            try
            {
                content.AsSpan().CopyTo(buffer);
                return ParseAsync(buffer.AsMemory(0, content.Length), hash);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }
        /// <summary>
        /// Read simai text from <paramref name="content"/> and parse it into <seealso cref="SimaiFile"/>.
        /// </summary>
        /// <param name="content">Simai text</param>
        /// <returns></returns>
        public static async Task<SimaiFile> ParseAsync(ReadOnlyMemory<char> content, string hash)
        {
            var metadata = await ParseMetadataAsync(content, hash);

            return await ParseAsync(metadata);
        }
        /// <summary>
        /// Parse simai <paramref name="metadata"/> into <seealso cref="SimaiFile"/>.
        /// </summary>
        /// <param name="metadata">Simai metadata</param>
        /// <returns></returns>
        public static async Task<SimaiFile> ParseAsync(SimaiMetadata metadata)
        {
            var rentedArrayForCharts = ArrayPool<SimaiChart>.Shared.Rent(7);
            var rentedArrayForTasks = ArrayPool<Task<SimaiChart>>.Shared.Rent(7);
            Array.Fill(rentedArrayForTasks, SimaiChartCompletedTask);
            try
            {
                for (var i = 0; i < 7; i++)
                {
                    var fumen = metadata.Fumens[i];
                    var designer = metadata.Designers[i];
                    var level = metadata.Levels[i];
                    rentedArrayForTasks[i] = ParseChartAsync(level, designer, fumen);
                }
                var tcs = new TaskCompletionSource<bool>();
                _ = Task.WhenAll(rentedArrayForTasks).ContinueWith(_ =>
                {
                    tcs.TrySetResult(true);
                });

                await tcs.Task;

                for (var i = 0; i < 7; i++)
                {
                    var task = rentedArrayForTasks[i];
                    if (task.IsCompletedSuccessfully)
                    {
                        rentedArrayForCharts[i] = task.Result;
                    }
                    else
                    {
                        rentedArrayForCharts[i] = new SimaiChart(metadata.Levels[i], metadata.Designers[i], metadata.Fumens[i], Array.Empty<SimaiTimingPoint>());
                        if (task.IsFaulted)
                        {
                            Console.WriteLine(task.Exception);
                        }
                    }
                }

                var simaiFile = new SimaiFile(metadata.Title, metadata.Artist, metadata.Offset, metadata.Hash, rentedArrayForCharts, null);
                var cmds = simaiFile.Commands;
                var cmdCount = metadata.Commands.Length;
                for (var i = 0; i < cmdCount; i++)
                {
                    cmds.Add(metadata.Commands[i]);
                }

                return simaiFile;
            }
            finally
            {
                ArrayPool<SimaiChart>.Shared.Return(rentedArrayForCharts);
                ArrayPool<Task<SimaiChart>>.Shared.Return(rentedArrayForTasks);
            }
        }
        /// <summary>
        /// Read simai text from <paramref name="contentStream"/> using UTF-8 encoding and parse it into <seealso cref="SimaiFile"/>. The Stream will be read to completion.
        /// </summary>
        /// <param name="contentStream">Provide simai content</param>
        /// <returns></returns>
        public static Task<SimaiFile> ParseAsync(Stream contentStream)
        {
            return ParseAsync(contentStream, Encoding.UTF8);
        }
        /// <summary>
        /// Read simai text from <paramref name="contentStream"/> using <paramref name="encoding"/> and parse it into <seealso cref="SimaiFile"/>. The Stream will be read to completion.
        /// </summary>
        /// <param name="contentStream">Provide simai content</param>
        /// <returns></returns>
        public static async Task<SimaiFile> ParseAsync(Stream contentStream, Encoding encoding)
        {
            var (contentBuffer, hash) = await DecodeAndHashAsync(contentStream, encoding);
            try
            {
                return await ParseAsync(contentBuffer.AsMemory(), hash);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(contentBuffer.Array!);
            }
        }
        #endregion
        #region ParseMetadata
        /// <summary>
        /// Read simai text from <paramref name="content"/> and parse it into <seealso cref="SimaiMetadata"/>. SimaiNote will not be parsed.
        /// </summary>
        /// <param name="content">Simai text</param>
        /// <returns></returns>
        /// <exception cref="InvalidSimaiMarkupException"></exception>
        public static SimaiMetadata ParseMetadata(in ReadOnlySpan<char> content, string hash)
        {
            static void SetValue(ReadOnlySpan<char> value, ref string valueStr)
            {
                if (!string.IsNullOrEmpty(valueStr))
                {
                    return;
                }
                else if (value.Length == 0)
                {
                    return;
                }
                valueStr = new string(value);
            }
            var title = string.Empty;
            var artist = string.Empty;
            var designer = string.Empty;
            var first = 0f;
            var rentedArrayForDesigners = ArrayPool<string>.Shared.Rent(7);
            var rentedArrayForFumens = ArrayPool<string>.Shared.Rent(7);
            var rentedArrayForLevels = ArrayPool<string>.Shared.Rent(7);

            var designers = rentedArrayForDesigners.AsSpan(0, 7);
            var fumens = rentedArrayForFumens.AsSpan(0, 7);
            var levels = rentedArrayForLevels.AsSpan(0, 7);
            var commands = ArrayPool<SimaiCommand>.Shared.Rent(16);
            var cI = 0;// for commands
            var i = 0;
            try
            {
                designers.Fill(string.Empty);
                fumens.Fill(string.Empty);
                levels.Fill(string.Empty);

                var lineCount = content.Count('\n') + 1;
                Span<Range> ranges = stackalloc Range[lineCount];
                lineCount = content.Split(ranges, '\n');
                ranges = ranges.Slice(0, lineCount);
                for (i = 0; i < lineCount; i++)
                {
                    var range = ranges[i];
                    var maidataTxt = content[range].Trim();
                    var tagIndex = maidataTxt.IndexOf('=');
                    if (maidataTxt.IsEmpty || maidataTxt[0] != '&')
                    {
                        continue;
                    }
                    else if (tagIndex == -1)
                    {
                        throw new InvalidSimaiMarkupException(i + 1, 0, maidataTxt.ToString());
                    }
                    var prefixStr = maidataTxt[1..tagIndex];
                    var valueStr = maidataTxt[(tagIndex + 1)..];
                    if (prefixStr.IsEmpty)
                    {
                        continue;
                    }
                    var prefix = new string(prefixStr);
                    var inotePrefix = 0;
                    switch (prefix)
                    {
                        case "title":
                            SetValue(valueStr, ref title);
                            break;
                        case "artist":
                            SetValue(valueStr, ref artist);
                            break;
                        case "des":
                            SetValue(valueStr, ref designer);
                            break;
                        case "des_1":
                            SetValue(valueStr, ref designers[0]);
                            break;
                        case "des_2":
                            SetValue(valueStr, ref designers[1]);
                            break;
                        case "des_3":
                            SetValue(valueStr, ref designers[2]);
                            break;
                        case "des_4":
                            SetValue(valueStr, ref designers[3]);
                            break;
                        case "des_5":
                            SetValue(valueStr, ref designers[4]);
                            break;
                        case "des_6":
                            SetValue(valueStr, ref designers[5]);
                            break;
                        case "des_7":
                            SetValue(valueStr, ref designers[6]);
                            break;
                        case "first":
                            if (!float.TryParse(valueStr, out first))
                            {
                                first = 0;
                            }
                            break;
                        case "lv_1":
                            SetValue(valueStr, ref levels[0]);
                            break;
                        case "lv_2":
                            SetValue(valueStr, ref levels[1]);
                            break;
                        case "lv_3":
                            SetValue(valueStr, ref levels[2]);
                            break;
                        case "lv_4":
                            SetValue(valueStr, ref levels[3]);
                            break;
                        case "lv_5":
                            SetValue(valueStr, ref levels[4]);
                            break;
                        case "lv_6":
                            SetValue(valueStr, ref levels[5]);
                            break;
                        case "lv_7":
                            SetValue(valueStr, ref levels[6]);
                            break;
                        case "inote_1":
                            inotePrefix = 1;
                            goto INOTE_PROCESSOR;
                        case "inote_2":
                            inotePrefix = 2;
                            goto INOTE_PROCESSOR;
                        case "inote_3":
                            inotePrefix = 3;
                            goto INOTE_PROCESSOR;
                        case "inote_4":
                            inotePrefix = 4;
                            goto INOTE_PROCESSOR;
                        case "inote_5":
                            inotePrefix = 5;
                            goto INOTE_PROCESSOR;
                        case "inote_6":
                            inotePrefix = 6;
                            goto INOTE_PROCESSOR;
                        case "inote_7":
                            inotePrefix = 7;
                            goto INOTE_PROCESSOR;
                        default:
                            BufferHelper.EnsureBufferLength(cI + 1, ref commands);
                            commands[cI++] = new SimaiCommand(prefix, new string(valueStr));
                            break;
                        INOTE_PROCESSOR:
                            {
                                if (inotePrefix == 0)
                                {
                                    throw new ArgumentOutOfRangeException();
                                }
                                var buffer = ArrayPool<char>.Shared.Rent(32);
                                try
                                {
                                    Array.Clear(buffer, 0, buffer.Length);
                                    var bufferIndex = 0;
                                    BufferHelper.EnsureBufferLength(valueStr.Length + 1, ref buffer);
                                    valueStr.CopyTo(buffer);
                                    bufferIndex += valueStr.Length;
                                    buffer[bufferIndex++] = '\n';
                                    i++;
                                    for (; i < lineCount; i++)
                                    {
                                        var isEOF = false;
                                        range = ranges[i];
                                        maidataTxt = content[range].Trim();
                                        if (!maidataTxt.IsEmpty)
                                        {
                                            if (maidataTxt[0] == '&')
                                            {
                                                isEOF = true;
                                                i--;
                                                break;
                                            }
                                            for (var i2 = 0; i2 < maidataTxt.Length; i2++)
                                            {
                                                ref readonly var current = ref maidataTxt[i2];
                                                //if (current == 'E')
                                                //{
                                                //    isEOF = true;
                                                //    break;
                                                //}
                                                BufferHelper.EnsureBufferLength(bufferIndex + 1, ref buffer);
                                                buffer[bufferIndex++] = current;
                                            }
                                            //if (isEOF)
                                            //{
                                            //    break;
                                            //}
                                        }

                                        BufferHelper.EnsureBufferLength(bufferIndex + 1, ref buffer);
                                        buffer[bufferIndex++] = '\n';
                                    }

                                    fumens[inotePrefix - 1] = new string(buffer.AsSpan(0, bufferIndex).Trim());
                                }
                                finally
                                {
                                    ArrayPool<char>.Shared.Return(buffer);
                                }
                            }
                            break;
                    }
                }
                //if (!string.IsNullOrEmpty(designer))
                //{
                //    for (var j = 0; j < 7; j++)
                //    {
                //        ref var d = ref designers[j];
                //        if (string.IsNullOrEmpty(d))
                //        {
                //            d = designer;
                //        }
                //    }
                //}
                var encoding = Encoding.UTF8;
                var byteCount = encoding.GetByteCount(content);
                var bytes = new byte[byteCount];
                encoding.GetBytes(content, bytes);
                return new SimaiMetadata(title,
                                         artist,
                                         first,
                                         designers,
                                         levels,
                                         fumens,
                                         commands.AsSpan(0, cI),
                                         hash);
            }
            catch (InvalidSimaiMarkupException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new InvalidSimaiMarkupException(i + 1, 0, "在maidata.txt第" + (i + 1) + "行:\n" + e.Message + "读取谱面时出现错误");
            }
            finally
            {
                ArrayPool<string>.Shared.Return(rentedArrayForDesigners);
                ArrayPool<string>.Shared.Return(rentedArrayForFumens);
                ArrayPool<string>.Shared.Return(rentedArrayForLevels);
                ArrayPool<SimaiCommand>.Shared.Return(commands);
            }
        }
        /// <summary>
        /// Read simai text from <paramref name="contentStream"/> using UTF-8 encoding and parse it into <seealso cref="SimaiMetadata"/>. SimaiNote will not be parsed.
        /// </summary>
        /// <param name="contentStream">Provide simai content</param>
        /// <returns></returns>
        /// <exception cref="InvalidSimaiMarkupException"></exception>
        public static SimaiMetadata ParseMetadata(Stream contentStream)
        {
            return ParseMetadata(contentStream, Encoding.UTF8);
        }
        /// <summary>
        /// Read simai text from <paramref name="contentStream"/> using <paramref name="encoding"/> and parse it into <seealso cref="SimaiMetadata"/>. SimaiNote will not be parsed. 
        /// <para>The Stream will be read to completion.</para>
        /// </summary>
        /// <param name="contentStream">Provide simai content</param>
        /// <returns></returns>
        /// <exception cref="InvalidSimaiMarkupException"></exception>
        public static SimaiMetadata ParseMetadata(Stream contentStream, Encoding encoding)
        {
            var (contentBuffer, hash) = DecodeAndHash(contentStream, encoding);
            try
            {
                return ParseMetadata(contentBuffer.AsSpan(), hash);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(contentBuffer.Array!);
            }
        }
        /// <summary>
        /// Read simai text from <paramref name="content"/> and parse it into <seealso cref="SimaiMetadata"/>. SimaiNote will not be parsed.
        /// <para>The Stream will be read to completion.</para>
        /// </summary>
        /// <param name="content">Simai text</param>
        /// <returns></returns>
        /// <exception cref="InvalidSimaiMarkupException"></exception>
        public static Task<SimaiMetadata> ParseMetadataAsync(string content, string hash)
        {
            return Task.Run(() => ParseMetadata(content, hash));
        }
        /// <summary>
        /// Read simai text from <paramref name="content"/> and parse it into <seealso cref="SimaiMetadata"/>. SimaiNote will not be parsed.
        /// </summary>
        /// <param name="content">Simai text</param>
        /// <returns></returns>
        /// <exception cref="InvalidSimaiMarkupException"></exception>
        public static Task<SimaiMetadata> ParseMetadataAsync(ReadOnlyMemory<char> content, string hash)
        {
            return Task.Run(() => ParseMetadata(content.Span, hash));
        }
        /// <summary>
        /// Read simai text from <paramref name="contentStream"/> using UTF-8 encoding and parse it into <seealso cref="SimaiMetadata"/>. SimaiNote will not be parsed.
        /// <para>The Stream will be read to completion.</para>
        /// </summary>
        /// <param name="contentStream">Provide simai content</param>
        /// <returns></returns>
        /// <exception cref="InvalidSimaiMarkupException"></exception>
        public static Task<SimaiMetadata> ParseMetadataAsync(Stream contentStream)
        {
            return ParseMetadataAsync(contentStream, Encoding.UTF8);
        }
        /// <summary>
        /// Read simai text from <paramref name="contentStream"/> using <paramref name="encoding"/> and parse it into <seealso cref="SimaiMetadata"/>. SimaiNote will not be parsed. 
        /// <para>The Stream will be read to completion.</para>
        /// </summary>
        /// <param name="contentStream">Provide simai content</param>
        /// <returns></returns>
        /// <exception cref="InvalidSimaiMarkupException"></exception>
        public static async Task<SimaiMetadata> ParseMetadataAsync(Stream contentStream, Encoding encoding)
        {
            var (contentBuffer, hash) = await DecodeAndHashAsync(contentStream, encoding);
            try
            {
                return await ParseMetadataAsync(contentBuffer.AsMemory(), hash);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(contentBuffer.Array!);
            }
        }
        #endregion
        #region ParseChart
        /// <summary>
        /// Read simai chart from <paramref name="fumen"/> and parse it into <seealso cref="SimaiChart"/>
        /// </summary>
        /// <param name="fumen">Simai chart</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SimaiChart ParseChart(ReadOnlySpan<char> fumen, long position, out double requestTime)
        {
            return ParseChart(string.Empty, string.Empty, fumen, position, out requestTime);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SimaiChart ParseChart(ReadOnlySpan<char> fumen, long position, int hSpeedInterpolationGrid, out double requestTime)
        {
            return ParseChart(string.Empty, string.Empty, fumen, position, hSpeedInterpolationGrid, out requestTime);
        }
        /// <summary>
        /// Read simai chart from <paramref name="fumen"/> and parse it into <seealso cref="SimaiChart"/>
        /// </summary>
        /// <param name="level">Level of simai chart</param>
        /// <param name="designer">designer of simai chart</param>
        /// <param name="fumen">Simai chart</param>
        /// <returns></returns>
        public static SimaiChart ParseChart(string level, string designer, ReadOnlySpan<char> fumen, long position, out double requestTime)
        {
            return ParseChart(level, designer, fumen, position, 32, out requestTime);
        }
        public static SimaiChart ParseChart(string level, string designer, ReadOnlySpan<char> fumen, long position, int hSpeedInterpolationGrid, out double requestTime)
        {
            requestTime = default;
            static bool IsNote(char c)
            {
                var isTapOrHoldOrSlide = c >= '0' && c <= '9';
                var isTouchOrTouchHold = c >= 'A' && c <= 'E';

                return isTapOrHoldOrSlide || isTouchOrTouchHold;
            }
            if (fumen.IsEmpty)
            {
                return new SimaiChart(level, designer, string.Empty, null);
            }
            if (hSpeedInterpolationGrid <= 0)
            {
                hSpeedInterpolationGrid = 32;
            }
            var noteContentBuffer = ArrayPool<char>.Shared.Rent(16);
            var commaTimingBuffer = ArrayPool<SimaiTimingPoint>.Shared.Rent(16);
            var rawTimingEntries = new List<RawTimingEntry>();
            var hSpeedEvents = new List<HSpeedEvent>();

            var noteContentBufIndex = 0;
            var commaTimingBufIndex = 0;
            var timingOrder = 0;

            float bpm = 0;
            Span<Range> signatureSplits = stackalloc Range[2];
            int signatureNumerator = 4;
            int signatureDenominator = 4;
            var curHSpeed = 1f;
            var hsGroupSpeeds = new Dictionary<int, float>(); // 各组独立速度
            var nextAutoGroupNum = -1; // <HS?*> 未定义组号变速组的下一个内部负数标号
            var currentSoflanGroup = 0; // 当前音符所属变速组
            var insideHsGroup = false; // 是否在 <HSg>(...) 的括号内
            double time = 0; //in seconds
            var beats = 4f; //{4}
            var haveNote = false;
            //var noteTemp = "";

            var cachedLineOffsetList = new List<int>();
            for (int i = 0; i < fumen.Length; i++)
            {
                if (fumen[i] == '\n')
                    cachedLineOffsetList.Add(i);
            }

            void getTextPosition(int offset, out int xCount, out int yCount)
            {
                yCount = 1;
                var lastLineOffsetBase = 0;

                foreach (var curLineOffsetBase in cachedLineOffsetList)
                {
                    if (offset < curLineOffsetBase)
                        break;

                    yCount++;
                    lastLineOffsetBase = curLineOffsetBase;
                }

                xCount = offset - lastLineOffsetBase;
            }

            int nextTimingOrder() => timingOrder++;

            void addRawTiming(double timing,
                              ReadOnlySpan<char> rawContent,
                              int textPosX,
                              int textPosY,
                              float rawBpm,
                              float hspeed,
                              int textPos,
                              int soflanGroup,
                              int order)
            {
                rawTimingEntries.Add(new RawTimingEntry(
                    new SimaiRawTimingPoint(timing, rawContent, textPosX, textPosY, rawBpm, hspeed, textPos, soflanGroup),
                    order));
            }

            void addHSpeedEvent(double timing, int soflanGroup, float hspeed, int order)
            {
                hSpeedEvents.Add(new HSpeedEvent(timing, soflanGroup, hspeed, order));
            }

            void addHSpeedRawTiming(double timing,
                                    int textPosX,
                                    int textPosY,
                                    float rawBpm,
                                    float hspeed,
                                    int textPos,
                                    int soflanGroup,
                                    int order)
            {
                ReadOnlySpan<char> noteContent = string.Empty;
                addHSpeedEvent(timing, soflanGroup, hspeed, order);
                addRawTiming(timing, noteContent, textPosX, textPosY, rawBpm, hspeed, textPos, soflanGroup, order);
            }

            void removeHSpeedEventsInRange(int soflanGroup, double startTime, double endTime)
            {
                var lowerBound = Math.Max(0d, startTime);
                hSpeedEvents.RemoveAll(hs =>
                    hs.SoflanGroup == soflanGroup &&
                    IsAfterTime(hs.Timing, lowerBound) &&
                    !IsAfterTime(hs.Timing, endTime));
                rawTimingEntries.RemoveAll(entry =>
                    IsHSpeedRawTiming(entry.RawTiming) &&
                    entry.RawTiming.SoflanGroup == soflanGroup &&
                    IsAfterTime(entry.RawTiming.Timing, lowerBound) &&
                    !IsAfterTime(entry.RawTiming.Timing, endTime));
            }

            void addHSpeedInterpolation(int soflanGroup,
                                        List<HSpeedSegment> segments,
                                        int textPosX,
                                        int textPosY,
                                        int textPos)
            {
                if (segments.Count == 0)
                {
                    throw new InvalidSimaiSyntaxException(textPosY, textPosX, "<HS>", "HSpeed chain interpolation requires at least one segment");
                }
                if (bpm <= 0)
                {
                    throw new InvalidSimaiSyntaxException(textPosY, textPosX, "<HS>", "HS interpolation requires a valid BPM");
                }
                var totalDuration = 0d;
                for (var i = 0; i < segments.Count; i++)
                {
                    if (segments[i].Duration <= 0)
                    {
                        throw new InvalidSimaiSyntaxException(textPosY, textPosX, "<HS>", "HS interpolation duration must be greater than zero");
                    }
                    totalDuration += segments[i].Duration;
                }

                var endTime = time;
                var startTime = endTime - totalDuration;
                var startHSpeed = GetEffectiveHSpeed(hSpeedEvents, soflanGroup, startTime);
                removeHSpeedEventsInRange(soflanGroup, startTime, endTime);

                var gridSeconds = 60d / bpm / 384d;
                var stepSeconds = gridSeconds * hSpeedInterpolationGrid;
                var sampleTimes = new List<double>();
                var segmentStartTime = startTime;
                for (var i = 0; i < segments.Count; i++)
                {
                    if (!IsAfterTime(segmentStartTime, 0d))
                    {
                        AddUniqueTime(sampleTimes, 0d);
                    }
                    else
                    {
                        AddUniqueTime(sampleTimes, segmentStartTime);
                    }

                    segmentStartTime += segments[i].Duration;
                    if (!IsAfterTime(segmentStartTime, 0d))
                    {
                        continue;
                    }
                    AddUniqueTime(sampleTimes, segmentStartTime);
                }

                if (startTime >= 0)
                {
                    AddUniqueTime(sampleTimes, startTime);
                }
                else
                {
                    AddUniqueTime(sampleTimes, 0);
                }

                var firstAlignedStep = (long)Math.Ceiling(Math.Max(0d, startTime) / stepSeconds - TimeEpsilon);
                for (var stepIndex = firstAlignedStep; ; stepIndex++)
                {
                    var sampleTime = stepIndex * stepSeconds;
                    if (IsAfterTime(sampleTime, endTime))
                    {
                        break;
                    }
                    if (IsAfterTime(sampleTime, Math.Max(0d, startTime)) && IsAfterTime(endTime, sampleTime))
                    {
                        AddUniqueTime(sampleTimes, sampleTime);
                    }
                }

                AddUniqueTime(sampleTimes, endTime);
                sampleTimes.Sort(CompareTime);

                foreach (var sampleTime in sampleTimes)
                {
                    var hspeed = GetInterpolatedHSpeedAt(segments, startHSpeed, startTime, sampleTime);
                    if (IsSameTime(sampleTime, endTime))
                    {
                        hspeed = segments[segments.Count - 1].TargetHSpeed;
                    }

                    addHSpeedRawTiming(sampleTime, textPosX, textPosY, bpm, hspeed, textPos, soflanGroup, nextTimingOrder());
                }
            }

            static float GetInterpolatedHSpeedAt(List<HSpeedSegment> segments, float startHSpeed, double startTime, double sampleTime)
            {
                var segmentStartTime = startTime;
                var segmentStartHSpeed = startHSpeed;
                for (var i = 0; i < segments.Count; i++)
                {
                    var segment = segments[i];
                    var segmentEndTime = segmentStartTime + segment.Duration;
                    if (IsAfterTime(sampleTime, segmentEndTime) && i < segments.Count - 1)
                    {
                        segmentStartTime = segmentEndTime;
                        segmentStartHSpeed = segment.TargetHSpeed;
                        continue;
                    }

                    var progress = (sampleTime - segmentStartTime) / segment.Duration;
                    if (progress < 0)
                    {
                        progress = 0;
                    }
                    else if (progress > 1)
                    {
                        progress = 1;
                    }
                    var easedProgress = ApplyHSpeedEasing(segment.Easing, progress);
                    var hspeed = (float)(segmentStartHSpeed + (segment.TargetHSpeed - segmentStartHSpeed) * easedProgress);
                    if (IsSameTime(sampleTime, segmentStartTime))
                    {
                        hspeed = segmentStartHSpeed;
                    }
                    else if (IsSameTime(sampleTime, segmentEndTime))
                    {
                        hspeed = segment.TargetHSpeed;
                    }
                    return hspeed;

                }

                return segments[segments.Count - 1].TargetHSpeed;
            }

            /// Xcount| 1 2 3 4 5 6 7 8 9 10| 
            /// --------------------------------
            ///       | A B C D E F G H I J | 1
            ///       | K L N M O P Q R F T | 2
            ///       | U V W X Y Z 1 1 4 5 | 3
            ///       | 1 4 X M M C G H H H | 4
            /// ----------------------------| Ycount
            try
            {
                for (var i = 0; i < fumen.Length; i++)
                {
                    if (i - 1 < position)
                        requestTime = time;

                    switch (fumen[i])
                    {
                        case '|': // 跳过注释
                            {
                                var str = fumen[i..];
                                if (str.Length >= 2 && str[1] == '|')
                                {
                                    i += 2;

                                    if (str.Length >= 6 && fumen[i] == 's') // ||sx/x
                                    {
                                        var startAt = i + 1;
                                        i++;
                                        for (; i < fumen.Length; i++)
                                        {
                                            if (fumen[i] == '\n')
                                                break;
                                        }
                                        var endAt = i;
                                        var signatureStr = fumen[startAt..endAt].Trim();

                                        if (signatureStr.Split(signatureSplits, '/') >= 2)
                                        {
                                            if (!int.TryParse(signatureStr[signatureSplits[0]], out signatureNumerator))
                                            {
                                                signatureNumerator = 4;
                                            }
                                            if (!int.TryParse(signatureStr[signatureSplits[1]], out signatureDenominator))
                                            {
                                                signatureDenominator = 4;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        for (; i < fumen.Length; i++)
                                        {
                                            if (fumen[i] == '\n')
                                                break;
                                        }
                                    }
                                }
                                else
                                {
                                    var s = fumen[i].ToString();
                                    getTextPosition(i, out var Xcount, out var Ycount);
                                    throw new InvalidSimaiMarkupException(Ycount, Xcount, s, $"Unexpected character \"{s}\"");
                                }
                            }
                            continue;
                        case '(': //Get bpm
                            {
                                haveNote = false;
                                //noteTemp = "";
                                noteContentBufIndex = 0;

                                if (fumen[i..].Length >= 3) // (x)
                                {
                                    var startAt = i + 1;
                                    i++;
                                    for (; i < fumen.Length; i++)
                                    {
                                        if (fumen[i] == '\n')
                                            continue;
                                        else if (fumen[i] == ')')
                                            break;
                                    }
                                    var endAt = i;
                                    var bpmStr = fumen[startAt..endAt].Trim();

                                    if (!float.TryParse(bpmStr, out bpm))
                                    {
                                        getTextPosition(i, out var Xcount, out var Ycount);
                                        throw new InvalidSimaiSyntaxException(Ycount, Xcount, bpmStr.ToString(), "BPM value must be a number");
                                    }
                                }
                                else
                                {
                                    var s = fumen[i].ToString();
                                    getTextPosition(i, out var Xcount, out var Ycount);
                                    throw new InvalidSimaiMarkupException(Ycount, Xcount, s, $"Unexpected character \"{s}\"");
                                }
                                //Console.WriteLine("BPM" + bpm);
                            }
                            continue;
                        case '{'://Get beats
                            {
                                haveNote = false;
                                //noteTemp = "";
                                noteContentBufIndex = 0;

                                if (fumen[i..].Length >= 3) // {x}
                                {
                                    var startAt = i + 1;
                                    i++;
                                    for (; i < fumen.Length; i++)
                                    {
                                        if (fumen[i] == '\n')
                                        {
                                            continue;
                                        }
                                        else if (fumen[i] == '}')
                                        {
                                            break;
                                        }
                                    }
                                    var endAt = i;
                                    var beatsStr = fumen[startAt..endAt].Trim();

                                    if (beatsStr.IsEmpty)
                                    {
                                        getTextPosition(i, out var Xcount, out var Ycount);
                                        throw new InvalidSimaiSyntaxException(Ycount, Xcount, fumen[startAt..endAt].ToString(), "Beats value must be a number");
                                    }
                                    else if (beatsStr[0] == '#')
                                    {
                                        if (!float.TryParse(beatsStr[1..], out var beatInterval))
                                        {
                                            getTextPosition(i, out var Xcount, out var Ycount);
                                            throw new InvalidSimaiSyntaxException(Ycount, Xcount, beatsStr.ToString(), "Beats value must be a number");
                                        }
                                        beats = 240f / (bpm * beatInterval);
                                    }
                                    else if (!float.TryParse(beatsStr, out beats))
                                    {
                                        getTextPosition(i, out var Xcount, out var Ycount);
                                        throw new InvalidSimaiSyntaxException(Ycount, Xcount, beatsStr.ToString(), "Beats value must be a number");
                                    }
                                }
                                else
                                {
                                    var s = fumen[i].ToString();
                                    getTextPosition(i, out var Xcount, out var Ycount);
                                    throw new InvalidSimaiMarkupException(Ycount, Xcount, s, $"Unexpected character \"{s}\"");
                                }
                                //Console.WriteLine("BEAT" + beats);
                            }
                            continue;
                        case '<':// Get HS: <HS*1.0>
                            {
                                if (insideHsGroup && IsHSpeedTagStart(fumen, i))
                                {
                                    getTextPosition(i, out var Xcount, out var Ycount);
                                    throw new InvalidSimaiSyntaxException(Ycount, Xcount, fumen[i].ToString(), "HS declaration is not allowed inside HS group scope");
                                }
                                if (haveNote)
                                {
                                    break;
                                }
                                haveNote = false;
                                //noteTemp = "";
                                noteContentBufIndex = 0;

                                if (fumen[i..].Length >= 4) // <HS*x>
                                {
                                    var startAt = i + 1;
                                    var buffer = ArrayPool<char>.Shared.Rent(16);
                                    var bufferIndex = 0;
                                    var tagIndex = -1; // position of '*'
                                    i++;
                                    try
                                    {
                                        for (; i < fumen.Length; i++)
                                        {
                                            ref readonly var currentChar = ref fumen[i];
                                            if (currentChar == '\n')
                                            {
                                                continue;
                                            }
                                            else if (currentChar == '*')
                                            {
                                                if (tagIndex != -1)
                                                {
                                                    getTextPosition(i, out var Xcount, out var Ycount);
                                                    throw new InvalidSimaiSyntaxException(Ycount, Xcount, fumen[(startAt - 1)..(i + 1)].ToString(), "Unexpected HS declaration syntax");
                                                }
                                                tagIndex = bufferIndex;
                                            }
                                            else if (currentChar == '>')
                                            {
                                                break;
                                            }
                                            BufferHelper.EnsureBufferLength(bufferIndex + 1, ref buffer);
                                            buffer[bufferIndex++] = currentChar;
                                        }
                                        if (HasLineBreakInsideHSpeedEasingName(fumen[startAt..i]))
                                        {
                                            getTextPosition(i, out var Xcount, out var Ycount);
                                            throw new InvalidSimaiSyntaxException(Ycount, Xcount, fumen[(startAt - 1)..Math.Min(i + 1, fumen.Length)].ToString(), "HSpeed easing name cannot contain line breaks");
                                        }
                                        var hsContent = buffer.AsSpan(0, bufferIndex);
                                        if (hsContent.IsEmpty ||
                                            hsContent.Length < 3 ||
                                            hsContent[0] != 'H' ||
                                            hsContent[1] != 'S')
                                        {
                                            getTextPosition(i, out var Xcount, out var Ycount);
                                            throw new InvalidSimaiSyntaxException(Ycount, Xcount, hsContent.ToString(), "Unexpected HS declaration syntax");
                                        }

                                        var hsBody = hsContent[2..]; // after "HS"
                                        var hsGroupNum = 0;
                                        var hasGroup = false;
                                        var isAutoGroup = false;
                                        var hasHSpeedChange = false;
                                        var hSpeedSegments = new List<HSpeedSegment>();
                                        var hasInterpolation = false;
                                        var commandOrder = nextTimingOrder();
                                        var hsValue = 1f;

                                        if (tagIndex != -1)
                                        {
                                            // Has '*': could be <HS*x> or <HSg*x>
                                            var beforeStar = hsContent[2..tagIndex];
                                            var afterStar = hsContent[(tagIndex + 1)..].Trim();

                                            if (!beforeStar.IsEmpty)
                                            {
                                                // <HSg*x> format, or <HS?*x> auto (undefined) group
                                                if (beforeStar.Length == 1 && beforeStar[0] == '?')
                                                {
                                                    hsGroupNum = nextAutoGroupNum--;
                                                    isAutoGroup = true;
                                                }
                                                else if (!int.TryParse(beforeStar, out hsGroupNum) || hsGroupNum < 0)
                                                {
                                                    getTextPosition(i, out var Xcount, out var Ycount);
                                                    throw new InvalidSimaiSyntaxException(Ycount, Xcount, hsContent.ToString(), "HS group number must be a non-negative integer");
                                                }
                                                hasGroup = true;
                                            }

                                            if (!TryParseHSpeedValue(bpm,
                                                                     afterStar,
                                                                     hSpeedSegments,
                                                                     out hsValue,
                                                                     out hasInterpolation,
                                                                     out var parseError,
                                                                     out var invalidEasing))
                                            {
                                                getTextPosition(i, out var Xcount, out var Ycount);
                                                if (parseError == HSpeedValueParseError.InvalidHSpeed)
                                                {
                                                    throw new InvalidSimaiMarkupException(Ycount, Xcount, hsContent.ToString(), "HSpeed value must be a number");
                                                }
                                                if (parseError == HSpeedValueParseError.UnknownEasing)
                                                {
                                                    throw new InvalidSimaiSyntaxException(Ycount, Xcount, hsContent.ToString(), $"Unknown HSpeed easing \"{invalidEasing}\"");
                                                }
                                                throw new InvalidSimaiSyntaxException(Ycount, Xcount, hsContent.ToString(), "Unexpected HS declaration syntax");
                                            }
                                            hasHSpeedChange = true;

                                            if (hasGroup)
                                            {
                                                hsGroupSpeeds[hsGroupNum] = hsValue;
                                            }
                                            else
                                            {
                                                // <HS*x> - 旧语法，设置默认组速度
                                                curHSpeed = hsValue;
                                            }

                                            if (hasInterpolation)
                                            {
                                                getTextPosition(i, out var Xcount, out var Ycount);
                                                addHSpeedInterpolation(hsGroupNum, hSpeedSegments, Xcount, Ycount, i);
                                            }
                                            else
                                            {
                                                addHSpeedEvent(time, hsGroupNum, hsValue, commandOrder);
                                            }
                                        }
                                        else
                                        {
                                            // No '*': must be <HSg> format (group only, no speed change)
                                            if (hsBody.Length == 1 && hsBody[0] == '?')
                                            {
                                                getTextPosition(i, out var Xcount, out var Ycount);
                                                throw new InvalidSimaiSyntaxException(Ycount, Xcount, hsContent.ToString(), "Undefined HS group requires a speed value (use <HS?*speed>)");
                                            }
                                            if (!int.TryParse(hsBody, out hsGroupNum) || hsGroupNum <= 0)
                                            {
                                                getTextPosition(i, out var Xcount, out var Ycount);
                                                throw new InvalidSimaiSyntaxException(Ycount, Xcount, hsContent.ToString(), "Unexpected HS declaration syntax");
                                            }
                                            hasGroup = true;
                                        }

                                        // Look ahead for '`' after > immediatly
                                        var nextIdx = i + 1;
                                        while (nextIdx < fumen.Length && (fumen[nextIdx] == ' ' || fumen[nextIdx] == '\n'))
                                        {
                                            nextIdx++;
                                        }
                                        if (nextIdx < fumen.Length && fumen[nextIdx] == '`')
                                        {
                                            i = nextIdx; // skip to '`'
                                        }

                                        // Check for group parentheses: <HSg*x>(...) or <HSg>(...)
                                        if (hasGroup)
                                        {
                                            // Look ahead for '('
                                            nextIdx = i + 1;
                                            while (nextIdx < fumen.Length && (fumen[nextIdx] == ' ' || fumen[nextIdx] == '\n'))
                                            {
                                                nextIdx++;
                                            }
                                            if (nextIdx < fumen.Length && fumen[nextIdx] == '(')
                                            {
                                                insideHsGroup = true;
                                                currentSoflanGroup = hsGroupNum;
                                                i = nextIdx; // skip to '('
                                            }
                                            else
                                            {
                                                if (isAutoGroup)
                                                {
                                                    getTextPosition(i, out var autoGroupXcount, out var autoGroupYcount);
                                                    throw new InvalidSimaiSyntaxException(autoGroupYcount, autoGroupXcount, hsContent.ToString(), "Undefined HS group requires a scope (...) after the declaration");
                                                }
                                                //没有括号,说明只是单纯的变速声明
                                                var groupHSpeed = hsGroupSpeeds.TryGetValue(hsGroupNum, out var ghs) ? ghs : 1f;
                                                getTextPosition(i, out var Xcount, out var Ycount);
                                                if (hasInterpolation)
                                                {
                                                    // 插值命令已经生成了包含 nowTime 在内的空 TimingPoint。
                                                }
                                                else if (hasHSpeedChange)
                                                {
                                                    ReadOnlySpan<char> noteContent = string.Empty;
                                                    addRawTiming(time, noteContent, Xcount, Ycount, bpm, groupHSpeed, i, hsGroupNum, commandOrder);
                                                }
                                                else
                                                {
                                                    addHSpeedRawTiming(time, Xcount, Ycount, bpm, groupHSpeed, i, hsGroupNum, commandOrder);
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        ArrayPool<char>.Shared.Return(buffer);
                                    }
                                }
                                else
                                {
                                    var s = fumen[i].ToString();
                                    getTextPosition(i, out var Xcount, out var Ycount);
                                    throw new InvalidSimaiMarkupException(Ycount, Xcount, s, $"Unexpected character \"{s}\"");
                                }
                            }
                            continue;
                        case ')' when insideHsGroup:
                            {
                                // Exit HS group mode
                                if (haveNote)
                                {
                                    var noteContent = (ReadOnlySpan<char>)(noteContentBuffer.AsSpan(0, noteContentBufIndex));
                                    var groupHSpeed = hsGroupSpeeds.TryGetValue(currentSoflanGroup, out var ghs) ? ghs : 1f;
                                    getTextPosition(i, out var Xcount, out var Ycount);
                                    addRawTiming(time, noteContent, Xcount, Ycount, bpm, groupHSpeed, i, currentSoflanGroup, nextTimingOrder());
                                    haveNote = false;
                                    noteContentBufIndex = 0;
                                }
                                insideHsGroup = false;
                                currentSoflanGroup = 0;
                            }
                            continue;
                    }

                    if (!haveNote && IsNote(fumen[i]))
                    {
                        haveNote = true;
                        noteContentBufIndex = 0;
                    }
                    else if (!haveNote && fumen[i] == '@')
                    {
                        getTextPosition(i, out var Xcount, out var Ycount);
                        throw new InvalidSimaiSyntaxException(Ycount, Xcount, fumen[i].ToString(), "FixedSoflan modifier must be placed at the end of a note");
                    }

                    if (fumen[i] == ',')
                    {
                        if (haveNote)
                        {
                            var noteContent = (ReadOnlySpan<char>)(noteContentBuffer.AsSpan(0, noteContentBufIndex));
                            var fakeEachTagCount = noteContent.Count('`');

                            if (fakeEachTagCount != 0)
                            {
                                var rentedBufferForRanges = ArrayPool<Range>.Shared.Rent(fakeEachTagCount + 1);
                                var ranges = rentedBufferForRanges.AsSpan(fakeEachTagCount + 1);
                                try
                                {
                                    // 伪双
                                    var tagCount = noteContent.Split(ranges, '`', StringSplitOptions.RemoveEmptyEntries);
                                    var fakeTime = time;
                                    var timeInterval = 1.875 / bpm; // 128分音

                                    var fakeHSpeed = currentSoflanGroup != 0
                                        ? (hsGroupSpeeds.TryGetValue(currentSoflanGroup, out var fghs) ? fghs : 1f)
                                        : curHSpeed;
                                    for (var j = 0; j < tagCount; j++)
                                    {
                                        var fakeEachGroup = noteContent[ranges[j]];
                                        //Console.WriteLine(fakeEachGroup.ToString());
                                        getTextPosition(i, out var Xcount, out var Ycount);
                                        addRawTiming(fakeTime, fakeEachGroup, Xcount, Ycount, bpm, fakeHSpeed, i, currentSoflanGroup, nextTimingOrder());
                                        fakeTime += timeInterval;
                                    }
                                }
                                finally
                                {
                                    ArrayPool<Range>.Shared.Return(rentedBufferForRanges);
                                }
                            }
                            else
                            {
                                var noteHSpeed = currentSoflanGroup != 0
                                    ? (hsGroupSpeeds.TryGetValue(currentSoflanGroup, out var nghs) ? nghs : 1f)
                                    : curHSpeed;
                                getTextPosition(i, out var Xcount, out var Ycount);
                                addRawTiming(time, noteContent, Xcount, Ycount, bpm, noteHSpeed, i, currentSoflanGroup, nextTimingOrder());
                            }
                            //Console.WriteLine("Note:" + noteTemp);

                            //noteTemp = "";
                            noteContentBufIndex = 0;
                        }
                        {
                            BufferHelper.EnsureBufferLength(commaTimingBufIndex + 1, ref commaTimingBuffer);
                            getTextPosition(i, out var Xcount, out var Ycount);
                            commaTimingBuffer[commaTimingBufIndex++] = new SimaiTimingPoint(time, null, string.Empty, Xcount, Ycount, bpm, 1, i, signatureNumerator, signatureDenominator);
                        }
                        time += 1d / (bpm / 60d) * 4d / beats;
                        //Console.WriteLine(time);
                        haveNote = false;
                        noteContentBufIndex = 0;
                    }
                    else if (haveNote)
                    {
                        ref readonly var curChar = ref fumen[i];
                        BufferHelper.EnsureBufferLength(noteContentBufIndex + 1, ref noteContentBuffer);
                        noteContentBuffer[noteContentBufIndex++] = curChar;
                    }
                }
                BufferHelper.EnsureBufferLength(commaTimingBufIndex + 1, ref commaTimingBuffer);
                getTextPosition(fumen.Length, out var endXcount, out var endYcount);
                commaTimingBuffer[commaTimingBufIndex++] = new SimaiTimingPoint(time, null, string.Empty, endXcount, endYcount, bpm, 1, fumen.Length, signatureNumerator, signatureDenominator);

                var finalHSpeedEvents = BuildFinalHSpeedEvents(hSpeedEvents);
                var finalRawTimingEntries = BuildFinalRawTimingEntries(rawTimingEntries);
                var noteTimingPoints = new SimaiTimingPoint[finalRawTimingEntries.Count];
                InvalidSimaiMarkupException? parseException = null;
                Parallel.For(0, finalRawTimingEntries.Count, (i, state) =>
                {
                    if (parseException != null)
                    {
                        state.Stop();
                        return;
                    }

                    var rawTiming = finalRawTimingEntries[i].RawTiming;
                    if (!string.IsNullOrEmpty(rawTiming.RawContent))
                    {
                        var finalHSpeed = GetEffectiveHSpeed(finalHSpeedEvents, rawTiming.SoflanGroup, rawTiming.Timing);
                        rawTiming = new SimaiRawTimingPoint(rawTiming.Timing,
                                                            rawTiming.RawContent.AsSpan(),
                                                            rawTiming.RawTextPositionX,
                                                            rawTiming.RawTextPositionY,
                                                            rawTiming.Bpm,
                                                            finalHSpeed,
                                                            rawTiming.RawTextPosition,
                                                            rawTiming.SoflanGroup);
                    }
                    try
                    {
                        var timingPoint = rawTiming.Parse();
                        noteTimingPoints[i] = timingPoint;
                    }
                    catch (InvalidSimaiMarkupException ex)
                    {
                        var mappedException = ex.Line == 0 && ex.Column == 0
                            ? new InvalidSimaiSyntaxException(rawTiming.RawTextPositionY,
                                                              rawTiming.RawTextPositionX,
                                                              ex.Content,
                                                              ex.Message)
                            : ex;
                        if (System.Threading.Interlocked.CompareExchange(ref parseException, mappedException, null) == null)
                        {
                            state.Stop();
                        }
                    }
                });
                if (parseException != null)
                {
                    throw parseException;
                }

                return new SimaiChart(level,
                                      designer,
                                      fumen.ToString(),
                                      noteTimingPoints.AsSpan(0, finalRawTimingEntries.Count),
                                      commaTimingBuffer.AsSpan(0, commaTimingBufIndex));
            }
            catch (InvalidSimaiMarkupException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new Exception("Error " + e.Message);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(noteContentBuffer);
                ArrayPool<SimaiTimingPoint>.Shared.Return(commaTimingBuffer);
            }
        }
        /// <summary>
        /// Read simai chart from <paramref name="fumen"/> and parse it into <seealso cref="SimaiChart"/>
        /// </summary>
        /// <param name="fumen">Simai chart</param>
        /// <returns></returns>
        public static Task<SimaiChart> ParseChartAsync(string fumen)
        {
            return Task.Run(() => ParseChart(string.Empty, string.Empty, fumen, default, out _));
        }
        /// <summary>
        /// Read simai chart from <paramref name="fumen"/> and parse it into <seealso cref="SimaiChart"/>
        /// </summary>
        /// <param name="fumen">Simai chart</param>
        /// <returns></returns>
        public static Task<SimaiChart> ParseChartAsync(ReadOnlyMemory<char> fumen)
        {
            return Task.Run(() => ParseChart(string.Empty, string.Empty, fumen.Span, default, out _));
        }
        /// <summary>
        /// Read simai chart from <paramref name="fumen"/> and parse it into <seealso cref="SimaiChart"/>
        /// </summary>
        /// <param name="level">Level of simai chart</param>
        /// <param name="designer">designer of simai chart</param>
        /// <param name="fumen">Simai chart</param>
        /// <returns></returns>
        public static Task<SimaiChart> ParseChartAsync(string level, string designer, ReadOnlyMemory<char> fumen)
        {
            return Task.Run(() => ParseChart(level, designer, fumen.Span, default, out _));
        }
        /// <summary>
        /// Read simai chart from <paramref name="fumen"/> and parse it into <seealso cref="SimaiChart"/>
        /// </summary>
        /// <param name="level">Level of simai chart</param>
        /// <param name="designer">designer of simai chart</param>
        /// <param name="fumen">Simai chart</param>
        /// <returns></returns>
        public static Task<SimaiChart> ParseChartAsync(string level, string designer, string fumen)
        {
            return Task.Run(() => ParseChart(level, designer, fumen, default, out _));
        }
        #endregion
        #region Deparse
        //Note: this method only deparse RawChart
        public static string Deparse(SimaiFile simaiFile)
        {
            var sb = new StringBuilder();
            var finalDesigner = string.Empty;

            sb.Append($"&title=")
              .AppendLine(simaiFile.Title)
              .Append($"&artist=")
              .AppendLine(simaiFile.Artist)
              .Append("&first=")
              .Append(simaiFile.Offset)
              .AppendLine();
            for (int i = 0; i < 7; i++)
            {
                var chart = simaiFile.Charts[i];
                if (chart is null)
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(chart.Designer))
                {
                    finalDesigner = chart.Designer;
                    sb.Append("&des_")
                      .Append(i + 1)
                      .Append('=')
                      .AppendLine(chart.Designer);
                }
                if (!string.IsNullOrEmpty(chart.Level))
                {
                    sb.Append("&lv_")
                      .Append(i + 1)
                      .Append('=')
                      .AppendLine(chart.Level);
                }
            }
            sb.Append("&des")
              .Append('=')
              .AppendLine(finalDesigner);
            foreach (var command in simaiFile.Commands)
            {
                sb.Append('&')
                  .Append(command.Prefix)
                  .Append('=')
                  .AppendLine(command.Value);
            }
            for (int i = 0; i < 7; i++)
            {
                var chart = simaiFile.Charts[i].Fumen;
                if (string.IsNullOrEmpty(chart))
                {
                    continue;
                }
                sb.Append("&inote_")
                  .Append(i + 1)
                  .Append('=')
                  .Append(chart)
                  .AppendLine();
                //if (!chart.EndsWith('E'))
                //{
                //    sb.Append('E')
                //      .AppendLine();
                //}
            }
            return sb.ToString();
        }
        public static void Deparse(SimaiFile simaiFile, Stream stream)
        {
            Deparse(simaiFile, stream, Encoding.UTF8);
        }
        public static void Deparse(SimaiFile simaiFile, Stream stream, Encoding encoding)
        {
            var fumen = Deparse(simaiFile);
            using var writer = new StreamWriter(stream, encoding);
            writer.Write(fumen);
        }
        public static Task<string> DeparseAsync(SimaiFile simaiFile)
        {
            return Task.Run(() => Deparse(simaiFile));
        }
        public static async Task DeparseAsync(SimaiFile simaiFile, Stream stream)
        {
            await DeparseAsync(simaiFile, stream, Encoding.UTF8);
        }
        public static async Task DeparseAsync(SimaiFile simaiFile, Stream stream, Encoding encoding)
        {
            var fumen = await DeparseAsync(simaiFile);
            using var writer = new StreamWriter(stream, encoding);
            await writer.WriteAsync(fumen);
        }
        #endregion
        const double TimeEpsilon = 1e-9;

        static bool IsSameTime(double left, double right)
        {
            return Math.Abs(left - right) <= TimeEpsilon;
        }

        static bool IsAfterTime(double left, double right)
        {
            return left - right > TimeEpsilon;
        }

        static int CompareTime(double left, double right)
        {
            if (IsSameTime(left, right))
            {
                return 0;
            }
            return left < right ? -1 : 1;
        }

        static long GetTimeKey(double timing)
        {
            return (long)Math.Round(timing / TimeEpsilon);
        }

        static bool IsHSpeedRawTiming(SimaiRawTimingPoint rawTiming)
        {
            return string.IsNullOrEmpty(rawTiming.RawContent);
        }

        static bool IsHSpeedTagStart(ReadOnlySpan<char> fumen, int index)
        {
            if (index < 0 || index >= fumen.Length || fumen[index] != '<')
            {
                return false;
            }

            index++;
            while (index < fumen.Length && char.IsWhiteSpace(fumen[index]))
            {
                index++;
            }

            return index + 1 < fumen.Length && fumen[index] == 'H' && fumen[index + 1] == 'S';
        }

        static void AddUniqueTime(List<double> timings, double timing)
        {
            for (var i = 0; i < timings.Count; i++)
            {
                if (IsSameTime(timings[i], timing))
                {
                    return;
                }
            }
            timings.Add(timing);
        }

        static bool TryGetHsDuration(double bpm, ReadOnlySpan<char> durationBody, out double time)
        {
            time = default;
            if (durationBody.IsEmpty)
            {
                return false;
            }

            Span<Range> ranges = stackalloc Range[2];
            var tagCount = durationBody.Split(ranges, '#', StringSplitOptions.None);
            switch (tagCount)
            {
                case 1:
                    return TryGetTimeFromRatio(bpm, durationBody, out time);
                case 2:
                    var param1 = durationBody[ranges[0]];
                    var param2 = durationBody[ranges[1]];
                    if (param1.IsEmpty)
                    {
                        return double.TryParse(param2, out time);
                    }
                    if (param2.IsEmpty || !double.TryParse(param1, out bpm))
                    {
                        return false;
                    }
                    return TryGetTimeFromRatio(bpm, param2, out time);
                default:
                    return false;
            }
        }

        static bool HasLineBreakInsideHSpeedEasingName(ReadOnlySpan<char> body)
        {
            var segmentStart = 0;
            while (segmentStart < body.Length)
            {
                var durationEndOffset = body[segmentStart..].IndexOf(']');
                if (durationEndOffset == -1)
                {
                    return false;
                }

                var easingStart = segmentStart + durationEndOffset + 1;
                var separatorOffset = body[easingStart..].IndexOf('~');
                var segmentEnd = separatorOffset == -1 ? body.Length : easingStart + separatorOffset;
                var easingBody = body[easingStart..segmentEnd];
                var firstNameCharacter = 0;
                while (firstNameCharacter < easingBody.Length && char.IsWhiteSpace(easingBody[firstNameCharacter]))
                {
                    firstNameCharacter++;
                }

                var lastNameCharacter = easingBody.Length - 1;
                while (lastNameCharacter >= firstNameCharacter && char.IsWhiteSpace(easingBody[lastNameCharacter]))
                {
                    lastNameCharacter--;
                }

                for (var index = firstNameCharacter; index <= lastNameCharacter; index++)
                {
                    if (easingBody[index] == '\r' || easingBody[index] == '\n')
                    {
                        return true;
                    }
                }

                if (separatorOffset == -1)
                {
                    return false;
                }
                segmentStart = segmentEnd + 1;
            }

            return false;
        }

        static bool TryParseHSpeedEasing(ReadOnlySpan<char> body, out HSpeedEasing easing)
        {
            easing = HSpeedEasing.Linear;
            body = body.Trim();
            if (body.IsEmpty)
            {
                return true;
            }

            for (var index = 0; index < body.Length; index++)
            {
                if (!char.IsLetter(body[index]))
                {
                    return false;
                }
            }

            if (body.Equals("linear".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string easingName;
            if (body.StartsWith("ease".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                easingName = body.ToString();
            }
            else if (body.StartsWith("io".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                easingName = "EaseInOut" + body[2..].ToString();
            }
            else if (body.StartsWith("i".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                easingName = "EaseIn" + body[1..].ToString();
            }
            else if (body.StartsWith("o".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                easingName = "EaseOut" + body[1..].ToString();
            }
            else
            {
                return false;
            }

            return Enum.TryParse(easingName, true, out easing);
        }

        static double ApplyHSpeedEasing(HSpeedEasing easing, double progress)
        {
            const double backOvershoot = 1.70158;
            const double inOutBackOvershoot = backOvershoot * 1.525;
            const double backScale = backOvershoot + 1;
            var elasticInOutPeriod = 2 * Math.PI / 4.5;
            var elasticPeriod = 2 * Math.PI / 3;

            switch (easing)
            {
                case HSpeedEasing.Linear:
                    return progress;
                case HSpeedEasing.EaseInQuad:
                    return progress * progress;
                case HSpeedEasing.EaseOutQuad:
                    return 1 - (1 - progress) * (1 - progress);
                case HSpeedEasing.EaseInOutQuad:
                    return progress < 0.5 ? 2 * progress * progress : 1 - Math.Pow(-2 * progress + 2, 2) / 2;
                case HSpeedEasing.EaseInCubic:
                    return progress * progress * progress;
                case HSpeedEasing.EaseOutCubic:
                    return 1 - Math.Pow(1 - progress, 3);
                case HSpeedEasing.EaseInOutCubic:
                    return progress < 0.5 ? 4 * progress * progress * progress : 1 - Math.Pow(-2 * progress + 2, 3) / 2;
                case HSpeedEasing.EaseInQuart:
                    return progress * progress * progress * progress;
                case HSpeedEasing.EaseOutQuart:
                    return 1 - Math.Pow(1 - progress, 4);
                case HSpeedEasing.EaseInOutQuart:
                    return progress < 0.5 ? 8 * Math.Pow(progress, 4) : 1 - Math.Pow(-2 * progress + 2, 4) / 2;
                case HSpeedEasing.EaseInQuint:
                    return Math.Pow(progress, 5);
                case HSpeedEasing.EaseOutQuint:
                    return 1 - Math.Pow(1 - progress, 5);
                case HSpeedEasing.EaseInOutQuint:
                    return progress < 0.5 ? 16 * Math.Pow(progress, 5) : 1 - Math.Pow(-2 * progress + 2, 5) / 2;
                case HSpeedEasing.EaseInSine:
                    return 1 - Math.Cos(progress * Math.PI / 2);
                case HSpeedEasing.EaseOutSine:
                    return Math.Sin(progress * Math.PI / 2);
                case HSpeedEasing.EaseInOutSine:
                    return -(Math.Cos(Math.PI * progress) - 1) / 2;
                case HSpeedEasing.EaseInExpo:
                    return progress == 0 ? 0 : Math.Pow(2, 10 * progress - 10);
                case HSpeedEasing.EaseOutExpo:
                    return progress == 1 ? 1 : 1 - Math.Pow(2, -10 * progress);
                case HSpeedEasing.EaseInOutExpo:
                    if (progress == 0 || progress == 1)
                    {
                        return progress;
                    }
                    return progress < 0.5 ? Math.Pow(2, 20 * progress - 10) / 2 : (2 - Math.Pow(2, -20 * progress + 10)) / 2;
                case HSpeedEasing.EaseInCirc:
                    return 1 - Math.Sqrt(1 - progress * progress);
                case HSpeedEasing.EaseOutCirc:
                    return Math.Sqrt(1 - Math.Pow(progress - 1, 2));
                case HSpeedEasing.EaseInOutCirc:
                    return progress < 0.5
                        ? (1 - Math.Sqrt(1 - Math.Pow(2 * progress, 2))) / 2
                        : (Math.Sqrt(1 - Math.Pow(-2 * progress + 2, 2)) + 1) / 2;
                case HSpeedEasing.EaseInBack:
                    return backScale * progress * progress * progress - backOvershoot * progress * progress;
                case HSpeedEasing.EaseOutBack:
                    return 1 + backScale * Math.Pow(progress - 1, 3) + backOvershoot * Math.Pow(progress - 1, 2);
                case HSpeedEasing.EaseInOutBack:
                    return progress < 0.5
                        ? Math.Pow(2 * progress, 2) * ((inOutBackOvershoot + 1) * 2 * progress - inOutBackOvershoot) / 2
                        : (Math.Pow(2 * progress - 2, 2) * ((inOutBackOvershoot + 1) * (progress * 2 - 2) + inOutBackOvershoot) + 2) / 2;
                case HSpeedEasing.EaseInElastic:
                    if (progress == 0 || progress == 1)
                    {
                        return progress;
                    }
                    return -Math.Pow(2, 10 * progress - 10) * Math.Sin((progress * 10 - 10.75) * elasticPeriod);
                case HSpeedEasing.EaseOutElastic:
                    if (progress == 0 || progress == 1)
                    {
                        return progress;
                    }
                    return Math.Pow(2, -10 * progress) * Math.Sin((progress * 10 - 0.75) * elasticPeriod) + 1;
                case HSpeedEasing.EaseInOutElastic:
                    if (progress == 0 || progress == 1)
                    {
                        return progress;
                    }
                    return progress < 0.5
                        ? -(Math.Pow(2, 20 * progress - 10) * Math.Sin((20 * progress - 11.125) * elasticInOutPeriod)) / 2
                        : Math.Pow(2, -20 * progress + 10) * Math.Sin((20 * progress - 11.125) * elasticInOutPeriod) / 2 + 1;
                case HSpeedEasing.EaseInBounce:
                    return 1 - ApplyHSpeedBounceOut(1 - progress);
                case HSpeedEasing.EaseOutBounce:
                    return ApplyHSpeedBounceOut(progress);
                case HSpeedEasing.EaseInOutBounce:
                    return progress < 0.5
                        ? (1 - ApplyHSpeedBounceOut(1 - 2 * progress)) / 2
                        : (1 + ApplyHSpeedBounceOut(2 * progress - 1)) / 2;
                default:
                    return progress;
            }
        }

        static double ApplyHSpeedBounceOut(double progress)
        {
            const double bounceScale = 7.5625;
            const double bounceDivisor = 2.75;
            if (progress < 1 / bounceDivisor)
            {
                return bounceScale * progress * progress;
            }
            if (progress < 2 / bounceDivisor)
            {
                progress -= 1.5 / bounceDivisor;
                return bounceScale * progress * progress + 0.75;
            }
            if (progress < 2.5 / bounceDivisor)
            {
                progress -= 2.25 / bounceDivisor;
                return bounceScale * progress * progress + 0.9375;
            }

            progress -= 2.625 / bounceDivisor;
            return bounceScale * progress * progress + 0.984375;
        }

        static bool TryParseHSpeedValue(double bpm,
                                        ReadOnlySpan<char> body,
                                        List<HSpeedSegment> segments,
                                        out float hspeed,
                                        out bool hasInterpolation,
                                        out HSpeedValueParseError parseError,
                                        out string invalidEasing)
        {
            hspeed = default;
            hasInterpolation = false;
            parseError = HSpeedValueParseError.None;
            invalidEasing = string.Empty;
            segments.Clear();
            body = body.Trim();
            if (body.IsEmpty)
            {
                parseError = HSpeedValueParseError.Syntax;
                return false;
            }

            if (body.IndexOf('~') == -1)
            {
                if (TryParseHSpeedSegment(bpm, body, out var segment, out var segmentError, out invalidEasing))
                {
                    segments.Add(segment);
                    hspeed = segment.TargetHSpeed;
                    hasInterpolation = true;
                    return true;
                }

                if (body.IndexOf('[') == -1 && body.IndexOf(']') == -1)
                {
                    if (float.TryParse(body, out hspeed))
                    {
                        return true;
                    }
                    parseError = HSpeedValueParseError.InvalidHSpeed;
                    return false;
                }

                parseError = segmentError;
                return false;
            }

            if (body[0] == '~' || body[body.Length - 1] == '~')
            {
                parseError = HSpeedValueParseError.Syntax;
                return false;
            }

            var segmentStart = 0;
            while (segmentStart < body.Length)
            {
                var separatorIndex = body[segmentStart..].IndexOf('~');
                ReadOnlySpan<char> segmentBody;
                if (separatorIndex == -1)
                {
                    segmentBody = body[segmentStart..];
                    segmentStart = body.Length + 1;
                }
                else
                {
                    segmentBody = body.Slice(segmentStart, separatorIndex);
                    segmentStart += separatorIndex + 1;
                }

                if (!TryParseHSpeedSegment(bpm, segmentBody, out var segment, out var segmentError, out invalidEasing))
                {
                    segments.Clear();
                    parseError = segmentError;
                    return false;
                }
                segments.Add(segment);
            }

            if (segments.Count == 0)
            {
                parseError = HSpeedValueParseError.Syntax;
                return false;
            }

            hspeed = segments[segments.Count - 1].TargetHSpeed;
            hasInterpolation = true;
            return true;
        }

        static bool TryParseHSpeedSegment(double bpm,
                                          ReadOnlySpan<char> body,
                                          out HSpeedSegment segment,
                                          out HSpeedValueParseError parseError,
                                          out string invalidEasing)
        {
            segment = default;
            parseError = HSpeedValueParseError.None;
            invalidEasing = string.Empty;
            body = body.Trim();
            if (body.IsEmpty)
            {
                parseError = HSpeedValueParseError.Syntax;
                return false;
            }

            var durationStart = body.IndexOf('[');
            var durationEnd = body.IndexOf(']');
            if (durationStart <= 0 ||
                durationEnd == -1 ||
                body[(durationEnd + 1)..].IndexOf(']') != -1)
            {
                parseError = HSpeedValueParseError.Syntax;
                return false;
            }

            var hSpeedValueBody = body[..durationStart].Trim();
            var durationBody = body[(durationStart + 1)..durationEnd].Trim();
            if (hSpeedValueBody.IsEmpty ||
                !TryGetHsDuration(bpm, durationBody, out var duration) ||
                duration <= 0)
            {
                parseError = HSpeedValueParseError.Syntax;
                return false;
            }

            if (!float.TryParse(hSpeedValueBody, out var hspeed))
            {
                parseError = HSpeedValueParseError.InvalidHSpeed;
                return false;
            }

            var easingBody = body[(durationEnd + 1)..].Trim();
            var easing = HSpeedEasing.Linear;
            if (!easingBody.IsEmpty && !TryParseHSpeedEasing(easingBody, out easing))
            {
                invalidEasing = easingBody.ToString();
                parseError = HSpeedValueParseError.UnknownEasing;
                return false;
            }

            segment = new HSpeedSegment(hspeed, duration, easing);
            return true;
        }

        static bool TryGetTimeFromRatio(double bpm, ReadOnlySpan<char> ratioBody, out double time)
        {
            time = default;
            if (bpm <= 0)
            {
                return false;
            }

            Span<Range> ranges = stackalloc Range[2];
            var tagCount = ratioBody.Split(ranges, ':', StringSplitOptions.None);
            if (tagCount != 2)
            {
                return false;
            }

            var divideStr = ratioBody[ranges[0]];
            var countStr = ratioBody[ranges[1]];
            if (divideStr.IsEmpty || countStr.IsEmpty)
            {
                return false;
            }
            if (!int.TryParse(divideStr, out var divide) || !int.TryParse(countStr, out var count))
            {
                return false;
            }
            if (divide <= 0 || count < 0)
            {
                return false;
            }

            var timeOneBeat = 1d / (bpm / 60d);
            time = timeOneBeat * 4d / divide * count;
            return true;
        }

        static float GetEffectiveHSpeed(List<HSpeedEvent> hSpeedEvents, int soflanGroup, double timing)
        {
            var hspeed = 1f;
            var bestTimeKey = long.MinValue;
            var bestOrder = int.MinValue;
            var timingKey = GetTimeKey(timing);

            for (var i = 0; i < hSpeedEvents.Count; i++)
            {
                var hs = hSpeedEvents[i];
                if (hs.SoflanGroup != soflanGroup)
                {
                    continue;
                }

                var eventTimeKey = GetTimeKey(hs.Timing);
                if (eventTimeKey > timingKey)
                {
                    continue;
                }
                if (eventTimeKey > bestTimeKey || (eventTimeKey == bestTimeKey && hs.Order > bestOrder))
                {
                    hspeed = hs.HSpeed;
                    bestTimeKey = eventTimeKey;
                    bestOrder = hs.Order;
                }
            }

            return hspeed;
        }

        static List<HSpeedEvent> BuildFinalHSpeedEvents(List<HSpeedEvent> hSpeedEvents)
        {
            var sortedEvents = new List<HSpeedEvent>(hSpeedEvents);
            sortedEvents.Sort((left, right) =>
            {
                var leftTimeKey = GetTimeKey(left.Timing);
                var rightTimeKey = GetTimeKey(right.Timing);
                var c = leftTimeKey.CompareTo(rightTimeKey);
                if (c != 0)
                {
                    return c;
                }
                c = left.SoflanGroup.CompareTo(right.SoflanGroup);
                if (c != 0)
                {
                    return c;
                }
                return left.Order.CompareTo(right.Order);
            });

            var finalEvents = new List<HSpeedEvent>();
            var eventIndexMap = new Dictionary<(long TimeKey, int SoflanGroup), int>();
            for (var i = 0; i < sortedEvents.Count; i++)
            {
                var hs = sortedEvents[i];
                var key = (GetTimeKey(hs.Timing), hs.SoflanGroup);
                if (eventIndexMap.TryGetValue(key, out var index))
                {
                    finalEvents[index] = hs;
                }
                else
                {
                    eventIndexMap[key] = finalEvents.Count;
                    finalEvents.Add(hs);
                }
            }

            finalEvents.Sort((left, right) =>
            {
                var leftTimeKey = GetTimeKey(left.Timing);
                var rightTimeKey = GetTimeKey(right.Timing);
                var c = leftTimeKey.CompareTo(rightTimeKey);
                if (c != 0)
                {
                    return c;
                }
                return left.Order.CompareTo(right.Order);
            });
            return finalEvents;
        }

        static List<RawTimingEntry> BuildFinalRawTimingEntries(List<RawTimingEntry> rawTimingEntries)
        {
            var sortedEntries = new List<RawTimingEntry>(rawTimingEntries);
            sortedEntries.Sort((left, right) =>
            {
                var leftTimeKey = GetTimeKey(left.RawTiming.Timing);
                var rightTimeKey = GetTimeKey(right.RawTiming.Timing);
                var c = leftTimeKey.CompareTo(rightTimeKey);
                if (c != 0)
                {
                    return c;
                }

                var leftIsHSpeed = IsHSpeedRawTiming(left.RawTiming);
                var rightIsHSpeed = IsHSpeedRawTiming(right.RawTiming);
                if (leftIsHSpeed != rightIsHSpeed)
                {
                    return leftIsHSpeed ? -1 : 1;
                }

                return left.Order.CompareTo(right.Order);
            });

            var finalEntries = new List<RawTimingEntry>();
            var hSpeedIndexMap = new Dictionary<(long TimeKey, int SoflanGroup), int>();
            for (var i = 0; i < sortedEntries.Count; i++)
            {
                var entry = sortedEntries[i];
                if (IsHSpeedRawTiming(entry.RawTiming))
                {
                    var key = (GetTimeKey(entry.RawTiming.Timing), entry.RawTiming.SoflanGroup);
                    if (hSpeedIndexMap.TryGetValue(key, out var index))
                    {
                        finalEntries[index] = entry;
                        continue;
                    }

                    hSpeedIndexMap[key] = finalEntries.Count;
                }

                finalEntries.Add(entry);
            }

            return finalEntries;
        }

        readonly struct RawTimingEntry
        {
            public SimaiRawTimingPoint RawTiming { get; }
            public int Order { get; }

            public RawTimingEntry(SimaiRawTimingPoint rawTiming, int order)
            {
                RawTiming = rawTiming;
                Order = order;
            }
        }

        readonly struct HSpeedEvent
        {
            public double Timing { get; }
            public int SoflanGroup { get; }
            public float HSpeed { get; }
            public int Order { get; }

            public HSpeedEvent(double timing, int soflanGroup, float hspeed, int order)
            {
                Timing = timing;
                SoflanGroup = soflanGroup;
                HSpeed = hspeed;
                Order = order;
            }
        }

        enum HSpeedValueParseError
        {
            None,
            Syntax,
            InvalidHSpeed,
            UnknownEasing
        }

        enum HSpeedEasing
        {
            Linear,
            EaseInQuad,
            EaseOutQuad,
            EaseInOutQuad,
            EaseInCubic,
            EaseOutCubic,
            EaseInOutCubic,
            EaseInQuart,
            EaseOutQuart,
            EaseInOutQuart,
            EaseInQuint,
            EaseOutQuint,
            EaseInOutQuint,
            EaseInSine,
            EaseOutSine,
            EaseInOutSine,
            EaseInExpo,
            EaseOutExpo,
            EaseInOutExpo,
            EaseInCirc,
            EaseOutCirc,
            EaseInOutCirc,
            EaseInBack,
            EaseOutBack,
            EaseInOutBack,
            EaseInElastic,
            EaseOutElastic,
            EaseInOutElastic,
            EaseInBounce,
            EaseOutBounce,
            EaseInOutBounce
        }

        readonly struct HSpeedSegment
        {
            public float TargetHSpeed { get; }
            public double Duration { get; }
            public HSpeedEasing Easing { get; }

            public HSpeedSegment(float targetHSpeed, double duration, HSpeedEasing easing)
            {
                TargetHSpeed = targetHSpeed;
                Duration = duration;
                Easing = easing;
            }
        }

        static class MD5Helper
        {
            public static byte[] ComputeHash(byte[] data)
            {
                using (var md5 = MD5.Create())
                {
                    return md5.ComputeHash(data);
                }
            }
            public static byte[] ComputeHash(byte[] data, int offset, int count)
            {
                using (var md5 = MD5.Create())
                {
                    return md5.ComputeHash(data, offset, count);
                }
            }
            public static string ComputeHashAsBase64String(byte[] data)
            {
                var hash = ComputeHash(data);

                return Convert.ToBase64String(hash);
            }
            public static string ComputeHashAsBase64String(byte[] data, int offset, int count)
            {
                var hash = ComputeHash(data, offset, count);

                return Convert.ToBase64String(hash);
            }
            public static async Task<byte[]> ComputeHashAsync(byte[] data)
            {
                using (var md5 = MD5.Create())
                {
                    return await Task.Run(() => md5.ComputeHash(data));
                }
            }
            public static async Task<byte[]> ComputeHashAsync(byte[] data, int offset, int count)
            {
                using (var md5 = MD5.Create())
                {
                    return await Task.Run(() => md5.ComputeHash(data, offset, count));
                }
            }
            public static async Task<string> ComputeHashAsBase64StringAsync(byte[] data)
            {
                var hash = await ComputeHashAsync(data);

                return Convert.ToBase64String(hash);
            }
            public static async Task<string> ComputeHashAsBase64StringAsync(byte[] data, int offset, int count)
            {
                var hash = await ComputeHashAsync(data, offset, count);

                return Convert.ToBase64String(hash);
            }
        }
        static (ArraySegment<char> Content, string Hash) DecodeAndHash(Stream contentStream, Encoding encoding)
        {
            var contentLen = (int)contentStream.Length;
            var buffer = ArrayPool<byte>.Shared.Rent(contentLen);
            var content = ArrayPool<char>.Shared.Rent(1024);
            try
            {
                using var memoryStream = new MemoryStream(buffer, 0, contentLen);
                contentStream.CopyTo(memoryStream);
                memoryStream.Position = 0;
                var read = 0;
                var hash = MD5Helper.ComputeHashAsBase64String(buffer, 0, contentLen);
                var charBuffer = (stackalloc char[4096]);
                using (var decodeStream = new StreamReader(memoryStream, encoding, true))
                {
                    while (!decodeStream.EndOfStream)
                    {
                        var currentRead = decodeStream.Read(charBuffer);
                        if (currentRead == 0)
                        {
                            continue;
                        }
                        BufferHelper.EnsureBufferLength(read + currentRead, ref content);
                        charBuffer.Slice(0, currentRead).CopyTo(content.AsSpan(read));
                        read += currentRead;
                    }
                }
                return (new ArraySegment<char>(content, 0, read), hash);
            }
            catch
            {
                ArrayPool<char>.Shared.Return(content);
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        static async Task<(ArraySegment<char> Content, string Hash)> DecodeAndHashAsync(Stream contentStream, Encoding encoding)
        {
            var contentLen = (int)contentStream.Length;
            var buffer = ArrayPool<byte>.Shared.Rent(contentLen);
            var charBuffer = ArrayPool<char>.Shared.Rent(4096);
            var content = ArrayPool<char>.Shared.Rent(1024);
            try
            {
                using var memoryStream = new MemoryStream(buffer, 0, contentLen);
                await contentStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                var read = 0;
                var hash = await MD5Helper.ComputeHashAsBase64StringAsync(buffer, 0, contentLen);
                using (var decodeStream = new StreamReader(memoryStream, encoding, true))
                {
                    while (!decodeStream.EndOfStream)
                    {
                        var currentRead = await decodeStream.ReadAsync(charBuffer);
                        if (currentRead == 0)
                        {
                            continue;
                        }
                        BufferHelper.EnsureBufferLength(read + currentRead, ref content);
                        charBuffer.AsSpan(0, currentRead).CopyTo(content.AsSpan(read));
                        read += currentRead;
                    }
                }
                return (new ArraySegment<char>(content, 0, read), hash);
            }
            catch
            {
                ArrayPool<char>.Shared.Return(content);
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                ArrayPool<char>.Shared.Return(charBuffer);
            }
        }
    }
}
