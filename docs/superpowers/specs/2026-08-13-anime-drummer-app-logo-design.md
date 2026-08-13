# Anime Drummer App Logo Design

## Goal

Replace the existing DTXManiaCX application icon with a distinctive anime-style drum-game identity that remains recognizable at desktop app-icon sizes.

## Chosen Direction

Use **Style 1: Anime Drummer Mascot** from the approved concept sheet.

The mascot should read as a **late-teen / young-adult drummer** (roughly high-school/university age), not a child. The expression should be confident, upbeat, and energetic rather than overly cute or chibi.

## Visual Design

- Square composition suitable for Windows/macOS application icons.
- Female anime drummer centered behind a compact red/black acoustic drum kit.
- Dynamic performance pose with visible drumsticks and cymbals.
- Dark concert-stage background with red, magenta, and violet lighting accents.
- Strong silhouette and face/drum contrast so the subject survives downscaling.
- Avoid excessive background detail, tiny accessories, or fine text that disappears below 64 px.
- Branding should use the project name **DTXManiaCX**. Large-source artwork may include a compact DTX/CX mark, but small icon readability takes priority over full wordmark visibility.

## Repository Integration

Keep the implementation intentionally small:

- Replace `DTXMania.Game/Icon.bmp`.
- Replace `DTXMania.Game/Icon.ico`.
- Preserve the existing project references and filenames so no runtime or project-file changes are required unless validation proves otherwise.
- Do not add a branding subsystem, splash screen, UI changes, or unrelated art assets in this PR.

## Asset Requirements

- Generate a high-resolution square master artwork.
- Derive the existing BMP and ICO deliverables from the same master so their appearance stays consistent.
- ICO should contain practical desktop icon sizes including 16, 32, 48, 64, 128, and 256 px where supported by the conversion toolchain.
- Ensure the artwork remains identifiable at 32 px and acceptable at 16 px even if fine facial details are lost.

## Validation

- Confirm both files can be opened by standard image tooling after conversion.
- Confirm ICO contains multiple resolutions.
- Build the affected game project(s) or solution sufficiently to verify the icon assets do not break packaging/project loading.
- Inspect 256, 64, and 32 px renders for clipping and legibility.

## Out of Scope

- In-game logo/title-screen redesign.
- Installer artwork or store listing assets.
- Animated logo/video assets.
- DTXCreator branding.
