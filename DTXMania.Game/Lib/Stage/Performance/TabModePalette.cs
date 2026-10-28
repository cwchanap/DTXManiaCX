#nullable enable

using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Tab-mode colors resolved once from the active skin's Theme.ini, shared by the
    /// notation strip and the Tab HUD. Every key is optional: the shared UI.* ramp
    /// and accents fall back to the CX Neon values, and the judgement ramp reuses the
    /// result screen's Result.*Text ledger so a hit reads the same color on both
    /// screens. Surfaces are fixed dark navy tones so notation stays legible over any BGA.
    /// </summary>
    public sealed class TabModePalette
    {
        public static readonly TabModePalette Default = Resolve(SkinTheme.Empty);

        public Color TextPrimary { get; private init; }
        public Color TextSecondary { get; private init; }
        public Color TextTertiary { get; private init; }
        public Color TextFaint { get; private init; }
        public Color Accent { get; private init; }
        public Color AccentSecondary { get; private init; }
        public Color Success { get; private init; }
        public Color Danger { get; private init; }

        public Color Perfect { get; private init; }
        public Color Great { get; private init; }
        public Color Good { get; private init; }
        public Color Poor { get; private init; }
        public Color Miss { get; private init; }

        // Fixed surfaces: translucent HUD panels over the backdrop, an opaque strip so
        // a bright BGA never washes out the notation, and a slightly raised key column.
        public Color Panel { get; } = new Color(0x0B, 0x12, 0x20) * 0.86f;
        public Color StripBackground { get; } = new Color(0x09, 0x0F, 0x1C);
        public Color KeyColumn { get; } = new Color(0x10, 0x19, 0x2B);

        // Danger-zone gauge midpoint, sampled from CX Neon's 7_gauge_bar.png.
        public Color Warning { get; } = new Color(0xF9, 0x9C, 0x16);

        public static TabModePalette Resolve(ISkinTheme? theme)
        {
            theme ??= SkinTheme.Empty;
            return new TabModePalette
            {
                TextPrimary = theme.GetColor("UI.TextPrimary", new Color(0xDB, 0xEB, 0xF0)),
                TextSecondary = theme.GetColor("UI.TextSecondary", new Color(0xA4, 0xBE, 0xC5)),
                TextTertiary = theme.GetColor("UI.TextTertiary", new Color(0x7A, 0x99, 0xA1)),
                TextFaint = theme.GetColor("UI.TextFaint", new Color(0x64, 0x7E, 0x85)),
                Accent = theme.GetColor("UI.Accent", new Color(0x22, 0xD3, 0xEE)),
                AccentSecondary = theme.GetColor("UI.AccentSecondary", new Color(0xE8, 0x79, 0xF9)),
                Success = theme.GetColor("UI.Success", new Color(0x2B, 0xD1, 0x8F)),
                Danger = theme.GetColor("UI.Danger", new Color(0xE9, 0x45, 0x3A)),
                Perfect = theme.GetColor("Result.PerfectText", new Color(0xF7, 0xCA, 0x41)),
                Great = theme.GetColor("Result.GreatText", new Color(0x2B, 0xD1, 0x8F)),
                Good = theme.GetColor("Result.GoodText", new Color(0x71, 0xA7, 0xE6)),
                Poor = theme.GetColor("Result.PoorText", new Color(0xDF, 0x5A, 0xE4)),
                Miss = theme.GetColor("Result.MissText", new Color(0xE9, 0x45, 0x3A)),
            };
        }

        public Color ForJudgement(JudgementType type) => type switch
        {
            JudgementType.Perfect => Perfect,
            JudgementType.Great => Great,
            JudgementType.Good => Good,
            JudgementType.Poor => Poor,
            _ => Miss,
        };

        /// <summary>
        /// LIFE segment color by its position along the gauge (0..1), mirroring the
        /// skin's gauge bar: red→amber through the danger quarter, then accent→success.
        /// </summary>
        public Color GaugeSegment(float position)
        {
            const float dangerZone = 0.25f;
            if (position < dangerZone)
                return Color.Lerp(Danger, Warning, position / dangerZone);
            return Color.Lerp(Accent, Success, (position - dangerZone) / (1f - dangerZone));
        }
    }
}
