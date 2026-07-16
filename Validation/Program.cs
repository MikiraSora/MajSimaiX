using MajSimai;

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
    ("existing HS forms remain usable", ExistingHsForms)
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
