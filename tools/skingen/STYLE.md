# CX Neon — Art Style Guide

Every generated image must follow this style so the pack reads as one system.
The `prompts` command bakes the **base style prompt** below into every asset
prompt; keep edits synchronized with `descriptors.json`.

## Identity

Neon dark / synthwave arcade. Dark slate surfaces, glowing cyan/magenta
accents, thin grid and scanline motifs, chamfered panel corners, soft outer
glow on highlighted elements. Confident arcade energy — never pastel, never
photorealistic, never Gitadora-glossy-metallic.

## Tokens

| Token | Value |
|---|---|
| Background base | `#0F172A` |
| Panel surface | `#1E293B` |
| Primary accent (cyan) | `#22D3EE` |
| Secondary accent (magenta) | `#E879F9` |
| Success | `#22C55E` |
| Danger | `#EF4444` |
| Text primary | `#F1F5F9` |

## Base style prompt (prepended to every asset prompt)

> Synthwave arcade game UI asset, dark slate background #0F172A, neon cyan
> #22D3EE and magenta #E879F9 accents, thin glowing grid lines and subtle
> scanlines, chamfered corners, soft neon outer glow, crisp vector-like edges,
> flat dark surfaces (#1E293B), high contrast, no text, no watermark, no logo.

## Rules for generation

- Generate at 2x the target size or larger; `skingen compose` downsizes.
- Assets marked TRANSPARENT need an alpha background (or a solid removable
  black background if the generator cannot output alpha).
- Sprite-sheet *cells* are generated as individual images; `compose` assembles
  the sheet. Never ask the generator to lay out a grid itself.
- Digits/glyphs: geometric, Orbitron-like letterforms, uniform stroke weight,
  filled with `#F1F5F9` and a cyan outer glow.
- Per-lane effect variants are NOT generated: one master is generated and
  `compose` hue-shifts it (see recipes in `manifest.json`).
