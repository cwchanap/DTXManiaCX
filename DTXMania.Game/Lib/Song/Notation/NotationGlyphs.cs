using System;
using System.Collections.Generic;

namespace DTXMania.Game.Lib.Song.Notation
{
    /// <summary>
    /// The SMuFL glyphs the drum notation view draws, restricted to the 32 characters baked into
    /// Content/Bravura-Notation.spritefont (Bravura 1.482). Enum values are the exact SMuFL code
    /// points, so the glyph → PUA char mapping is total and <see cref="NotationGlyphs.ToChar"/>
    /// is a plain cast.
    /// </summary>
    internal enum NotationGlyph
    {
        // Percussion clef
        UnpitchedPercussionClef1 = 0xE069,

        // Time signature digits
        TimeSig0 = 0xE080,
        TimeSig1 = 0xE081,
        TimeSig2 = 0xE082,
        TimeSig3 = 0xE083,
        TimeSig4 = 0xE084,
        TimeSig5 = 0xE085,
        TimeSig6 = 0xE086,
        TimeSig7 = 0xE087,
        TimeSig8 = 0xE088,
        TimeSig9 = 0xE089,

        // Noteheads: open whole/half and black variants for the three NoteheadShape families
        NoteheadWhole = 0xE0A2,
        NoteheadHalf = 0xE0A3,
        NoteheadBlack = 0xE0A4,
        NoteheadXWhole = 0xE0A7,
        NoteheadXHalf = 0xE0A8,
        NoteheadXBlack = 0xE0A9,
        NoteheadCircleXWhole = 0xE0B1,
        NoteheadCircleXHalf = 0xE0B2,
        NoteheadCircleX = 0xE0B3,

        // Stem-up flags, eighth through 64th
        Flag8thUp = 0xE240,
        Flag16thUp = 0xE242,
        Flag32ndUp = 0xE244,
        Flag64thUp = 0xE246,

        // Rests, whole through 64th
        RestWhole = 0xE4E3,
        RestHalf = 0xE4E4,
        RestQuarter = 0xE4E5,
        Rest8th = 0xE4E6,
        Rest16th = 0xE4E7,
        Rest32nd = 0xE4E8,
        Rest64th = 0xE4E9,

        // Triplet numeral for tuplet display
        Tuplet3 = 0xE883
    }

    /// <summary>
    /// Closed rhythmic duration ladder matching DTXWeb's tick table. <see cref="NotationGlyphs.FromTicks"/>
    /// maps notation ticks onto this ladder; the quantizer's DurationTicks are never rewritten.
    /// </summary>
    internal enum NoteValue
    {
        /// <summary>192 ticks.</summary>
        Whole,

        /// <summary>96 ticks.</summary>
        Half,

        /// <summary>48 ticks.</summary>
        Quarter,

        /// <summary>24 ticks.</summary>
        Eighth,

        /// <summary>12 ticks.</summary>
        Sixteenth,

        /// <summary>6 ticks.</summary>
        ThirtySecond,

        /// <summary>3 ticks.</summary>
        SixtyFourth
    }

    /// <summary>
    /// One staff-space coordinate from Bravura's glyph metadata (a bBox corner or stem anchor).
    /// </summary>
    /// <param name="X">Horizontal offset in staff spaces from the glyph origin.</param>
    /// <param name="Y">Vertical offset in staff spaces from the glyph baseline (positive is up).</param>
    internal readonly record struct GlyphPoint(float X, float Y);

    /// <summary>
    /// Glyph geometry the notation renderer consumes, copied verbatim from the pinned Bravura metadata.
    /// </summary>
    /// <param name="Width">bBox width in staff spaces (bBoxNE.x - bBoxSW.x).</param>
    /// <param name="Origin">bBox south-west corner relative to the glyph origin (bBoxSW).</param>
    /// <param name="StemUpSE">
    /// stemUpSE attachment where a stem-up meets the glyph's south-east edge; null when the glyph has
    /// no up-stem anchor (whole noteheads carry no stem, non-noteheads never do).
    /// </param>
    /// <param name="StemUpNW">
    /// stemUpNW attachment where a stem-up meets the glyph's north-west edge; only flag glyphs expose it.
    /// </param>
    internal readonly record struct NotationGlyphMetrics(
        float Width,
        GlyphPoint Origin,
        GlyphPoint? StemUpSE,
        GlyphPoint? StemUpNW);

    /// <summary>
    /// Pure glyph contract for the notation view: glyph → PUA char mapping, duration →
    /// value/glyph selection and Bravura metadata. GPU-free by design — no IFont, no
    /// Lib.Resources, no Microsoft.Xna.Framework types; resource consumers pass
    /// <c>font.SupportsCharacter</c> as a plain predicate.
    /// Expected load/validate use (HPA-410/HPA-411): acquire the font with
    /// IResourceManager.LoadFont("Bravura-Notation", 40), call <see cref="NotationGlyphs.IsComplete"/>
    /// with the font's SupportsCharacter, and on false release the acquired reference and fail —
    /// LoadFont silently falls back to NotoSerifJP when the asset is missing or partial, which this
    /// check rejects.
    /// </summary>
    internal static class NotationGlyphs
    {
        // Glyph geometry copied from Bravura 1.482 (Steinberg's pinned bravura-1.482 release),
        // redist/Bravura.json: glyphBBoxes bBoxSW/bBoxNE and glyphsWithAnchors stemUpSE/stemUpNW,
        // all in staff spaces. Width = bBoxNE.x - bBoxSW.x; Origin = bBoxSW. Values are verbatim
        // metadata, not hand-tuned pixels; update them only from the same pinned release.
        private static readonly IReadOnlyDictionary<NotationGlyph, NotationGlyphMetrics> BravuraMetrics = new Dictionary<NotationGlyph, NotationGlyphMetrics>
        {
            [NotationGlyph.UnpitchedPercussionClef1] = new(1.528f, new GlyphPoint(0f, -1f), null, null),

            [NotationGlyph.TimeSig0] = new(1.72f, new GlyphPoint(0.08f, -1f), null, null),
            [NotationGlyph.TimeSig1] = new(1.176f, new GlyphPoint(0.08f, -1f), null, null),
            [NotationGlyph.TimeSig2] = new(1.624f, new GlyphPoint(0.08f, -1.028f), null, null),
            [NotationGlyph.TimeSig3] = new(1.524f, new GlyphPoint(0.08f, -1.004f), null, null),
            [NotationGlyph.TimeSig4] = new(1.72f, new GlyphPoint(0.08f, -1f), null, null),
            [NotationGlyph.TimeSig5] = new(1.452f, new GlyphPoint(0.08f, -1.004f), null, null),
            [NotationGlyph.TimeSig6] = new(1.576f, new GlyphPoint(0.08f, -0.996f), null, null),
            [NotationGlyph.TimeSig7] = new(1.604f, new GlyphPoint(0.08f, -1f), null, null),
            [NotationGlyph.TimeSig8] = new(1.584f, new GlyphPoint(0.08f, -1.036f), null, null),
            [NotationGlyph.TimeSig9] = new(1.576f, new GlyphPoint(0.08f, -0.996f), null, null),

            [NotationGlyph.NoteheadWhole] = new(1.688f, new GlyphPoint(0f, -0.5f), null, null),
            [NotationGlyph.NoteheadHalf] = new(1.18f, new GlyphPoint(0f, -0.5f), new GlyphPoint(1.18f, 0.168f), null),
            [NotationGlyph.NoteheadBlack] = new(1.18f, new GlyphPoint(0f, -0.5f), new GlyphPoint(1.18f, 0.168f), null),
            [NotationGlyph.NoteheadXWhole] = new(1.508f, new GlyphPoint(0f, -0.5f), null, null),
            [NotationGlyph.NoteheadXHalf] = new(1.336f, new GlyphPoint(0f, -0.5f), new GlyphPoint(1.336f, 0.412f), null),
            [NotationGlyph.NoteheadXBlack] = new(1.16f, new GlyphPoint(0f, -0.5f), new GlyphPoint(1.16f, 0.444f), null),
            [NotationGlyph.NoteheadCircleXWhole] = new(0.996f, new GlyphPoint(0f, -0.5f), null, null),
            [NotationGlyph.NoteheadCircleXHalf] = new(1f, new GlyphPoint(0f, -0.5f), new GlyphPoint(1f, 0f), null),
            [NotationGlyph.NoteheadCircleX] = new(0.996f, new GlyphPoint(0f, -0.5f), new GlyphPoint(0.996f, 0f), null),

            [NotationGlyph.Flag8thUp] = new(1.056f, new GlyphPoint(0f, -3.24f), null, new GlyphPoint(0f, -0.04f)),
            [NotationGlyph.Flag16thUp] = new(1.116f, new GlyphPoint(0f, -3.252f), null, new GlyphPoint(0f, -0.088f)),
            [NotationGlyph.Flag32ndUp] = new(1.044f, new GlyphPoint(0f, -3.248f), null, new GlyphPoint(0f, 0.376f)),
            [NotationGlyph.Flag64thUp] = new(1.044f, new GlyphPoint(0f, -3.248f), null, new GlyphPoint(0f, 1.172f)),

            [NotationGlyph.RestWhole] = new(1.128f, new GlyphPoint(0f, -0.54f), null, null),
            [NotationGlyph.RestHalf] = new(1.128f, new GlyphPoint(0f, -0.008f), null, null),
            [NotationGlyph.RestQuarter] = new(1.076f, new GlyphPoint(0.004f, -1.5f), null, null),
            [NotationGlyph.Rest8th] = new(0.988f, new GlyphPoint(0f, -1.004f), null, null),
            [NotationGlyph.Rest16th] = new(1.28f, new GlyphPoint(0f, -2f), null, null),
            [NotationGlyph.Rest32nd] = new(1.452f, new GlyphPoint(0f, -2f), null, null),
            [NotationGlyph.Rest64th] = new(1.692f, new GlyphPoint(0f, -3.012f), null, null),

            [NotationGlyph.Tuplet3] = new(1.184f, new GlyphPoint(0.04f, -0.032f), null, null)
        };

        /// <summary>Converts a glyph to the PUA character baked into Bravura-Notation.spritefont.</summary>
        public static char ToChar(NotationGlyph glyph) => (char)glyph;

        /// <summary>
        /// Returns the glyph's Bravura metadata (bBox width/origin in staff spaces plus stem
        /// attachment anchors where the glyph has them).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The glyph has no metadata entry.</exception>
        public static NotationGlyphMetrics GetMetrics(NotationGlyph glyph) =>
            BravuraMetrics.TryGetValue(glyph, out var metrics)
                ? metrics
                : throw new ArgumentOutOfRangeException(nameof(glyph), glyph, "No Bravura metadata for the requested glyph.");

        /// <summary>
        /// Picks the notehead glyph for a staff shape and duration: whole and half use the open
        /// head of the shape (E0A2/E0A3, E0A7/E0A8, E0B1/E0B2); quarter and shorter use the black
        /// variants (E0A4, E0A9, E0B3).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The shape is not a <see cref="NoteheadShape"/>.</exception>
        public static NotationGlyph Notehead(NoteheadShape shape, NoteValue value) => (shape, value) switch
        {
            (NoteheadShape.Normal, NoteValue.Whole) => NotationGlyph.NoteheadWhole,
            (NoteheadShape.Normal, NoteValue.Half) => NotationGlyph.NoteheadHalf,
            (NoteheadShape.X, NoteValue.Whole) => NotationGlyph.NoteheadXWhole,
            (NoteheadShape.X, NoteValue.Half) => NotationGlyph.NoteheadXHalf,
            (NoteheadShape.CircledX, NoteValue.Whole) => NotationGlyph.NoteheadCircleXWhole,
            (NoteheadShape.CircledX, NoteValue.Half) => NotationGlyph.NoteheadCircleXHalf,
            (NoteheadShape.Normal, _) => NotationGlyph.NoteheadBlack,
            (NoteheadShape.X, _) => NotationGlyph.NoteheadXBlack,
            (NoteheadShape.CircledX, _) => NotationGlyph.NoteheadCircleX,
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown notehead shape.")
        };

        /// <summary>Returns the rest glyph matching the duration (E4E3 through E4E9).</summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a <see cref="NoteValue"/>.</exception>
        public static NotationGlyph Rest(NoteValue value) => value switch
        {
            NoteValue.Whole => NotationGlyph.RestWhole,
            NoteValue.Half => NotationGlyph.RestHalf,
            NoteValue.Quarter => NotationGlyph.RestQuarter,
            NoteValue.Eighth => NotationGlyph.Rest8th,
            NoteValue.Sixteenth => NotationGlyph.Rest16th,
            NoteValue.ThirtySecond => NotationGlyph.Rest32nd,
            NoteValue.SixtyFourth => NotationGlyph.Rest64th,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown note value.")
        };

        /// <summary>
        /// Returns the stem-up flag glyph for the duration, or null for whole/half/quarter: flags
        /// exist only from the eighth note up (E240/E242/E244/E246).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a <see cref="NoteValue"/>.</exception>
        public static NotationGlyph? Flag(NoteValue value) => value switch
        {
            NoteValue.Eighth => NotationGlyph.Flag8thUp,
            NoteValue.Sixteenth => NotationGlyph.Flag16thUp,
            NoteValue.ThirtySecond => NotationGlyph.Flag32ndUp,
            NoteValue.SixtyFourth => NotationGlyph.Flag64thUp,
            NoteValue.Whole or NoteValue.Half or NoteValue.Quarter => null,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown note value.")
        };

        /// <summary>
        /// True when every required notation glyph is available. The enum members are exactly the
        /// required set, so each is probed once. Pure: the caller supplies the predicate (normally
        /// <c>font.SupportsCharacter</c> after the character-cache fix); this helper never reads
        /// SpriteFont.Characters itself.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="hasGlyph"/> is null.</exception>
        public static bool IsComplete(Func<char, bool> hasGlyph)
        {
            if (hasGlyph == null)
            {
                throw new ArgumentNullException(nameof(hasGlyph));
            }

            foreach (NotationGlyph glyph in Enum.GetValues<NotationGlyph>())
            {
                if (!hasGlyph(ToChar(glyph)))
                {
                    return false;
                }
            }

            return true;
        }

        extension(NoteValue)
        {
            /// <summary>
            /// Maps notation ticks to the longest <see cref="NoteValue"/> that fits, following
            /// DTXWeb's descending table (192/96/48/24/12/6/3): the greatest supported tick count
            /// ≤ ticks wins, and anything below 3 (1–2 tick fallbacks, 5-tick folded rests) lands
            /// on <see cref="NoteValue.SixtyFourth"/>.
            /// Tuplet display: the renderer derives the displayed base as SlotTicks * 3 / 2 BEFORE
            /// calling this method (DTXWeb rule), so a 48-tick triplet slot shows as a 72-tick quarter.
            /// </summary>
            /// <exception cref="ArgumentOutOfRangeException">Ticks is not positive.</exception>
            public static NoteValue FromTicks(int ticks)
            {
                if (ticks <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Duration ticks must be positive.");
                }

                return ticks switch
                {
                    >= 192 => NoteValue.Whole,
                    >= 96 => NoteValue.Half,
                    >= 48 => NoteValue.Quarter,
                    >= 24 => NoteValue.Eighth,
                    >= 12 => NoteValue.Sixteenth,
                    >= 6 => NoteValue.ThirtySecond,
                    _ => NoteValue.SixtyFourth
                };
            }
        }
    }
}
