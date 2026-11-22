# HPA-800 Native Verification

Baseline: `50f46439993ac3d2d852092c994ef1a0d8c94bfa` (production
matches `241d78d8ed040a3d7d69f01562462b5ad57ad7ab`).
Implementation tested: `b6a740a959562f2a4f5daf578cfa5289bef10f74`.
Platform: macOS 26.6.2, Apple Silicon, .NET 10.0.100.

| Mode | Baseline | Implementation |
| --- | --- | --- |
| Lanes | [Screenshot](baseline-lanes.png) | [Screenshot](implementation-lanes.png) |
| Tab | [Screenshot](https://github.com/cwchanap/DTXManiaCX/blob/5e8b47e311badceafc8942b804a255f0527f9463/docs/evidence/hpa-800/baseline-tab.png) | [Screenshot](https://github.com/cwchanap/DTXManiaCX/blob/5e8b47e311badceafc8942b804a255f0527f9463/docs/evidence/hpa-800/implementation-tab.png) |

The Tab pair links to the historical capture commit. Raw test logs and the
field-filtered MCP transcript are no longer kept in the current tree; the
verified counts and limitations are recorded below.

The 1280x720 captures show matching HUD, staff/lane, overlay and BGA
composition. Note positions and hit-flash intensity differ with capture time.
Baseline and implementation Result screenshots were byte-identical.

- **Unit output**: 9,575 passed, 58 skipped, 0 failed.
- **Native E2E output**: 12 passed, 0 skipped, including
  Performance Back to SongSelect, normal Result, simulated MIDI and Tab.
- **MCP observations**: selected, field-filtered records
  from the real MCP transcript, including Lanes/Tab screenshots, Result and
  Performance-to-SongSelect Back. The suite drove the game through JSON-RPC;
  MCP observed and captured it. The shell ran the test commands.

The native game used the null audio driver and reported silent GameTime clock
fallback in gameplay. These results do not establish audible playback or audio
stopping. No physical input/MIDI/audio hardware or Windows-native run was
tested; the 58 unit skips include device-dependent tests. A static frame
does not prove continuous video playback or every animation state.
