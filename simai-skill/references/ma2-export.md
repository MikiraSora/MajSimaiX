# MajdataEdit MA2 Export Reference

Use this reference for MajdataEdit's simai-to-MA2 conversion contract. These
rules describe exporter extensions, not MajSimaiX input grammar or standard
MA2.

## Note Tail Field

MajdataEdit keeps the original MA2 note ID and writes optional modifiers in
one final tab-separated field.

| Meaning | Canonical output | Equivalent input |
| --- | --- | --- |
| Mine | `!m` | `!m` |
| Force Yellow | `!y` | `!y` |
| Force Yellow + group 12 | `!y#12` | `#12!y` |
| Force Yellow + fixed speed 600 | `!y#F600` | `#F600!y` |
| Mine + group 12 | `!m#12` | `#12!m` |
| Mine + fixed speed 600 | `!m#F600` | `#F600!m` |
| Mine + group 12 + fixed speed 600 | `!m#12F600` | `#12F600!m` |

The exporter writes private flags before `#groupFspeed`, in canonical order
`!m`, `!y`, then `#`. Mine and Force Yellow are mutually exclusive, so valid
output never contains both flags. Readers that implement this extension should
treat the modifiers as order-independent. To parse the field:

1. Detect and remove at most one exact lowercase `!m` and at most one exact
   lowercase `!y`.
2. Parse the remaining `#groupFspeed` with the existing Soflan rules.
3. Reject duplicate private flags, duplicate `#` markers, or `!m` together
   with `!y`.

Do not infer Mine or Force Yellow from the MA2 note ID. `NM` continues to mean
normal. These tails are Majdata private extensions; no behaviour is promised
for external MA2 readers that do not implement them.

## Mine Mapping

| Generated record | Source flag |
| --- | --- |
| Tap, Hold, Touch, TouchHold | `IsMine` |
| Slide `STR` head | `IsMine` |
| Slide/Wifi body records | `IsMineSlide` |
| No-head Slide | Body only, using `IsMineSlide` |

`IsMine` does not mark the Slide body, and `IsMineSlide` does not mark the
head. Same-head branches remain independent. A connected Slide is one
`SimaiNote`, so all MA2 body segments generated from it share
`IsMineSlide`.

## Force Yellow Mapping

| Generated record | Source value |
| --- | --- |
| Tap, Hold, Touch, TouchHold | `IsForceYellow` |
| Slide `STR` head | `IsForceYellow` |
| Slide/Wifi body segment | Its zero-based index is present in `ForceYellowSlideSegmentIndices` |
| No-head Slide | Body segments only; a hidden-head `IsForceYellow` has no record |

Connected Slide path flags are emitted per generated body record; they do not
spread to later segments. Same-head branches remain independent. A natural
EACH normalization clears a redundant `IsForceYellow` before export, while
path indices remain available.

## Soflan And Statistics

- Missing group means group `0` and emits no `#` unless FixedSoflan is set.
- Slide star-head records use `SoflanGroup`; Slide/Wifi body records use `SlideSoflanGroup`. They are normally equal, but `<HSg>(1)-3[...]` assigns only the star head to group `g` and leaves the body in group `0`.
- `#F` means group `0` with the default fixed speed.
- `#12F600` means group `12` with fixed speed `600`.
- Mine does not create a new MA2 note ID.
- Mine and Force Yellow markers do not change `T_REC_*`, `T_NUM_*`,
  `T_JUDGE_*`, `TTM_EACHPAIRS`, or
  `TTM_SCR_*` output.

Examples:

```text
NMTAP  0  0  0  !m
NMTAP  0  0  0  !m#12F600
NMTAP  0  0  0  !y
NMTAP  0  0  0  !y#12F600
NMSTR  0  0  0  !m
NMSTR  0  0  0  !y
NMSI_  0  0  0  96  384  3  !m
NMSI_  0  0  0  96  384  3  !y
```
