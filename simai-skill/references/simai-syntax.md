# Simai Syntax Reference

This document describes the simai / maidata chart syntax as parsed by
**MajSimaiX** (`Assets/Plugins/MajSimaiX`). It is the ground truth for writing
charts that the parser accepts. MajSimaiX-specific extensions (HS/Soflan,
FixedSoflan, wifi slides, etc.) are documented in
`majsimaix-parser-notes.md`.

External references:
- MajdataView wiki (怎样写谱?): https://github.com/LingFeng-bbben/MajdataView/wiki/怎样写谱？
- simai wiki: https://w.atwiki.jp/simai/pages/1003.html

---

## 1. File structure

A maidata file is a sequence of `&key=value` lines. The value may span
multiple lines for chart content.

```
&title=Song Title
&artist=Artist Name
&first=2.25
&des=Chart Designer
&lv_5=12+
&inote_5=
(180){8}1,2,3,4,5,6,7,8,
```

### Metadata fields

| Field | Meaning |
|-------|---------|
| `&title=` | Song title. |
| `&artist=` | Artist name. |
| `&first=` | Audio offset in seconds (float). Time before the first beat. |
| `&des=` | Default designer (fallback for all charts). |
| `&des_1` … `&des_7` | Per-chart designer (1 = Basic, …, 7 = Re:Master). |
| `&lv_1` … `&lv_7` | Per-chart level string (e.g. `10+`, `13`). |
| `&inote_1` … `&inote_7` | Per-chart fumen (note data). Multi-line: continues until the next line that starts with `&`. |
| `&anykey=value` | Custom command. Stored as a `SimaiCommand(prefix, value)`. All occurrences are kept. |

Notes on metadata:
- For `title`, `artist`, `des`, `des_n`, `lv_n`: the **first non-empty**
  occurrence wins; later duplicates are ignored.
- Chart indices 1–7 map to: 1 Basic, 2 Advanced, 3 Expert, 4 Master,
  5 Re:Master, 6–7 additional.
- Whitespace is trimmed from each line before matching the prefix.

### Comments (inside fumen)

| Syntax | Meaning |
|--------|---------|
| `||text…` | Rest of line is a comment, ignored by the parser. |
| `||sN/D` | Special comment that sets the time signature to N/D (e.g. `||s4/4`). Stored on timing points for display; does not affect note timing. |

---

## 2. Timing

### BPM

```
(180)
(150.5)
```

- `(float)` sets the BPM. A float is accepted.
- BPM stays in effect until the next `(bpm)` declaration.
- BPM **must** be set before any notes; the parser initialises BPM to 0 and
  dividing by 0 produces invalid timing.

### Beats / subdivision

```
{4}    each comma = one beat (quarter note)
{8}    each comma = eighth note
{16}   each comma = sixteenth note
{#2.5} each comma = exactly 2.5 seconds (absolute)
```

- `{n}` (integer or float): each comma advances by `60 / bpm * 4 / n` seconds.
  `{4}` = one beat, `{8}` = half a beat, `{16}` = quarter of a beat.
- `{#seconds}`: each comma advances by exactly `seconds`, independent of BPM.
- Default (before any `{}` declaration) is `{4}`.

### Comma and time advance

- A comma `,` advances the current time by one beat-interval as set above.
- An **empty comma** (nothing between the previous comma and this one) is a
  rest — no note is placed, but time still advances.
- The last comma before the end of the fumen also advances time.

Example — four sixteenth-note taps followed by a rest:

```
(180){16}1,2,3,4,,
```

---

## 3. Notes

Positions 1–8 correspond to the 8 keys around the ring, numbered clockwise
starting from the top (1 = top, going clockwise: 2, 3, 4, 5 = bottom,
6, 7, 8).

### Tap and tap flags

| Syntax | Meaning |
|--------|---------|
| `1` | Tap at position 1. |
| `1b` | Break tap. |
| `1x` | EX tap. |
| `1m` | Mine. |
| `1$` | Force star (creates a star without a slide). |
| `1$$` | Force star with fake rotation. |
| `1bx` | Break + EX (flags combine). |

Flags are detected by scanning the note token left-to-right. They can appear
in any order after the position digit and combine freely:
- `b` — break
- `x` — EX
- `m` — mine
- `f` — hanabi (touch only)
- `$` — force star (`$$` = fake rotation)

### Hold

```
1h            short hold (0 duration)
1h[4:1]       hold for one beat
1h[#2.5]      hold for 2.5 seconds (absolute)
1h[120#4:1]   hold with custom BPM 120
1bh / 1hb     break hold
```

- `h` marks a hold. A duration in `[...]` is optional.
- Hold durations accept three forms (see §4 for details).

### Slide

A slide connects the current position to a target via a **slide mark** and a
duration. See `majsimaix-parser-notes.md` for the full list of marks and
same-head / conn / no-head variants.

Basic forms:

```
1-4[8:3]              straight slide 1→4
1>5[4:1]              curve-right slide 1→5
1-3[8:1]*-5[8:1]      same-head: 1→3 then 1→5 (see parser notes)
1>5[4:1]-8[8:1]       conn slide: 1→5 then 5→8
```

- A slide mark is one of: `- ^ v < > V p q s z w`
- The duration `[...]` is **required** for slides.
- Slide durations accept additional forms with custom BPM and wait time
  (see §4).

### Touch

Touch notes start with a zone letter **A–E**, followed by a position digit
(1–8). Zone **C** is the centre and takes no position digit.

| Zone | Meaning |
|------|---------|
| A | Upper-left ring zone |
| B | Upper-right ring zone |
| C | Centre |
| D | Lower-left ring zone |
| E | Lower-right ring zone |

```
B1      touch at zone B, position 1
B1f     touch with hanabi (firework) flag
B1b     break touch
B1m     mine touch
B1x     EX touch
C       centre touch
```

### Touch hold

```
Ch[4:1]    centre touch hold, one beat
B1h        touch hold, short form (0 duration)
B1hf[4:1]  touch hold with hanabi
B1hb[4:1]  break touch hold
B1hx[4:1]  EX touch hold
```

- Same flag set as touch, plus `h` for hold and `[duration]`.

### Each notes

Notes separated by `/` are played simultaneously (same timing):

```
1/2        tap 1 and tap 2 at the same time
1/2/3      three simultaneous taps
1h[4:1]/2  hold 1 and tap 2 simultaneously
```

### Shorthand tap pair

Two digits with no other characters (e.g. `12`) is parsed as two simultaneous
taps — equivalent to `1/2`. Only works for exactly two digits; for three or
more, use `/`.

### Fake each

Backticks `` ` `` separate notes that share a single comma but are placed at
slightly offset times (128th-note intervals), creating a roll effect:

```
1`2`3`4,
```

Each segment is placed at an offset of `1.875 / bpm` seconds after the
previous one.

---

## 4. Duration format

Durations appear inside `[...]` for holds and slides.

### Hold durations

| Form | Meaning |
|------|---------|
| `[divide:count]` | `time = (60 / bpm) * 4 / divide * count` seconds. |
| `[#seconds]` | Absolute seconds (hold only). |
| `[bpm#divide:count]` | Use a custom BPM for the ratio calculation. |

Examples:
```
1h[4:1]       one beat at current BPM
1h[#2.5]      2.5 seconds
1h[120#4:1]   one beat at BPM 120
```

### Slide durations

Slides require a duration and support additional forms that set the **wait
time** (the delay between the star tap and when the slide begins moving).

| Form | Meaning |
|------|---------|
| `[divide:count]` | Slide time from ratio at current BPM. Wait = one beat. |
| `[bpm#divide:count]` | Slide time from ratio at custom BPM. Wait = one beat at custom BPM. |
| `[bpm#seconds]` | Slide time = absolute seconds. Wait = one beat at custom BPM. |
| `[wait##seconds]` | Wait = `wait` seconds (per-beat). Slide time = absolute seconds. |
| `[wait##divide:count]` | Wait = `wait` seconds. Slide time from ratio at current BPM. |
| `[wait##bpm#divide:count]` | Wait = `wait` seconds. Slide time from ratio at custom BPM. |

In the `##` forms, `wait` is the seconds-per-beat value used to derive the
wait time. The slide wait time equals one beat at `60 / wait` BPM.

Examples:
```
1-4[8:3]              3 eighth-notes at current BPM
1-4[160#8:3]          3 eighth-notes at BPM 160
1-4[160#2.0]          2.0 seconds at BPM 160 (wait)
1-4[3.0##1.5]         wait 3s, slide 1.5s
1-4[3.0##4:1]         wait 3s, slide one beat at current BPM
1-4[3.0##160#4:1]     wait 3s, slide one beat at BPM 160
```

### Ratio calculation

`[divide:count]` computes: `time = (60 / bpm) * 4 / divide * count`

- `divide` must be a positive integer.
- `count` must be a non-negative integer.
- Common values: `4:1` = one beat, `8:1` = half beat, `8:3` = 1.5 beats,
  `16:1` = quarter beat.