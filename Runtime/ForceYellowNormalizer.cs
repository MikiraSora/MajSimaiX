using System;
using System.Collections.Generic;
using System.Linq;

namespace MajSimai
{
    internal static class ForceYellowNormalizer
    {
        private const double TimeTolerance = 1e-9;

        public static void ClearNaturalEachHeadFlags(IEnumerable<SimaiTimingPoint> timingPoints)
        {
            var currentGroupNotes = new List<SimaiNote>();
            var hasCurrentGroup = false;
            var currentGroupTime = 0d;

            foreach (var timingPoint in timingPoints
                         .Where(timingPoint => timingPoint.Notes.Length != 0)
                         .OrderBy(timingPoint => timingPoint.Timing))
            {
                if (!hasCurrentGroup || Math.Abs(timingPoint.Timing - currentGroupTime) > TimeTolerance)
                {
                    ClearCurrentGroup();
                    currentGroupNotes.Clear();
                    currentGroupTime = timingPoint.Timing;
                    hasCurrentGroup = true;
                }

                foreach (var note in timingPoint.Notes)
                {
                    if (IsCandidate(note))
                    {
                        currentGroupNotes.Add(note);
                    }
                }
            }

            ClearCurrentGroup();

            void ClearCurrentGroup()
            {
                if (currentGroupNotes.Count <= 1)
                {
                    return;
                }

                foreach (var note in currentGroupNotes)
                {
                    note.IsForceYellow = false;
                }
            }
        }

        private static bool IsCandidate(SimaiNote note)
        {
            if (note.IsMine || note.IsMineSlide || note.IsSlideNoHead)
            {
                return false;
            }

            return note.Type == SimaiNoteType.Tap ||
                   note.Type == SimaiNoteType.Hold ||
                   note.Type == SimaiNoteType.Touch ||
                   note.Type == SimaiNoteType.TouchHold ||
                   note.Type == SimaiNoteType.Slide;
        }
    }
}
