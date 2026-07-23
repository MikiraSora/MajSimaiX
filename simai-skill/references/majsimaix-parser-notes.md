# MajSimaiX Parser Notes

This document covers MajSimaiX-specific implementation details that go beyond
basic simai syntax. It is derived from the parser source code in
`Assets/Plugins/MajSimaiX/Runtime/` and the `HS_Soflan_Reference.md` in the
same directory. Always prefer the behaviour described here over external simai
documentation when writing charts for MajSimaiX.

---

## 1. Slide marks

MajSimaiX recognises these 11 slide marks (defined in `SimaiNoteParser.cs`
`IsSlideMark` and `NoteFlag.Detect`):

| Mark | Name | Description |
|------|------|-------------|
| `-` | straight | Direct line to the target. |
| `<` | curve-left | Arc curving toward the left. |
| `>` | curve-right | Arc curving toward the right. |
| `^` | V-up | V-shape peaking outward. |
| `v` | V-down | V-shape dipping inward. |
| `V` | big-V | Large V-shape. |
| `p` | S-curve p | S-curve (one direction). |
| `q` | S-curve q | S-curve (other direction). |
| `s` | circle-cw | Full circle, clockwise. |
| `z` | circle-ccw | Full circle, counter-clockwise. |
| `w` | wifi | Fan-out to multiple targets. |

Any of these characters inside a note token causes the parser to treat it as
a slide. The visual rendering is handled by the MajdataView runtime, not the
parser.

---

## 2. Same-head slides (`*`)

Use `*` to chain multiple slides that share the same starting head:

```
1-3[8:1]*-5[8:1]
```

- The first segment `1-3[8:1]` is a normal slide (with star head).
- Each segment after `*` is a **no-head slide**: the parser prepends the head
  digit from the first segment and marks it `IsSlideNoHead = true`.
- `*-5[8:1]` becomes `1-5[8:1]` internally, rendered without a second star.

Multiple same-head slides:

```
1-3[8:1]*-5[8:1]*-7[8:1]
```

---

## 3. Conn slides (multi-segment)

Chain slide marks in a single note token to create a multi-point path:

```
1-3-5-7[8:1]           duration applies to the whole chain
1-3[8:1]-5[8:1]-7[8:1] per-segment durations
```

- The parser accumulates all `[...]` durations into a single `SlideTime`.
- The slide wait time is one beat at the effective BPM (current or custom).
- The raw content preserves the full chain text for the runtime to render.

Mixed marks are allowed:

```
1>5[4:1]-8[8:1]        curve-right to 5, then straight to 8
```

---

## 4. No-head slides (`!` and `?`)

A no-head slide creates a slide path without showing a star at the start
position.

| Syntax | Flag | Behaviour |
|--------|------|-----------|
| `1!-3[8:1]` | `IsSlideNoHead` | No star head; slide starts at `timing + wait`. |
| `1?-3[8:1]` | `IsSlideNoHead` (delayed) | No star head; delayed variant. |

Both set `IsSlideNoHead = true`. The runtime distinguishes the delayed
variant visually.

**FixedSoflan `@` is not supported on no-head slides.** The parser throws
`FixedSoflan modifier is only supported on slide star heads`.

---

## 5. HS / Soflan (Hi-Speed)

HS is MajSimaiX's visual speed-change system. It changes the **visual**
timeline only; it does not affect audio time, judgement time, or note logic
time. Full details are in `HS_Soflan_Reference.md`.

### 5.1 Global instantaneous

```
<HS*1.0>       set global HSpeed to 1.0 (default)
<HS*2.0>       2x speed
<HS*0.5>       half speed
```

### 5.2 SV compatibility tag

```text
<SV*2>       group 0 HSpeed = 2
<SV*0>       stop the visual timeline
<SV*-1>      reverse the visual timeline
<SV*1>       explicitly restore the default speed
```

- Only uppercase `SV` is accepted, and only the instantaneous `<SV*number>` form.
- The numeric grammar is the same as instantaneous HS: invariant-culture float
  parsing, including zero, negative values, a leading plus sign, exponents, and
  the special values accepted by HS (`NaN`/`Infinity`).
- SV is normalized to a group `0` `HSpeedEvent` plus an empty timing point. It
  does not maintain an SV multiplier or an `SVeloc` field; `CommaTimings.HSpeed`
  remains `1`.
- `<SV*x>`, `<HS*x>`, and `<HS0*x>` share the group `0` timeline. At one actual
  timestamp the last declaration in source order wins, and the value persists
  until the next group `0` HS/SV declaration. Nonzero HS groups remain separate.
- A tag may occur before or after a note and applies to `/` each notes in that
  timing. Fake-each items query the event timeline at their expanded timing, so
  an item crossing into the next comma slot can see a later HS/SV command.
- Group prefixes, `?` groups, note scopes, durations, `~` chains, and easing
  are invalid for SV and raise `InvalidSimaiSyntaxException`; invalid numeric
  literals use the HS `InvalidSimaiMarkupException` boundary. Tags must close
  with `>`; a following `(120)` remains an ordinary BPM declaration.

SV error matrix:

| Input | Result |
| --- | --- |
| `<SV*>`, `<SV**2>`, `<SV1*2>`, `<SV?>` | `InvalidSimaiSyntaxException` |
| `<SV*abc>` | `InvalidSimaiMarkupException` |
| `<SV*2[4:1]>`, `<SV*2~1>`, `<SV*2[4:1]ioCubic>` | `InvalidSimaiSyntaxException` |
| `<SV*2>(1,)` or SV inside `<HS1>(...)` | `InvalidSimaiSyntaxException` |
| `<SV*2` | `InvalidSimaiSyntaxException` |

### 5.3 Grouped instantaneous

```
<HS1*2.0>      group 1 speed = 2.0
<HS0*3.0>      explicitly set default group 0
```

- `g` is a non-negative integer (group number).
- Group speeds are stored independently and can be referenced later.

### 5.4 Interpolation and easing

```
<HS*2.0[8:1]>             interpolate to 2.0 over [8:1] duration
<HS1*0.5[#1.25]>          group 1, interpolate over 1.25 seconds
<HS2*3.0[150#4:1]>        group 2, custom BPM 150 for duration
<HS*4[4:1]easeInOutCubic> full easing name
<HS*4[4:1]ioCubic>        equivalent abbreviation
<HS*4[#0]>                 instantaneous segment at this boundary
```

- Duration reuses the Hold duration syntax: `[divide:count]`, `[#seconds]`,
  `[bpm#divide:count]`.
- HS speed and duration numbers use invariant culture with `.` as the decimal
  separator.
- Only absolute seconds may be exactly zero. `[#0]`, `[#0.0]`, `[#+0]`, and
  `[#0e0]` assign the target speed instantaneously at the segment boundary;
  the boundary itself uses the new speed. Ratio zero, negative zero, negative,
  non-finite, and positive-underflow-to-zero durations are invalid.
- Any HS command with a duration requires a valid preceding BPM declaration,
  even when every segment is `[#0]`.
- The interpolation spans from `nowTime - duration` to `nowTime`.
- Sampling is aligned to the 384-grid at the current BPM. The interval is
  controlled by `HSpeedInterpolationGrid` and defaults to every 32 grids.
- The optional suffix is an easings.net easing name. Names and abbreviations
  are case-insensitive. Omitting the suffix, or explicitly writing `linear`,
  uses linear interpolation.
- Full names use `easeInX`, `easeOutX`, or `easeInOutX`. Abbreviations use
  `iX`, `oX`, or `ioX`. `X` may be `Quad`, `Cubic`, `Quart`, `Quint`, `Sine`,
  `Expo`, `Circ`, `Back`, `Elastic`, or `Bounce`.
- Whitespace and line breaks are allowed before or after the easing name, but
  not inside it. Unknown names are syntax errors; easing cannot suffix an
  instantaneous HS command without a duration. A zero-duration segment may
  retain any valid easing suffix, but the easing has no effect.
- Easing maps each sampled HSpeed progress value. `Back`, `Elastic`, and
  `Bounce` retain their official overshoot/bounce behaviour; segment endpoints
  remain exact.

### 5.5 Chain interpolation

```
<HS*2.0[8:1]~1.0[4:1]>              two segments
<HS*2.0[8:1]~1.0[4:1]~-0.5[#1.25]>  three segments
<HS*2.0[8:1]ioCubic~1.0[4:1]oBounce> per-segment easing
<HS*2[#0]~4[#1]>                     jump to 2x, then interpolate to 4x
<HS*2[#1]~4[#0]~8[#1]>               discontinuity between interpolations
```

- Each segment is `targetHSpeed[duration]easing`, joined by `~`.
- Easing is selected independently per segment. A segment without a suffix is
  linear and does not inherit the preceding segment's easing.
- Negative and zero HSpeed values are allowed (e.g. `~-1[4:1]`).
- A `[#0]` segment executes from left to right at its boundary, breaks
  interpolation continuity, and supplies the starting speed for the next
  positive-duration segment. Consecutive zero segments use the last target.
- Zero segments are valid at the start, middle, or end of a chain and as the
  only segment. They create an empty boundary timing point and are independent
  of `HSpeedInterpolationGrid`.
- A chained segment may not omit its duration. Write `target[#0]` explicitly
  for an instantaneous segment.
- Positive durations such as `[#0.00000001]` remain real interpolation
  durations and are not normalized to zero.
- Same-group HS commands at one timestamp execute in source order; the last
  command determines the speed for all notes and empty timing points at that
  boundary.
- Easing is expanded into ordinary sampled HSpeed points during parsing. It is
  not retained in `SimaiChart`, and MA2 export writes sampled `SFL` values, so
  the original easing name cannot be reconstructed from parsed/exported data.

### 5.6 Group scope

```
<HS1*2.0>(1,2,3,4),     notes inside () belong to group 1
<HS1>(1,2,),            reuse previously declared group 1 speed
<HS1*1.5[8:1]>(1,2,3,4) group 1 with interpolation, then scope
<HS1*2.0>(1)-3[4:1]     only the slide star head belongs to group 1
```

- All notes inside `()` belong to the specified group.
- If the scope contains only a Slide star head and a slide mark follows `)`, the parser keeps one Slide note but assigns the body to the outside group. For example, `<HS1>(1)-3[4:1]` gives the star head group `1` and the body group `0`.
- Put the complete Slide inside the scope, as in `<HS1>(1-3[4:1])`, when both the star head and body should use group `1`.
- The head-only form requires the final note before `)` to be one valid star head. Earlier comma-separated notes in the scope remain valid and keep the HS group, for example `<HS1>(1,2)-4[4:1]`; only the Slide body is assigned to group `0`. The final head still cannot use `/` each, backtick fake-each, or a scope that contains only part of a connected Slide path.
- HS/SV declarations embedded inside a Slide path are unsupported; keep the declaration outside the Slide or scope the complete Slide.
- After `)`, the group scope exits and returns to group 0.
- HS and SV declarations are **not allowed** inside the group scope.

### 5.7 Raw note normalization

Before FixedSoflan `@` validation and note flag detection, the parser removes
lowercase `c` from raw note content. Therefore `1c`, `1-3[8:1]c`, and
`1c@600-3[8:1]` are equivalent to their forms without `c`, and both timing and
note `RawContent` contain no lowercase `c`. Uppercase `C` remains the centre
Touch marker.

---

## 6. FixedSoflan (`@`)

`@` is a per-note modifier that fixes the visual speed of a single Tap or
Star head, making it immune to the player's note-speed setting. It is not an
HS command and does not create a Soflan group.

### Syntax

| Form | Meaning |
|------|---------|
| `1@` | FixedSoflan with default speed 600. |
| `1@600` | FixedSoflan with explicit speed 600. |
| `1@750.5` | FixedSoflan with speed 750.5. |
| `1@-3[8:1]` | FixedSoflan on a slide star head (before the first mark). |
| `1@600-3[8:1]` | FixedSoflan with speed 600 on a slide star head. |
| `1@w5[8:1]` | FixedSoflan on a wifi slide head. |

### Rules

- Speed must be a positive float (invariant culture). `0`, negative, `NaN`,
  `Infinity` are invalid.
- For non-slide tokens: `@` goes at the end (or before the speed value).
- For slide tokens: `@` goes after the star head digit and **before the first
  slide mark**.
- Only one `@` per note token.
- `@` only modifies the current token. In `1@/2`, only `1` is FixedSoflan.
- No-head slides cannot carry `@`.

### Interaction with HS

FixedSoflan and Soflan groups are independent:

```
<HS1*2.0>(1@,2,3@750,)
```

- `1@` and `3@750` belong to group 1 **and** have FixedSoflan.
- `2` belongs to group 1 but uses the player's note speed.
- If no HS change exists (`containsSoflans() == false`), `@` is parsed but the
  runtime uses the normal display path.

### Runtime support

FixedSoflan visually affects: Tap, Break, EX Tap, Force Star, normal Star
heads, and Slide/Wifi star heads. It does **not** affect: Hold, Touch,
TouchHold, Slide body, Wifi body, or EachLine.

---

## 7. Force Yellow (`y`)

`y` is a MajSimaiX per-component appearance extension. It does not participate
in logical EACH analysis. The base syntax and examples are in
`simai-syntax.md`; this section defines the parser and data-model contract.

### 7.1 Component scope

- A header `y` applies to a Tap, Hold, Touch, TouchHold, Force Star, or Slide
  star head. It must appear after the note identity and before the first Slide
  mark or Hold duration.
- For a Slide segment, `y` may appear after the endpoint before `[...]`, or
  immediately after that segment's `]`. Both forms set the same segment index.
- Connected Slide indices are zero-based in source order. A Wifi path is one
  segment. Same-head `*` branches are separate `SimaiNote` instances and each
  starts at segment index 0.
- Header and path scope never spread into one another. A no-head Slide may keep
  `IsForceYellow = true` for its hidden head, but only an index in
  `ForceYellowSlideSegmentIndices` colors its path.

### 7.2 Conflicts and normalization

- `y` is compatible with `x`, `f`, `$` / `$$`, and `@`.
- `y` is incompatible with `b` and `m`. On a Slide, the conflict boundary is
  the complete parsed branch: a flag on the head conflicts with one on any
  connected segment and vice versa. `/` notes and `*` branches remain
  independent.
- Duplicate `y` on one header or one segment is a syntax error. Uppercase `Y`
  and ambiguous positions such as `-y3` are also errors.
- After all actual timings are known, a natural EACH group uses the same
  `1e-9` timing tolerance and candidate rules as MajdataEdit's
  `EachNoteAnalysis`. MajSimaiX clears `IsForceYellow` from its eligible heads;
  it does not clear Slide segment indices.
- `RawContent` contains the structural note after all `y` characters have been
  removed. The original `SimaiChart.Fumen` remains unchanged.

### 7.3 Managed data contract

```csharp
public bool IsForceYellow { get; set; }
public int[] ForceYellowSlideSegmentIndices { get; set; } = Array.Empty<int>();
```

The segment array is empty for non-Slides and unmodified paths. Non-empty
indices must be strictly increasing, unique, non-negative, and smaller than
the parsed path count. Missing or explicit `null` values in legacy managed JSON
normalize to an empty array; missing `IsForceYellow` defaults to `false`.

The existing `MajSimai_Parse` native ABI deliberately remains unchanged and
does not expose either field. Native consumers need a future versioned API for
complete head and per-segment support. MajdataView rendering is also outside
the current parser contract; adding fields to managed JSON alone does not make
an older runtime display them.

---

## 8. Differences from standard simai

MajSimaiX is broadly compatible with standard simai but adds extensions and
omits a few features. When in doubt, use MajSimaiX syntax.

### MajSimaiX extensions (not in standard simai)

| Feature | Syntax |
|---------|--------|
| HS / Soflan | `<HS...>`, `<HSg...>`, `<HSg>(...)` |
| FixedSoflan | `@`, `@speed` |
| Force Yellow | `y` on a note header or Slide path segment |
| Force star | `1$` |
| Fake rotation | `1$$` |
| No-head slide | `1!-3[8:1]`, `1?-3[8:1]` |
| Wifi slide | `w` mark |
| Extended slide durations | `[bpm#seconds]`, `[wait##...]` forms |
| Time signature comment | `||sN/D` |
| Fake each | `` 1`2`3 `` (backtick) |
| Shorthand tap pair | `12` (two digits = `1/2`) |

### Not supported by MajSimaiX

| Feature | Notes |
|---------|-------|
| `E` EOF flag | Listed in README as unimplemented; code is commented out. |

### Behavioural notes

- **Metadata first-wins**: for `title`, `artist`, `des`, `des_n`, `lv_n`, the
  first non-empty value is kept. Custom `&key=value` commands are all
  preserved.
- **Whitespace**: the parser strips all whitespace from note content before
  parsing (newlines become spaces, then all whitespace is removed). Exception:
  `@` speed values must not contain internal whitespace.
- **Touch zones**: A–E ring zones take a position digit (1–8). Zone C (centre)
  takes no position digit.
- **Slide duration required**: a slide without `[...]` fails to parse.
- **Hold duration optional**: `1h` without `[...]` is valid (0 duration).
- **BPM before notes**: BPM must be declared with `(bpm)` before any notes;
  otherwise timing calculations divide by zero.

---

## 9. Source file index

| File | Responsibility |
|------|----------------|
| `Runtime/SimaiParser.cs` | File/metadata parsing, chart timing loop, `<HS>` tags, group scope, interpolation, comma/fake-each timing. |
| `Runtime/SimaiNoteParser.cs` | Note-level parsing: flags, holds, slides, same-head, touch, FixedSoflan `@`, and Force Yellow `y`. |
| `Runtime/ForceYellowModifierParser.cs` | Component-level `y` parsing, conflict detection, normalization, and Slide segment indices. |
| `Runtime/ForceYellowNormalizer.cs` | Clears redundant header flags from natural EACH groups after timing resolution. |
| `Runtime/SimaiRawTimingPoint.cs` | Raw timing content, FixedSoflan `@` whitespace validation, `SoflanGroup` / `SlideSoflanGroup` propagation. |
| `Runtime/SimaiNote.cs` | Note data model, including managed Force Yellow fields. |
| `Runtime/SimaiTimingPoint.cs` | Timing point data model (timing, BPM, HSpeed, SoflanGroup, notes). |
| `Runtime/SimaiMetadata.cs` | Metadata data model (title, artist, offset, designers, levels, fumens, commands). |
| `HS_Soflan_Reference.md` | Full HS/Soflan and FixedSoflan reference with runtime support matrix. |
| `README.md` | Feature checklist and API usage examples. |
