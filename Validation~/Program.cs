using MajSimai;
using MajSimai.Unmanaged;
using System.Text.Json;

var tests = new (string Name, Action Body)[]
{
    ("ordinary baseline", OrdinaryBaseline),
    ("SV persistence and explicit restore", SvPersistence),
    ("SV-only empty timing and comma speed", SvOnlyTiming),
    ("same-slot HS/SV last declaration", SameSlotLastDeclaration),
    ("HS0 shares group zero", Hs0SharesGroupZero),
    ("nonzero HS group stays independent", NonzeroGroupIndependent),
    ("fake-each uses expanded timing", FakeEachTiming),
    ("later HS interpolation covers SV", LaterInterpolationCoversSv),
    ("SV whitespace follows instantaneous HS", SvWhitespace),
    ("HS/SV invalid forms", InvalidSvForms),
    ("SV numeric parity with instantaneous HS", NumericParity),
    ("lowercase c normalization", LowercaseCNormalization),
    ("existing HS forms remain usable", ExistingHsForms),
    ("slide head-only HS scope", SlideHeadOnlyHsScope),
    ("Force Yellow basic modifiers", ForceYellowBasicModifiers),
    ("Force Yellow slide segments", ForceYellowSlideSegments),
    ("Force Yellow natural each normalization", ForceYellowNaturalEach),
    ("Force Yellow invalid forms", InvalidForceYellowForms),
    ("Force Yellow managed JSON defaults", ForceYellowJsonDefaults)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} validation cases passed.");
return failures == 0 ? 0 : 1;

static SimaiChart Parse(string fumen)
{
    return SimaiParser.ParseChart(fumen.AsSpan(), 0, out _);
}

static SimaiTimingPoint FindNote(SimaiChart chart, string rawContent, double timing)
{
    var matches = chart.NoteTimings.ToArray()
        .Where(point => !point.IsEmpty && point.RawContent == rawContent && NearlyEqual(point.Timing, timing))
        .ToArray();
    Expect(matches.Length == 1, $"expected one note '{rawContent}' at {timing}, found {matches.Length}");
    return matches[0];
}

static SimaiTimingPoint FindEmpty(SimaiChart chart, double timing)
{
    var matches = chart.NoteTimings.ToArray()
        .Where(point => point.IsEmpty && NearlyEqual(point.Timing, timing))
        .ToArray();
    Expect(matches.Length == 1, $"expected one empty timing at {timing}, found {matches.Length}");
    return matches[0];
}

static void OrdinaryBaseline()
{
    var chart = Parse("(120){4}1,2,");
    var points = chart.NoteTimings.ToArray();
    Expect(points.Length == 2, $"ordinary note timing count was {points.Length}");
    Expect(points.All(point => !point.IsEmpty), "ordinary chart unexpectedly emitted an empty timing");
    Expect(points.All(point => NearlyEqual(point.HSpeed, 1f)), "ordinary chart changed HSpeed");
    Expect(chart.CommaTimings.ToArray().All(point => NearlyEqual(point.HSpeed, 1f)), "comma HSpeed changed");
}

static void SvPersistence()
{
    var chart = Parse("(120){4}<SV*2>1,2,<SV*1>3,");
    var first = FindNote(chart, "1", 0);
    var second = FindNote(chart, "2", 0.5);
    var third = FindNote(chart, "3", 1.0);
    Expect(NearlyEqual(first.HSpeed, 2f), "first note did not use SV 2");
    Expect(NearlyEqual(second.HSpeed, 2f), "SV did not persist to the next slot");
    Expect(NearlyEqual(third.HSpeed, 1f), "SV*1 did not restore group zero");
    Expect(NearlyEqual(FindEmpty(chart, 0).HSpeed, 2f), "SV event point has wrong speed");
    Expect(NearlyEqual(FindEmpty(chart, 1).HSpeed, 1f), "restore event point has wrong speed");
}

static void SvOnlyTiming()
{
    var chart = Parse("(120)<SV*2>,");
    var empty = FindEmpty(chart, 0);
    Expect(NearlyEqual(empty.HSpeed, 2f), "SV-only point did not preserve speed");
    Expect(empty.SoflanGroup == 0, "SV-only point was not assigned to group zero");
    Expect(chart.CommaTimings.ToArray().All(point => NearlyEqual(point.HSpeed, 1f)), "SV changed CommaTimings HSpeed");
}

static void SameSlotLastDeclaration()
{
    var hsAfterSv = FindNote(Parse("(120)<SV*2><HS*3>1,"), "1", 0);
    var svAfterHs = FindNote(Parse("(120)<HS*3><SV*2>1,"), "1", 0);
    var slashEach = Parse("(120)<SV*2>1/2<HS*3>,");
    Expect(NearlyEqual(hsAfterSv.HSpeed, 3f), "HS after SV did not win");
    Expect(NearlyEqual(svAfterHs.HSpeed, 2f), "SV after HS did not win");
    var eachNotes = slashEach.NoteTimings.ToArray().Where(point => !point.IsEmpty).ToArray();
    Expect(eachNotes.Length == 1 && eachNotes[0].Notes.Length == 2 && NearlyEqual(eachNotes[0].HSpeed, 3f), "same-slot each notes did not use the final speed");
    Expect(slashEach.NoteTimings.ToArray().Count(point => point.IsEmpty && NearlyEqual(point.Timing, 0)) == 1, "same-slot declarations emitted duplicate empty points");
}

static void Hs0SharesGroupZero()
{
    var chart = Parse("(120)<SV*2><HS0*3>1,2,");
    Expect(NearlyEqual(FindNote(chart, "1", 0).HSpeed, 3f), "HS0 did not override SV at the same time");
    Expect(NearlyEqual(FindNote(chart, "2", 0.5).HSpeed, 3f), "HS0 did not persist in group zero");
}

static void NonzeroGroupIndependent()
{
    var chart = Parse("(120)<SV*2><HS1*3>(1,),2,");
    var groupOne = FindNote(chart, "1", 0);
    var groupZero = FindNote(chart, "2", 1.0);
    Expect(groupOne.SoflanGroup == 1 && NearlyEqual(groupOne.HSpeed, 3f), "nonzero group speed was not retained");
    Expect(groupZero.SoflanGroup == 0 && NearlyEqual(groupZero.HSpeed, 2f), "nonzero HS changed group zero");

    ExpectThrows<InvalidSimaiSyntaxException>(
        "(120)<HS1*3>(<SV*2>1,)",
        "SV inside a group scope should fail");
    ExpectThrows<InvalidSimaiSyntaxException>(
        "(120)<SV*2>(1,)",
        "SV group scope should fail");
    var bpmAfterSv = FindNote(Parse("<SV*2>(120)1,"), "1", 0);
    Expect(NearlyEqual(bpmAfterSv.HSpeed, 2f), "a BPM declaration after SV was rejected");
}

static void FakeEachTiming()
{
    var sameSlot = Parse("(120){4}<SV*2>1`2,");
    var sameSlotNotes = sameSlot.NoteTimings.ToArray().Where(point => !point.IsEmpty).ToArray();
    Expect(sameSlotNotes.Length == 2 && sameSlotNotes.All(point => NearlyEqual(point.HSpeed, 2f)), "{4} fake-each did not use the current SV");

    var chart = Parse("(120){128}<SV*2>1`2,<SV*3>3,");
    var first = FindNote(chart, "1", 0);
    var secondTiming = 1.875 / 120d;
    var second = FindNote(chart, "2", secondTiming);
    var third = FindNote(chart, "3", secondTiming);
    Expect(NearlyEqual(first.HSpeed, 2f), "first fake-each item did not use SV");
    Expect(NearlyEqual(second.HSpeed, 3f), "fake-each item was not queried at its expanded timing");
    Expect(NearlyEqual(third.HSpeed, 3f), "next-slot note did not use SV");
}

static void LaterInterpolationCoversSv()
{
    var chart = Parse("(120){4}1,<SV*4>2,,<HS*3[1:1]>3,");
    var covered = FindNote(chart, "2", 0.5);
    Expect(NearlyEqual(covered.HSpeed, 2f), "later HS interpolation did not cover the SV event");
    Expect(chart.NoteTimings.ToArray().All(point => !NearlyEqual(point.HSpeed, 4f)), "covered SV speed remained in final timing points");
}

static void SvWhitespace()
{
    var spaced = FindNote(Parse("(120)<SV* +2>1,"), "1", 0);
    var lineBreak = FindNote(Parse("(120)<SV\n*2>1,"), "1", 0);
    var leadingLineBreak = FindNote(Parse("(120)<\nSV*2>1,"), "1", 0);
    Expect(NearlyEqual(spaced.HSpeed, 2f), "space after SV star was not accepted");
    Expect(NearlyEqual(lineBreak.HSpeed, 2f), "line break in SV tag was not accepted like HS");
    Expect(NearlyEqual(leadingLineBreak.HSpeed, 2f), "line break after tag opener was not accepted like HS");
}

static void InvalidSvForms()
{
    var syntaxForms = new[]
    {
        "(120)<SV*>1,",
        "(120)<SV**2>1,",
        "(120)<SV1*2>1,",
        "(120)<SV?*2>1,",
        "(120)<SV?>1,",
        "(120)<SV*2[4:1]>1,",
        "(120)<SV*2~1>1,",
        "(120)<SV*2>(1,)",
        "(120)<SV*2"
    };
    foreach (var form in syntaxForms)
    {
        ExpectThrows<InvalidSimaiSyntaxException>(form, $"expected syntax rejection for {form}");
    }

    ExpectThrows<InvalidSimaiMarkupException>("(120)<SV*abc>1,", "non-numeric SV should fail as a markup value");
    ExpectThrows<InvalidSimaiSyntaxException>("(120)<SVg*2>1,", "SV group syntax should fail");
    ExpectThrows<InvalidSimaiSyntaxException>("(120)<sv*2>1,", "lowercase sv should not be accepted");
}

static void NumericParity()
{
    var literals = new[] { "0", "-1", "+2", "1e2", "NaN", "Infinity", "-Infinity" };
    foreach (var literal in literals)
    {
        var hs = FindNote(Parse($"(120)<HS*{literal}>1,"), "1", 0).HSpeed;
        var sv = FindNote(Parse($"(120)<SV*{literal}>1,"), "1", 0).HSpeed;
        if (float.IsNaN(hs) || float.IsNaN(sv))
        {
            Expect(float.IsNaN(hs) && float.IsNaN(sv), $"NaN acceptance differs for {literal}");
        }
        else
        {
            Expect(hs.Equals(sv), $"numeric acceptance differs for {literal}: HS={hs}, SV={sv}");
        }
    }
}

static void LowercaseCNormalization()
{
    var pairs = new[]
    {
        (WithC: "(120){4}1c,", WithoutC: "(120){4}1,"),
        (WithC: "(120){4}1c-3[8:1],", WithoutC: "(120){4}1-3[8:1],"),
        (WithC: "(120){4}1c@600-3[8:1],", WithoutC: "(120){4}1@600-3[8:1],"),
        (WithC: "(120){4}1/2c,", WithoutC: "(120){4}1/2,")
    };

    foreach (var pair in pairs)
    {
        var withC = Parse(pair.WithC);
        var withoutC = Parse(pair.WithoutC);
        var withPoints = withC.NoteTimings.ToArray();
        var withoutPoints = withoutC.NoteTimings.ToArray();
        Expect(withPoints.Length == withoutPoints.Length, $"c changed timing count for {pair.WithC}");
        for (var i = 0; i < withPoints.Length; i++)
        {
            Expect(withPoints[i].RawContent.IndexOf('c') < 0, "timing RawContent retained lowercase c");
            Expect(withPoints[i].RawContent == withoutPoints[i].RawContent, $"c changed RawContent for {pair.WithC}");
            Expect(NearlyEqual(withPoints[i].HSpeed, withoutPoints[i].HSpeed), "c changed HSpeed");
            Expect(withPoints[i].Notes.Length == withoutPoints[i].Notes.Length, "c changed note count");
            for (var j = 0; j < withPoints[i].Notes.Length; j++)
            {
                Expect(withPoints[i].Notes[j].RawContent.IndexOf('c') < 0, "note RawContent retained lowercase c");
                Expect(withPoints[i].Notes[j].RawContent == withoutPoints[i].Notes[j].RawContent, "c changed note RawContent");
            }
        }
    }

    var centre = FindNote(Parse("(120){4}C,"), "C", 0);
    Expect(centre.Notes.Length == 1 && centre.Notes[0].TouchArea == 'C', "uppercase C centre touch changed");
}

static void ExistingHsForms()
{
    var instant = FindNote(Parse("(120)<HS*2>1,2,"), "2", 0.5);
    Expect(NearlyEqual(instant.HSpeed, 2f), "global HS baseline changed");

    var interpolation = Parse("(120){4}<HS*2[4:1]>1,2,");
    Expect(NearlyEqual(FindNote(interpolation, "1", 0).HSpeed, 2f), "HS interpolation endpoint changed");

    var grouped = Parse("(120)<HS1*2>(1,),2,");
    Expect(FindNote(grouped, "1", 0).SoflanGroup == 1, "HS group marker changed");
    Expect(NearlyEqual(FindNote(grouped, "1", 0).HSpeed, 2f), "HS group speed changed");
    Expect(NearlyEqual(FindNote(grouped, "2", 1.0).HSpeed, 1f), "HS group leaked into group zero");

    var fake = Parse("(120){128}1`2,");
    Expect(fake.NoteTimings.ToArray().Count(point => !point.IsEmpty) == 2, "baseline fake-each count changed");
}

static void SlideHeadOnlyHsScope()
{
    var exactHeadOnly = FindNote(Parse("(120){4}<HS44>(1)-2[4:1]V35[5:1],"), "1-2[4:1]V35[5:1]", 0);
    Expect(exactHeadOnly.Notes.Length == 1, "the requested head-only HS Slide did not remain one note");
    Expect(exactHeadOnly.Notes[0].SoflanGroup == 44, "the requested Slide star lost group 44");
    Expect(exactHeadOnly.Notes[0].SlideSoflanGroup == 0,
        "the requested Slide body was incorrectly assigned to group 44");

    var exactWhole = FindNote(Parse("(120){4}<HS45*2.5>(1-2[4:1]),"), "1-2[4:1]", 0);
    Expect(exactWhole.Notes[0].SoflanGroup == 45 && exactWhole.Notes[0].SlideSoflanGroup == 45,
        "whole-Slide HS scope did not assign group 45 to both star and body");

    var headOnly = FindNote(Parse("(120){4}<HS44*2>(1)-3[4:1]V57[5:1],"), "1-3[4:1]V57[5:1]", 0);
    Expect(headOnly.Notes.Length == 1, "head-only HS slide did not remain one note");
    Expect(headOnly.Notes[0].Type == SimaiNoteType.Slide, "head-only HS slide changed note type");
    Expect(headOnly.Notes[0].SoflanGroup == 44, "head-only HS slide star lost its group");
    Expect(headOnly.Notes[0].SlideSoflanGroup == 0, "head-only HS slide body inherited the star group");

    var wholeSlide = FindNote(Parse("(120){4}<HS45*2>(1-3[4:1]V57[5:1]),"), "1-3[4:1]V57[5:1]", 0);
    Expect(wholeSlide.Notes.Length == 1, "whole-scope HS slide did not remain one note");
    Expect(wholeSlide.Notes[0].SoflanGroup == 45, "whole-scope HS slide star lost its group");
    Expect(wholeSlide.Notes[0].SlideSoflanGroup == 45, "whole-scope HS slide body lost its group");

    var spaced = FindNote(Parse("(120){4}<HS46*2>(1) -3[4:1],"), "1-3[4:1]", 0);
    Expect(spaced.Notes[0].SoflanGroup == 46 && spaced.Notes[0].SlideSoflanGroup == 0,
        "whitespace changed head-only HS slide groups");

    var fixedHead = FindNote(Parse("(120){4}<HS46*2>(1@) -3[4:1],"), "1@-3[4:1]", 0);
    Expect(fixedHead.Notes[0].IsFixedSoflan && fixedHead.Notes[0].SlideSoflanGroup == 0,
        "whitespace broke bare FixedSoflan on a head-only HS slide");

    var auto = FindNote(Parse("(120){4}<HS?*2>(1)-3[4:1],"), "1-3[4:1]", 0);
    Expect(auto.Notes[0].SoflanGroup < 0 && auto.Notes[0].SlideSoflanGroup == 0,
        "auto head-only HS slide body inherited the generated group");

    var sameHead = FindNote(Parse("(120){4}<HS47*2>(1)-3[4:1]*-5[4:1],"), "1-3[4:1]*-5[4:1]", 0);
    Expect(sameHead.Notes.Length == 2 && sameHead.Notes.All(note => note.Type == SimaiNoteType.Slide),
        "head-only HS same-head slide did not preserve both branches");
    Expect(sameHead.Notes.All(note => note.SoflanGroup == 47 && note.SlideSoflanGroup == 0),
        "head-only HS same-head slide groups were inconsistent");

    ExpectThrows<InvalidSimaiSyntaxException>("(120){4}<HS48*2>(1)-,",
        "incomplete head-only HS slide was accepted");
    ExpectThrows<InvalidSimaiSyntaxException>("(120){4}<HS48*2>(1)-3[4:1]",
        "head-only HS slide without a trailing comma was silently dropped");
    ExpectThrows<InvalidSimaiSyntaxException>("(120){4}<HS48*2>(1-3[4:1])-5[4:1],",
        "partially scoped connected slide was accepted");
    var priorScopedNote = Parse("(120){4}<HS48*2>(1,2)-4[4:1],");
    var priorTap = FindNote(priorScopedNote, "1", 0);
    var scopedSlide = FindNote(priorScopedNote, "2-4[4:1]", 0.5);
    Expect(priorTap.Notes[0].SoflanGroup == 48,
        "a prior comma-separated note lost its HS scope group");
    Expect(scopedSlide.Notes[0].SoflanGroup == 48 && scopedSlide.Notes[0].SlideSoflanGroup == 0,
        "a comma-separated head-only Slide did not split its body group");
    ExpectThrows<InvalidSimaiSyntaxException>("(120){4}<HS48*2>(1,)-3[4:1],",
        "an empty trailing slot in a head-only HS scope was accepted");
    ExpectThrows<InvalidSimaiSyntaxException>("(120){4}<HS48*2>(1)-{8},2-4[8:1],",
        "unfinished head-only HS slide leaked across a beat declaration");
    ExpectThrows<InvalidSimaiSyntaxException>("(120){4}<HS48*2>(1)-3[4:1]`1-5[4:1],",
        "head-only HS slide accepted fake-each continuation");
    ExpectThrows<InvalidSimaiSyntaxException>("(120){4}1-<HS44*2>(2)-3[4:1],",
        "HS inside an unfinished Slide path was silently split into another note");
    ExpectThrows<InvalidSimaiSyntaxException>("(120){4}1-<HS44>(2[4:1])V<HS30>(35[5:1]),",
        "the documented embedded-HS Slide form was accepted");
    ExpectThrows<InvalidSimaiSyntaxException>("(120){4}1-3[4:1]V<HS30*2>(35[5:1]),",
        "HS between connected Slide segments was accepted");
    ExpectThrows<InvalidSimaiSyntaxException>("(120){4}1-3[4:1]*<HS30*2>(5[5:1]),",
        "HS after an incomplete same-head Slide branch was accepted");
    ExpectThrows<InvalidSimaiSyntaxException>("(120){4}1-3[4:1]`<HS30*2>(5[5:1]),",
        "HS after an incomplete fake-each Slide branch was accepted");

    var completeBeforeSpeed = Parse("(120){4}1-3[4:1]<HS*2>,");
    Expect(FindNote(completeBeforeSpeed, "1-3[4:1]", 0).Notes[0].Type == SimaiNoteType.Slide,
        "HS after a complete Slide was mistaken for an in-path declaration");

    var consecutiveGroups = Parse("(120){4}<HS1*2>(1)<HS2*3>(2),");
    Expect(FindNote(consecutiveGroups, "1", 0).SoflanGroup == 1,
        "first consecutive HS group was parsed as a slide mark");
    Expect(FindNote(consecutiveGroups, "2", 0).SoflanGroup == 2,
        "second consecutive HS group was parsed as a slide mark");

    var legacySlide = new SimaiNote { Type = SimaiNoteType.Slide, SoflanGroup = 49 };
    Expect(legacySlide.SlideSoflanGroup == 49,
        "an unset SlideSoflanGroup did not inherit SoflanGroup");
    var legacyJsonSlide = JsonSerializer.Deserialize<SimaiNote>("{\"Type\":1,\"SoflanGroup\":49}")!;
    Expect(legacyJsonSlide.SlideSoflanGroup == 49,
        "a legacy JSON Slide without SlideSoflanGroup did not inherit SoflanGroup");
    var explicitJsonSlide = JsonSerializer.Deserialize<SimaiNote>("{\"Type\":1,\"SoflanGroup\":49,\"SlideSoflanGroup\":0}")!;
    Expect(explicitJsonSlide.SlideSoflanGroup == 0,
        "an explicit JSON SlideSoflanGroup=0 was not preserved");

    if (IntPtr.Size == 8)
    {
        unsafe
        {
            var unmanagedNote = new UnmanagedSimaiNote();
            var baseAddress = (byte*)&unmanagedNote;
            Expect(sizeof(UnmanagedSimaiNote) == 64, "UnmanagedSimaiNote x64 size changed");
            Expect((byte*)&unmanagedNote.rawContentLen - baseAddress == 48,
                "UnmanagedSimaiNote rawContentLen ABI offset changed");
            Expect((byte*)&unmanagedNote.slideSoflanGroup - baseAddress == 52,
                "UnmanagedSimaiNote slideSoflanGroup did not use x64 alignment padding");
            Expect((byte*)&unmanagedNote.rawContent - baseAddress == 56,
                "UnmanagedSimaiNote rawContent ABI offset changed");
        }
    }
}

static void ForceYellowBasicModifiers()
{
    var tap = ParseSingleNote("1y");
    Expect(tap.IsForceYellow && tap.RawContent == "1", "Force Yellow Tap was not normalized");

    var ex = ParseSingleNote("1xy");
    Expect(ex.IsForceYellow && ex.IsEx && ex.RawContent == "1", "Force Yellow EX Tap flags changed");
    var reverseEx = ParseSingleNote("1yx");
    Expect(reverseEx.IsForceYellow && reverseEx.IsEx, "Force Yellow/EX order was not accepted");

    var hold = ParseSingleNote("1yh[4:1]");
    var reverseHold = ParseSingleNote("1hy[4:1]");
    Expect(hold.IsForceYellow && hold.Type == SimaiNoteType.Hold && hold.RawContent == "1h[4:1]",
        "Force Yellow Hold was not normalized");
    Expect(reverseHold.IsForceYellow && reverseHold.Type == SimaiNoteType.Hold,
        "Force Yellow/Hold order was not accepted");

    var touch = ParseSingleNote("B1fy");
    var touchHold = ParseSingleNote("Cyh[4:1]");
    Expect(touch.IsForceYellow && touch.IsHanabi && touch.Type == SimaiNoteType.Touch,
        "Force Yellow Touch flags changed");
    Expect(touchHold.IsForceYellow && touchHold.Type == SimaiNoteType.TouchHold,
        "Force Yellow TouchHold was not parsed");

    var forceStar = ParseSingleNote("1$y");
    var fakeRotate = ParseSingleNote("1y$$");
    Expect(forceStar.IsForceYellow && forceStar.IsForceStar, "Force Yellow Force Star was not parsed");
    Expect(fakeRotate.IsForceYellow && fakeRotate.IsForceStar && fakeRotate.IsFakeRotate,
        "Force Yellow fake-rotation star was not parsed");

    var fixedTap = ParseSingleNote("1y@600");
    var fixedSlide = ParseSingleNote("1y@600-3[8:1]");
    Expect(fixedTap.IsForceYellow && fixedTap.IsFixedSoflan && fixedTap.HasFixedSoflanSpeed,
        "Force Yellow FixedSoflan Tap changed");
    Expect(fixedSlide.IsForceYellow && fixedSlide.IsFixedSoflan && fixedSlide.Type == SimaiNoteType.Slide,
        "Force Yellow FixedSoflan Slide changed");

    var noHeadBefore = ParseSingleNote("1y!-3[8:1]");
    var noHeadAfter = ParseSingleNote("1!y-3[8:1]");
    Expect(noHeadBefore.IsForceYellow && noHeadBefore.IsSlideNoHead && noHeadBefore.ForceYellowSlideSegmentIndices.Length == 0,
        "Force Yellow before no-head marker changed scope");
    Expect(noHeadAfter.IsForceYellow && noHeadAfter.IsSlideNoHead && noHeadAfter.ForceYellowSlideSegmentIndices.Length == 0,
        "Force Yellow after no-head marker changed scope");
}

static void ForceYellowSlideSegments()
{
    var beforeDuration = ParseSingleNote("1-3y[8:1]");
    var afterDuration = ParseSingleNote("1-3[8:1]y");
    Expect(!beforeDuration.IsForceYellow && beforeDuration.ForceYellowSlideSegmentIndices.SequenceEqual(new[] { 0 }),
        "pre-duration Force Yellow segment index was wrong");
    Expect(afterDuration.ForceYellowSlideSegmentIndices.SequenceEqual(new[] { 0 }),
        "post-duration Force Yellow segment index was wrong");
    Expect(beforeDuration.RawContent == "1-3[8:1]" && afterDuration.RawContent == "1-3[8:1]",
        "Force Yellow remained in Slide RawContent");

    var firstOnly = ParseSingleNote("1-3y[8:1]-5[8:1]");
    var secondOnly = ParseSingleNote("1-3[8:1]-5y[8:1]");
    var both = ParseSingleNote("1-3[8:1]y-5y[8:1]");
    var sharedDuration = ParseSingleNote("1-3y-5[8:1]");
    Expect(firstOnly.ForceYellowSlideSegmentIndices.SequenceEqual(new[] { 0 }),
        "first connected Slide segment index was wrong");
    Expect(secondOnly.ForceYellowSlideSegmentIndices.SequenceEqual(new[] { 1 }),
        "second connected Slide segment index was wrong");
    Expect(both.ForceYellowSlideSegmentIndices.SequenceEqual(new[] { 0, 1 }),
        "connected Slide did not retain both segment indices");
    Expect(sharedDuration.ForceYellowSlideSegmentIndices.SequenceEqual(new[] { 0 }),
        "shared-duration connected Slide segment index was wrong");

    var bigV = ParseSingleNote("1V35y[8:1]");
    var wifi = ParseSingleNote("1w5[8:1]y");
    Expect(bigV.ForceYellowSlideSegmentIndices.SequenceEqual(new[] { 0 }), "big-V Force Yellow was not parsed");
    Expect(wifi.ForceYellowSlideSegmentIndices.SequenceEqual(new[] { 0 }), "Wifi Force Yellow was not parsed");

    var noHeadTrack = ParseSingleNote("1!-3y[8:1]");
    Expect(!noHeadTrack.IsForceYellow && noHeadTrack.IsSlideNoHead &&
           noHeadTrack.ForceYellowSlideSegmentIndices.SequenceEqual(new[] { 0 }),
        "no-head Force Yellow track changed scope");

    var sameHeadChart = Parse("(120){4}1y-3[8:1]*-5y[8:1],");
    var sameHeadPoint = FindNote(sameHeadChart, "1y-3[8:1]*-5y[8:1]", 0);
    Expect(sameHeadPoint.Notes.Length == 2, "same-head Force Yellow branch count changed");
    Expect(sameHeadPoint.Notes[0].IsForceYellow && sameHeadPoint.Notes[0].ForceYellowSlideSegmentIndices.Length == 0,
        "same-head visible branch Force Yellow was wrong");
    Expect(sameHeadPoint.Notes[1].IsSlideNoHead &&
           sameHeadPoint.Notes[1].ForceYellowSlideSegmentIndices.SequenceEqual(new[] { 0 }),
        "same-head no-head branch Force Yellow segment was wrong");

    var independentBreakBranch = Parse("(120){4}1y-3[8:1]*-5b[8:1],");
    Expect(FindNote(independentBreakBranch, "1y-3[8:1]*-5b[8:1]", 0).Notes.Length == 2,
        "independent same-head Break branch conflicted with Force Yellow");
}

static void ForceYellowNaturalEach()
{
    var slash = FindNote(Parse("(120){4}1y/2,"), "1y/2", 0);
    Expect(slash.Notes.Length == 2 && slash.Notes.All(note => !note.IsForceYellow),
        "natural each did not clear Force Yellow heads");

    var slideEach = FindNote(Parse("(120){4}1y-3y[8:1]/2,"), "1y-3y[8:1]/2", 0);
    var slide = slideEach.Notes.Single(note => note.Type == SimaiNoteType.Slide);
    Expect(!slide.IsForceYellow && slide.ForceYellowSlideSegmentIndices.SequenceEqual(new[] { 0 }),
        "natural each cleared the Force Yellow Slide track or retained the head");

    var excludedMine = FindNote(Parse("(120){4}1y/2m,"), "1y/2m", 0);
    Expect(excludedMine.Notes.Single(note => !note.IsMine).IsForceYellow,
        "Mine incorrectly caused natural-each Force Yellow clearing");

    var excludedNoHead = FindNote(Parse("(120){4}1y!-3[8:1]/2,"), "1y!-3[8:1]/2", 0);
    Expect(excludedNoHead.Notes.Single(note => note.IsSlideNoHead).IsForceYellow,
        "no-head Slide incorrectly lost its hidden-head Force Yellow flag");

    var fakeCollision = Parse("(120){128}1`2y,3,");
    var second = FindNote(fakeCollision, "2y", 1.875 / 120d).Notes[0];
    Expect(!second.IsForceYellow, "same-time notes from separate timing points did not clear Force Yellow");
}

static void InvalidForceYellowForms()
{
    var invalid = new[]
    {
        "1yb", "1by", "1ym", "1my",
        "1y-3b[8:1]", "1b-3y[8:1]", "1-3y[8:1]-5b[8:1]",
        "1yy", "1-3yy[8:1]", "1-3y[8:1]y",
        "Y1", "1Y", "y1", "1-y3[8:1]", "1h[4:1]y", "1-3[8:y1]",
        "1@y600", "1y@600!-3[8:1]", "1y-3", "1yh[bad]"
    };

    foreach (var note in invalid)
    {
        ExpectThrows<InvalidSimaiSyntaxException>($"(120){{4}}{note},", $"expected Force Yellow rejection for {note}");
    }
}

static void ForceYellowJsonDefaults()
{
    var legacy = JsonSerializer.Deserialize<SimaiNote>("{\"Type\":0}")!;
    Expect(!legacy.IsForceYellow, "legacy JSON defaulted IsForceYellow to true");
    Expect(legacy.ForceYellowSlideSegmentIndices is not null && legacy.ForceYellowSlideSegmentIndices.Length == 0,
        "legacy JSON did not default ForceYellowSlideSegmentIndices to an empty array");

    var explicitValue = JsonSerializer.Deserialize<SimaiNote>(
        "{\"Type\":1,\"IsForceYellow\":true,\"ForceYellowSlideSegmentIndices\":[0,2]}")!;
    Expect(explicitValue.IsForceYellow && explicitValue.ForceYellowSlideSegmentIndices.SequenceEqual(new[] { 0, 2 }),
        "managed JSON did not preserve Force Yellow fields");

    var nullValue = JsonSerializer.Deserialize<SimaiNote>(
        "{\"Type\":1,\"ForceYellowSlideSegmentIndices\":null}")!;
    Expect(nullValue.ForceYellowSlideSegmentIndices.Length == 0,
        "managed JSON null did not normalize ForceYellowSlideSegmentIndices");
}

static SimaiNote ParseSingleNote(string noteContent)
{
    var chart = Parse($"(120){{4}}{noteContent},");
    var notes = chart.NoteTimings.ToArray()
        .Where(point => !point.IsEmpty)
        .SelectMany(point => point.Notes)
        .ToArray();
    Expect(notes.Length == 1, $"expected one parsed note for {noteContent}, found {notes.Length}");
    return notes[0];
}

static void ExpectThrows<T>(string fumen, string message) where T : Exception
{
    try
    {
        _ = Parse(fumen);
    }
    catch (T)
    {
        return;
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"{message}; got {ex.GetType().Name}: {ex.Message}");
    }

    throw new InvalidOperationException(message);
}

static bool NearlyEqual(double left, double right)
{
    return Math.Abs(left - right) <= 1e-7;
}

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
