#nullable enable

using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.UI.Layout
{
    /// <summary>
    /// Layout constants for the Drum Key Mapping page (DrumConfigStage), in the 1280x720
    /// virtual space. The page is four chamfered panels over the backdrop: a header with the
    /// title, MIDI status and Reset, the kit on the left, the 10-row bindings list on the
    /// right, and a footer of key hints. Text is ASCII-only (the Orbitron and Share Tech Mono
    /// sprite fonts bake 32..126). Colors come from <see cref="SkinPalette"/>; lane colors
    /// from <see cref="PerformanceUILayout.GetLaneColor"/>.
    /// </summary>
    public static class DrumConfigUILayout
    {
        /// <summary>Short lane codes in gameplay lane order (0..9).</summary>
        public static readonly string[] LaneCodes =
        {
            "LC", "HH", "LP", "SN", "HT", "BD", "LT", "FT", "CY", "RD"
        };

        public const int LaneCount = 10;

        // ---- Fonts ----
        public const string DisplayFontFamily = "Orbitron";
        public const string MonoFontFamily = "ShareTechMono";
        public const int TitleFontSize = 18;
        public const int LabelFontSize = 10;
        public const int MonoFontSize = 11;
        /// <summary>CJK-capable face for MIDI device names the Latin-only mono face cannot draw.</summary>
        public const string SerifFontFamily = "NotoSerifJP";
        public const int SerifFontSize = 14;
        /// <summary>Scale for small mono text (chip KEY/MIDI prefixes and VEL tags).</summary>
        public const float SmallTextScale = 0.8f;

        // ---- Shared panel look ----
        public const int PanelChamfer = 8;
        public const int RowChamfer = 6;
        public const int ChipChamfer = 4;
        public const int ControlChamfer = 2;
        public const float PanelBorderAlpha = 0.45f;
        /// <summary>Dark wash over the backdrop so the panels sit on a calmer field.</summary>
        public static readonly Color BackdropScrim = new Color(5, 9, 18) * 0.3f;
        /// <summary>Opaque chip fill, a step lighter than the row.</summary>
        public static readonly Color ChipFill = new Color(0x16, 0x22, 0x3A);
        public static readonly Color FocusedChipFill = new Color(0x12, 0x30, 0x49);
        public static readonly Color SelectedRowFill = new Color(0x0D, 0x25, 0x34);
        public static readonly Color TagFill = new Color(0x0D, 0x15, 0x26);
        public static readonly Color SelectedTagFill = new Color(0x0F, 0x3A, 0x4A);
        public static readonly Color ControlFill = new Color(0x22, 0x30, 0x4A);
        public static readonly Color RemoveFill = new Color(0x3A, 0x1D, 0x27);

        // ---- Header ----
        public static readonly Rectangle HeaderPanel = new(16, 12, 1248, 60);
        public static readonly Vector2 TitlePosition = new(34, 17);
        public static readonly Vector2 SubtitlePosition = new(35, 47);
        public const string Title = "DRUM KEY MAPPING";
        public const string Subtitle = "Select a lane, press Enter, then hit the pad or key to bind it.";
        /// <summary>Right edge of the "MIDI" caption + status dot + device name group.</summary>
        public const int MidiGroupRight = 1040;
        public const int MidiGroupCenterY = 37;
        public const int MidiDeviceMaxWidth = 220;
        public const int StatusDotSize = 10;
        public static readonly Rectangle ResetButton = new(1060, 21, 204, 32);
        public const string ResetLabel = "RESET TO DEFAULTS";

        // ---- Caption pill (KIT / BINDINGS / MIDI) ----
        public const int CaptionHeight = 22;
        public const int CaptionPadX = 8;
        public const int CaptionChamfer = 4;

        // ---- Kit panel ----
        public static readonly Rectangle KitPanel = new(16, 84, 676, 576);
        public static readonly Point KitCaption = new(30, 98);
        public const string KitHint = "click a piece, then hit a pad or key";
        /// <summary>Where the kit illustration is drawn (its pieces' hotspots follow it).</summary>
        public static readonly Rectangle KitArt = new(24, 170, 660, 430);
        public const int PieceTagWidth = 40;
        public const int PieceTagHeight = 22;

        // ---- Bindings panel ----
        public static readonly Rectangle ListPanel = new(700, 84, 564, 576);
        public static readonly Point ListCaption = new(714, 98);
        public const string ListHint = "keys and MIDI notes per lane";
        public const int RowX = 712;
        public const int RowTop = 134;
        public const int RowPitch = 49;
        public const int RowWidth = 540;
        public const int RowHeight = 44;
        public static readonly Rectangle RowTag = new(14, 11, 40, 22);
        public const int RowNameOffsetX = 66;
        public const int ChipsOffsetX = 176;
        public const int ChipsOffsetY = 8;
        public const int ChipsRightInset = 10;
        public const int ChipHeight = 28;
        public const int ChipGap = 6;
        public const int ChipPadX = 10;
        public const int ChipPadRightWithControls = 6;
        public const int ChipPrefixGap = 6;
        public const int ChipVelocityGap = 8;
        public const int ControlSize = 16;
        public const int ControlGap = 3;
        public const int ControlLeadGap = 8;
        public const int RemoveLeadGap = 6;
        public const int ListeningDotSize = 8;
        public const int ListeningDotGap = 6;
        public const string ListeningLabel = "LISTENING...";
        public const int FlashTextRightInset = 14;
        public static readonly Vector2 StatusPosition = new(716, 628);

        // ---- Footer ----
        public static readonly Rectangle FooterPanel = new(16, 672, 1248, 36);
        public const int FooterChamfer = 6;
        public const int FooterPadLeft = 14;
        public const int KeycapHeight = 22;
        public const int KeycapMinWidth = 24;
        public const int KeycapPadX = 6;
        public const int KeycapGap = 4;
        public const int KeycapLabelGap = 3;
        public const int FooterGroupGap = 24;

        /// <summary>Footer hints while browsing: (keycaps, label).</summary>
        public static readonly (string[] Keys, string Label)[] BrowseHints =
        {
            (new[] { "Up", "Down" }, "lane"),
            (new[] { "Left", "Right" }, "binding"),
            (new[] { "Enter" }, "add"),
            (new[] { "Del" }, "remove"),
            (new[] { "-", "+" }, "velocity"),
            (new[] { "Esc" }, "save & exit"),
        };

        /// <summary>Footer hints while listening for a new binding.</summary>
        public static readonly (string[] Keys, string Label)[] ListeningHints =
        {
            (new[] { "Esc" }, "stop listening"),
        };

        // ---- Timing ----
        /// <summary>How long a live hit lights its row and piece.</summary>
        public const double HitFlashSeconds = 0.6;
        /// <summary>How long a status notice (added, removed, reserved key) stays up.</summary>
        public const double NoticeSeconds = 2.0;

        /// <summary>Screen rectangle of the bindings row for <paramref name="lane"/> (0..9).</summary>
        public static Rectangle GetRowRect(int lane) =>
            new(RowX, RowTop + (lane * RowPitch), RowWidth, RowHeight);
    }
}
