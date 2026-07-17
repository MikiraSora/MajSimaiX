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
| Mine + group 12 | `!m#12` | `#12!m` |
| Mine + fixed speed 600 | `!m#F600` | `#F600!m` |
| Mine + group 12 + fixed speed 600 | `!m#12F600` | `#12F600!m` |

The exporter writes `!m` before `#groupFspeed`, but readers must treat the
modifiers as order-independent. To parse the field:

1. Detect and remove one exact lowercase `!m`.
2. Parse the remaining `#groupFspeed` with the existing Soflan rules.
3. Reject duplicate `!m` or duplicate `#` markers.

Do not infer Mine from the MA2 note ID. `NM` continues to mean normal, not
Mine.

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

## Soflan And Statistics

- Missing group means group `0` and emits no `#` unless FixedSoflan is set.
- `#F` means group `0` with the default fixed speed.
- `#12F600` means group `12` with fixed speed `600`.
- Mine does not create a new MA2 note ID.
- Mine markers do not change `T_REC_*`, `T_NUM_*`, `T_JUDGE_*`, or
  `TTM_SCR_*` output.

Examples:

```text
NMTAP  0  0  0  !m
NMTAP  0  0  0  !m#12F600
NMSTR  0  0  0  !m
NMSI_  0  0  0  96  384  3  !m
```
