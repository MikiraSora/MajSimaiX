using System;
using System.Collections.Generic;

#nullable enable
namespace MajSimai
{
    internal readonly struct ForceYellowParseResult
    {
        public string NoteContent { get; }
        public bool IsForceYellow { get; }
        public int[] SlideSegmentIndices { get; }

        public ForceYellowParseResult(string noteContent, bool isForceYellow, int[] slideSegmentIndices)
        {
            NoteContent = noteContent;
            IsForceYellow = isForceYellow;
            SlideSegmentIndices = slideSegmentIndices;
        }
    }

    internal static class ForceYellowModifierParser
    {
        public static ForceYellowParseResult Parse(ReadOnlySpan<char> noteText)
        {
            if (noteText.IndexOf('Y') >= 0)
            {
                throw SyntaxError(noteText, "Unexpected character \"Y\"");
            }

            var modifierCount = Count(noteText, 'y');
            if (modifierCount == 0)
            {
                return new ForceYellowParseResult(noteText.ToString(), false, Array.Empty<int>());
            }

            if (noteText.IndexOf('b') >= 0)
            {
                throw SyntaxError(noteText, "Force Yellow cannot coexist with Break");
            }
            if (noteText.IndexOf('m') >= 0)
            {
                throw SyntaxError(noteText, "Force Yellow cannot coexist with Mine");
            }

            var firstSlideMarkIndex = IndexOfFirstSlideMark(noteText);
            var headerEnd = firstSlideMarkIndex >= 0
                ? firstSlideMarkIndex
                : IndexOfOrLength(noteText, '[');
            var minimumHeaderModifierIndex = GetMinimumHeaderModifierIndex(noteText);
            var headerModifierCount = 0;

            for (var i = 0; i < headerEnd; i++)
            {
                if (noteText[i] != 'y')
                {
                    continue;
                }

                if (i < minimumHeaderModifierIndex)
                {
                    throw SyntaxError(noteText, "Invalid Force Yellow modifier position");
                }
                headerModifierCount++;
                if (headerModifierCount > 1)
                {
                    throw SyntaxError(noteText, "Duplicate Force Yellow modifier");
                }
            }

            var slideSegmentIndices = firstSlideMarkIndex >= 0
                ? ParseSlideSegmentModifiers(noteText, firstSlideMarkIndex)
                : Array.Empty<int>();

            var classifiedCount = headerModifierCount + slideSegmentIndices.Length;
            if (classifiedCount != modifierCount)
            {
                throw SyntaxError(noteText, "Invalid Force Yellow modifier position");
            }

            var cleaned = RemoveModifiers(noteText, modifierCount);
            if (cleaned.Length == 0)
            {
                throw SyntaxError(noteText, "Invalid Force Yellow modifier position");
            }

            return new ForceYellowParseResult(cleaned, headerModifierCount == 1, slideSegmentIndices);
        }

        private static int[] ParseSlideSegmentModifiers(ReadOnlySpan<char> noteText, int firstSlideMarkIndex)
        {
            var result = new List<int>();
            var cursor = firstSlideMarkIndex;
            var segmentIndex = 0;

            while (cursor < noteText.Length)
            {
                if (!IsSlideMark(noteText[cursor]))
                {
                    throw SyntaxError(noteText, "Invalid Force Yellow modifier position");
                }

                var mark = noteText[cursor++];
                if ((mark == 'p' || mark == 'q') && cursor < noteText.Length && noteText[cursor] == mark)
                {
                    cursor++;
                }

                var targetDigitCount = mark == 'V' ? 2 : 1;
                for (var i = 0; i < targetDigitCount; i++)
                {
                    if (cursor >= noteText.Length || !IsPositionDigit(noteText[cursor]))
                    {
                        throw SyntaxError(noteText, "Invalid Force Yellow modifier position");
                    }
                    cursor++;
                }

                var isForceYellow = false;
                if (cursor < noteText.Length && noteText[cursor] == 'y')
                {
                    isForceYellow = true;
                    cursor++;
                }

                while (cursor < noteText.Length && !IsSlideMark(noteText[cursor]))
                {
                    if (noteText[cursor] == '[')
                    {
                        var closeOffset = noteText.Slice(cursor + 1).IndexOf(']');
                        if (closeOffset < 0)
                        {
                            throw SyntaxError(noteText, "Invalid Force Yellow modifier position");
                        }

                        var closeIndex = cursor + 1 + closeOffset;
                        if (noteText.Slice(cursor + 1, closeIndex - cursor - 1).IndexOf('y') >= 0)
                        {
                            throw SyntaxError(noteText, "Invalid Force Yellow modifier position");
                        }
                        cursor = closeIndex + 1;

                        if (cursor < noteText.Length && noteText[cursor] == 'y')
                        {
                            if (isForceYellow)
                            {
                                throw SyntaxError(noteText, "Duplicate Force Yellow modifier");
                            }
                            isForceYellow = true;
                            cursor++;
                        }
                        continue;
                    }

                    if (noteText[cursor] == 'y')
                    {
                        if (isForceYellow)
                        {
                            throw SyntaxError(noteText, "Duplicate Force Yellow modifier");
                        }
                        throw SyntaxError(noteText, "Invalid Force Yellow modifier position");
                    }
                    cursor++;
                }

                if (isForceYellow)
                {
                    result.Add(segmentIndex);
                }
                segmentIndex++;
            }

            return result.Count == 0 ? Array.Empty<int>() : result.ToArray();
        }

        private static int GetMinimumHeaderModifierIndex(ReadOnlySpan<char> noteText)
        {
            if (noteText.IsEmpty)
            {
                return int.MaxValue;
            }

            var first = noteText[0];
            if (IsPositionDigit(first) || first == 'C')
            {
                return 1;
            }
            if (first == 'A' || first == 'B' || first == 'D' || first == 'E')
            {
                return 2;
            }
            return int.MaxValue;
        }

        private static string RemoveModifiers(ReadOnlySpan<char> noteText, int modifierCount)
        {
            var buffer = new char[noteText.Length - modifierCount];
            var writeIndex = 0;
            for (var i = 0; i < noteText.Length; i++)
            {
                if (noteText[i] == 'y')
                {
                    continue;
                }
                buffer[writeIndex++] = noteText[i];
            }
            return new string(buffer);
        }

        private static int IndexOfFirstSlideMark(ReadOnlySpan<char> noteText)
        {
            for (var i = 0; i < noteText.Length; i++)
            {
                if (IsSlideMark(noteText[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        private static int IndexOfOrLength(ReadOnlySpan<char> value, char target)
        {
            var index = value.IndexOf(target);
            return index < 0 ? value.Length : index;
        }

        private static int Count(ReadOnlySpan<char> value, char target)
        {
            var count = 0;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == target)
                {
                    count++;
                }
            }
            return count;
        }

        private static bool IsPositionDigit(char value)
        {
            return value >= '1' && value <= '8';
        }

        private static bool IsSlideMark(char value)
        {
            return value == '-' || value == '^' || value == 'v' || value == '<' || value == '>' ||
                   value == 'V' || value == 'p' || value == 'q' || value == 's' || value == 'z' || value == 'w';
        }

        private static InvalidSimaiSyntaxException SyntaxError(ReadOnlySpan<char> noteText, string message)
        {
            return new InvalidSimaiSyntaxException(0, 0, noteText.ToString(), message);
        }
    }
}
