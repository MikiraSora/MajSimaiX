---
name: simai-skill
description: Read, write, inspect, and fix simai / maidata 谱面 chart syntax for the MajSimaiX parser. Covers metadata (&title, &artist, &first, &des, &lv, &inote), timing ((bpm), {n}, {#seconds}, commas), every note type (tap, break, EX, mine, force star, hold, slide, touch, touch hold, each), slide marks, durations, HS/Soflan, and FixedSoflan. Use this skill when writing or editing simai note lines, fixing maidata parse errors, or verifying MajSimaiX parser compatibility.
---

# Simai Skill

This skill helps read, write, inspect, and fix simai / maidata chart syntax as
understood by the **MajSimaiX** parser (`Assets/Plugins/MajSimaiX`).

## When to use

Use this skill when you need to:

- Write or edit simai note lines, each groups, holds, or slides inside a `&inote_n=` block.
- Fix simai parse errors or invalid syntax in a maidata file.
- Write or correct maidata metadata (`&title`, `&artist`, `&first`, `&des`, `&lv_n`, `&inote_n`).
- Understand how a specific simai construct is parsed by MajSimaiX.

## Workflow

1. If the task involves writing or fixing simai notes, timing, metadata, or
   durations, read `references/simai-syntax.md` for the grammar and examples.
2. If the task involves MajSimaiX-specific extensions (HS/Soflan, FixedSoflan
   `@`, force star `$`, wifi slides, same-head slides, no-head slides), or you
   need to know what the parser actually accepts versus standard simai, read
   `references/majsimaix-parser-notes.md`.
3. Always prefer syntax that the MajSimaiX parser accepts. Standard simai wiki
   syntax may include forms MajSimaiX does not support (for example the `E` EOF
   flag is not implemented).

## References

- [simai-syntax.md](references/simai-syntax.md) — Core simai grammar: metadata,
  timing, notes, durations, each notes.
- [majsimaix-parser-notes.md](references/majsimaix-parser-notes.md) —
  MajSimaiX parser specifics: slide marks, same-head / conn / no-head slides,
  HS/Soflan, FixedSoflan, and differences from standard simai.