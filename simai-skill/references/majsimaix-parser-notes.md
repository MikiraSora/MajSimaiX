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

### 5.2 Grouped instantaneous

```
<HS1*2.0>      group 1 speed = 2.0
<HS0*3.0>      explicitly set default group 0
```

- `g` is a non-negative integer (group number).
- Group speeds are stored independently and can be referenced later.

### 5.3 Linear interpolation

```
<HS*2.0[8:1]>             interpolate to 2.0 over [8:1] duration
<HS1*0.5[#1.25]>          group 1, interpolate over 1.25 seconds
<HS2*3.0[150#4:1]>        group 2, custom BPM 150 for duration
```

- Duration reuses the Hold duration syntax: `[divide:count]`, `[#seconds]`,
  `[bpm#divide:count]`.
- The interpolation spans from `nowTime - duration` to `nowTime`.
- Sampling is aligned to the 384-grid at the current BPM, every 32 grids.

### 5.4 Chain interpolation

```
<HS*2.0[8:1]~1.0[4:1]>              two segments
<HS*2.0[8:1]~1.0[4:1]~-0.5[#1.25]>  three segments
```

- Each segment is `targetHSpeed[duration]`, joined by `~`.
- Negative and zero HSpeed values are allowed (e.g. `~-1[4:1]`).
- No instantaneous segments allowed when `~` is present.

### 5.5 Group scope

```
<HS1*2.0>(1,2,3,4),     notes inside () belong to group 1
<HS1>(1,2,),            reuse previously declared group 1 speed
<HS1*1.5[8:1]>(1,2,3,4) group 1 with interpolation, then scope
```

- All notes inside `()` belong to the specified group.
- After `)`, the group scope exits and returns to group 0.
- HS declarations are **not allowed** inside the group scope.

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

## 7. Differences from standard simai

MajSimaiX is broadly compatible with standard simai but adds extensions and
omits a few features. When in doubt, use MajSimaiX syntax.

### MajSimaiX extensions (not in standard simai)

| Feature | Syntax |
|---------|--------|
| HS / Soflan | `<HS...>`, `<HSg...>`, `<HSg>(...)` |
| FixedSoflan | `@`, `@speed` |
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

## 8. Source file index

| File | Responsibility |
|------|----------------|
| `Runtime/SimaiParser.cs` | File/metadata parsing, chart timing loop, `<HS>` tags, group scope, interpolation, comma/fake-each timing. |
| `Runtime/SimaiNoteParser.cs` | Note-level parsing: flags, holds, slides, same-head, touch, FixedSoflan `@`. |
| `Runtime/SimaiRawTimingPoint.cs` | Raw timing content, FixedSoflan `@` whitespace validation, SoflanGroup propagation. |
| `Runtime/SimaiNote.cs` | Note data model (type, position, flags, SoflanGroup, FixedSoflan fields). |
| `Runtime/SimaiTimingPoint.cs` | Timing point data model (timing, BPM, HSpeed, SoflanGroup, notes). |
| `Runtime/SimaiMetadata.cs` | Metadata data model (title, artist, offset, designers, levels, fumens, commands). |
| `HS_Soflan_Reference.md` | Full HS/Soflan and FixedSoflan reference with runtime support matrix. |
| `README.md` | Feature checklist and API usage examples. |