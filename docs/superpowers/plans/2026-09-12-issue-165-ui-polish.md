# Issue #165 — UI polish

## Goal

Fix the three polish regressions reported in #165 without redesigning the affected screens.

## Scope

One PR for GitHub issue #165.

- **Version text:** use the game assembly version on Startup and Title instead of a hardcoded `v1.0.0`. Release jobs already export `APP_VERSION`; map that value to the SDK `Version` in `Directory.Build.props` so shipped binaries match the release tag.
- **Config spacing:** move the existing value column 10 px right and reduce its maximum width by the same amount. Keep the current two-column layout and description-panel boundary.
- **Result metadata:** keep the existing right-side metadata row, but shorten the score-bucket copy to `SCORE BUCKET: <speed> · ALL PITCHES` so its meaning remains clear without clipping at 1280x720.

## Tests

- Pin Title version rendering to the current game assembly version.
- Update Config layout assertions for the adjusted value column.
- Update Result model/renderer assertions for the compact score-bucket copy.
- Run the normal Windows/macOS CI suite.

## Out of scope

No new versioning framework, Config layout redesign, Result panel reflow/wrapping system, or unrelated UI cleanup.
