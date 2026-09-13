# Issue #164 — small Song Select lists

## Goal

Stop the fixed 13-row Song Select presentation from visually repeating songs and `RANDOM SELECT` when the current logical list contains fewer rows than the viewport.

## Scope

- Keep existing circular/infinite keyboard navigation and selection semantics.
- For lists with fewer than 13 nodes, render every logical node at most once around the selected center row and leave unused bar slots empty.
- Keep the existing 13-row circular projection for lists that fill or exceed the viewport.
- Use the same projection for visible texture pre-generation so rendering and cache work agree.
- Add focused pure unit tests for 1-node, 4-node, wrapped, and full-viewport cases.

## Out of scope

No song hierarchy/model redesign, pack-to-difficulty restructuring, preview-audio indicator, navigation behavior change, or new rendering framework.
