using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#nullable enable
namespace MajSimai
{
    internal readonly struct SimaiRawTimingPoint
    {
        public double Timing { get; }
        public float Bpm { get; }
        public float HSpeed { get; }
        public int SoflanGroup { get; }
        public int SlideSoflanGroup { get; }
        public string RawContent { get; }
        public int RawTextPositionX { get; }
        public int RawTextPositionY { get; }
        public int RawTextPosition { get; }

        public SimaiRawTimingPoint(double timing, ReadOnlySpan<char> rawContent, int textPosX = 0, int textPosY = 0, float bpm = 0f,
            float hspeed = 1f, int textPos = 0, int soflanGroup = 0, int? slideSoflanGroup = null)
        {
            Timing = timing;
            RawTextPositionX = textPosX;
            RawTextPositionY = textPosY;
            RawTextPosition = textPos;
            if (!rawContent.IsEmpty)
            {
                Span<char> rCSpan = stackalloc char[rawContent.Length];
                var contentLength = 0;
                for (var i = 0; i < rawContent.Length; i++)
                {
                    var current = rawContent[i];
                    if (current == 'c')
                    {
                        continue;
                    }
                    rCSpan[contentLength++] = current == '\n' ? ' ' : current;
                }

                var normalizedContent = rCSpan.Slice(0, contentLength);
                if (normalizedContent.Contains('@') && !IsFixedSoflanModifierSpacingValid(normalizedContent))
                {
                    throw new InvalidSimaiSyntaxException(textPosY, textPosX, rawContent.ToString(), "Invalid FixedSoflan modifier");
                }

                var i2 = 0;
                for (var i = 0; i < normalizedContent.Length; i++)
                {
                    var current = normalizedContent[i];
                    if (char.IsWhiteSpace(current))
                    {
                        continue;
                    }
                    else
                    {
                        rCSpan[i2++] = current;
                    }
                }
                RawContent = new string(rCSpan.Slice(0, i2));
            }
            else
            {
                RawContent = string.Empty;
            }
            Bpm = bpm;
            HSpeed = hspeed;
            SoflanGroup = soflanGroup;
            SlideSoflanGroup = slideSoflanGroup ?? soflanGroup;
        }

        static bool IsFixedSoflanModifierSpacingValid(ReadOnlySpan<char> rawContent)
        {
            for (var i = 0; i < rawContent.Length; i++)
            {
                if (rawContent[i] != '@')
                {
                    continue;
                }

                var tokenStart = i - 1;
                while (tokenStart >= 0 && rawContent[tokenStart] != '/' && rawContent[tokenStart] != '`' && rawContent[tokenStart] != '*')
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

        static bool IsSlideMark(char c)
        {
            switch (c)
            {
                case '-':
                case '^':
                case 'v':
                case '<':
                case '>':
                case 'V':
                case 'p':
                case 'q':
                case 's':
                case 'z':
                case 'w':
                    return true;
                default:
                    return false;
            }
        }
        public SimaiTimingPoint Parse()
        {
            if (SlideSoflanGroup != SoflanGroup && (RawContent.Contains('/') || RawContent.Contains('`')))
            {
                throw new InvalidSimaiSyntaxException(RawTextPositionY, RawTextPositionX, RawContent,
                    "A head-only HS group cannot be combined with each or fake-each notes");
            }

            var notes = SimaiNoteParser.GetNotes(Timing, Bpm, RawContent, SoflanGroup, SlideSoflanGroup);

            if (SlideSoflanGroup != SoflanGroup &&
                (notes.Length == 0 || notes.Any(note => note.Type != SimaiNoteType.Slide)))
            {
                throw new InvalidSimaiSyntaxException(RawTextPositionY, RawTextPositionX, RawContent,
                    "A head-only HS group must be followed by a slide body");
            }

            return new SimaiTimingPoint(Timing, notes, RawContent, RawTextPositionX, RawTextPositionY, Bpm, HSpeed, RawTextPosition, soflanGroup: SoflanGroup);
        }
        public Task<SimaiTimingPoint> ParseAsync()
        {
            return Task.Run(Parse);
        }
    }
}
