using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace MajSimai
{
    public class SimaiTimingPoint
    {
        public double Timing { get; set; } = 0;
        public float Bpm { get; set; } = -1;
        public float HSpeed { get; set; } = 1f;
        public string RawContent { get; } = string.Empty;
        public int RawTextPositionX { get; }
        public int RawTextPositionY { get; }
        public int RawTextPosition { get; }
        public int SignatureNumerator { get; } = 4;
        public int SignatureDenominator { get; } = 4;
        public SimaiNote[] Notes { get; set; } = Array.Empty<SimaiNote>();
        public bool IsEmpty => Notes.Length == 0;
        public int SoflanGroup { get; } = 0;

        public SimaiTimingPoint(double timing, SimaiNote[]? notes, string content, int textPosX = 0, int textPosY = 0, float bpm = 0f,
            float hspeed = 1f, int rawTextPosition = 0, int signatureNumerator = 0, int signatureDenominator = 0, int soflanGroup = 0)
            : this(timing,
                   notes,
                   content,
                   textPosX,
                   textPosY,
                   bpm,
                   hspeed,
                   rawTextPosition,
                   signatureNumerator,
                   signatureDenominator,
                   soflanGroup,
                   false)
        {
        }

        private SimaiTimingPoint(double timing, SimaiNote[]? notes, string? content, int textPosX, int textPosY, float bpm,
            float hspeed, int rawTextPosition, int signatureNumerator, int signatureDenominator, int soflanGroup,
            bool contentIsNormalized)
        {
            Timing = timing;
            RawTextPositionX = textPosX;
            RawTextPositionY = textPosY;
            RawTextPosition = rawTextPosition;
            SoflanGroup = soflanGroup;
            RawContent = contentIsNormalized ? content ?? string.Empty : NormalizeRawContent(content);
            Bpm = bpm;
            HSpeed = hspeed;
            if (notes != null)
            {
                Notes = notes;
            }

            SignatureNumerator = signatureNumerator;
            SignatureDenominator = signatureDenominator;
        }

        internal static SimaiTimingPoint CreateFromNormalizedContent(double timing,
                                                                      SimaiNote[]? notes,
                                                                      string normalizedContent,
                                                                      int textPosX,
                                                                      int textPosY,
                                                                      float bpm,
                                                                      float hspeed,
                                                                      int rawTextPosition,
                                                                      int soflanGroup)
        {
            return new SimaiTimingPoint(timing,
                                        notes,
                                        normalizedContent,
                                        textPosX,
                                        textPosY,
                                        bpm,
                                        hspeed,
                                        rawTextPosition,
                                        0,
                                        0,
                                        soflanGroup,
                                        true);
        }

        static string NormalizeRawContent(string? content)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                var rawContent = content.AsSpan();
                Span<char> rCSpan = stackalloc char[rawContent.Length];
                rawContent.Replace(rCSpan, '\n', ' ');
                var i2 = 0;
                for (var i = 0; i < rCSpan.Length; i++)
                {
                    var current = rCSpan[i];
                    if (char.IsWhiteSpace(current))
                    {
                        continue;
                    }
                    else
                    {
                        rCSpan[i2++] = current;
                    }
                }
                var newRaw = rCSpan.Slice(0, i2);
                if (newRaw != rawContent)
                {
                    return new string(rCSpan.Slice(0, i2));
                }

                return rawContent.ToString();
            }

            return string.Empty;
        }
#if NET7_0_OR_GREATER
        internal unsafe MajSimai.Unmanaged.UnmanagedSimaiTimingPoint ToUnmanaged()
        {
            var rawContentPtr = (char*)null;
            var noteArray = (MajSimai.Unmanaged.UnmanagedSimaiNote*)null;

            if (!string.IsNullOrEmpty(RawContent))
            {
                rawContentPtr = (char*)Marshal.StringToHGlobalAnsi(RawContent);
            }
            if (Notes.Length != 0)
            {
                noteArray = (MajSimai.Unmanaged.UnmanagedSimaiNote*)Marshal.AllocHGlobal(sizeof(MajSimai.Unmanaged.UnmanagedSimaiNote) * Notes.Length);
                for (var i = 0; i < Notes.Length; i++)
                {
                    *(noteArray + i) = Notes[i].ToUnmanaged();
                }
            }
            return new()
            {
                timing = Timing,
                bpm = Bpm,
                hSpeed = HSpeed,
                rawTextPositionX = RawTextPositionX,
                rawTextPositionY = RawTextPositionY,
                rawTextPosition = RawTextPosition,
                rawContent = rawContentPtr,
                rawContentLen = RawContent.Length,
                soflanGroup = SoflanGroup,
                notes = noteArray,
                notesLen = Notes.Length
            };
        }
#endif
    }
}
