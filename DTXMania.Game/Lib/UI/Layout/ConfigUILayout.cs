#nullable enable

using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.UI.Layout
{
    /// <summary>
    /// Coordinate/size constants and index→position helpers for the NX-style master-detail
    /// Config stage, in the 1280x720 virtual space ported from DTXManiaNX CStageConfig /
    /// CActConfigList. Pure (no GraphicsDevice) so it is fully unit-testable.
    /// </summary>
    public static class ConfigUILayout
    {
        public const int ScreenWidth = 1280;
        public const int ScreenHeight = 720;

        // Background + vertical divider.
        public static Rectangle BackgroundRect => new(0, 0, ScreenWidth, ScreenHeight);
        public static Rectangle ItemBarRect => new(400, 0, 18, ScreenHeight);

        // Inner board: a dark, translucent framed panel drawn over the busy GALAXY WAVE
        // background (4_background.png) but behind the menu/item/description panels, so the
        // config content stays legible and reads as a contained window. NX has no dedicated
        // asset for this — it is drawn with fills. The border rect is the outer frame; the
        // board rect is the 4px-inset fill. The border spans the full band between the header
        // (bottom=105) and footer (top=690) so it fully backs the scrolling item list: item
        // rows are visible anywhere in that band, so a shorter board would leave partial rows
        // sitting on the bare background at the top/bottom edges.
        public static Rectangle InnerBoardBorderRect => new(220, 105, 880, 585);
        public static Rectangle InnerBoardRect => new(224, 109, 872, 577);

        // Header / footer / title / instructions / import status.
        public static Rectangle HeaderRect => new(0, 0, ScreenWidth, 105);
        public static Rectangle FooterRect => new(0, 690, ScreenWidth, 30);
        public const int TitleY = 40;
        public const string InstructionsText =
            "UP/DOWN select   LEFT/RIGHT change   ENTER choose   ESC back (saves automatically)";
        public static Vector2 InstructionsPos => new(16, 696);
        public static Vector2 ImportStatusPos => new(430, 600);

        // Left category menu.
        public static Rectangle MenuPanelRect => new(245, 140, 180, 172);
        public const int MenuLabelCenterX = 335;   // panel center: 245 + 180/2
        public const int MenuCursorX = 250;
        public const int MenuCursorWidth = 170;
        public const int MenuCursorHeight = 32;
        public const int MenuFirstCursorY = 148;
        public const int MenuRowStride = 32;
        public static Rectangle MenuCursorRect(int index) =>
            new(MenuCursorX, MenuFirstCursorY + index * MenuRowStride, MenuCursorWidth, MenuCursorHeight);

        // Right item list — scrolling viewport (selected item locked to the focus row).
        public const int ItemListX = 420;
        public const int ItemBoxHeight = 80;
        public const int ItemBoxNormalWidth = 538;   // 4_itembox.png: dark name cell + white value cell
        public const int ItemRowStride = 67;         // NX stride; boxes overlap 13px as the art tiles
        public const int ItemFocusRowTopY = 189;     // panel-top Y of the centered/selected row
        public const int ItemVisibleTopY = 105;      // header bottom
        public const int ItemVisibleBottomY = 690;   // footer top
        public const int ItemNameOffsetX = 20;
        public const int ItemValueOffsetX = 260;
        public const int ItemTextOffsetY = 24;
        // Value text left-aligns at ItemListX+ItemValueOffsetX (680) and must stay left of the
        // description panel (x=800), so cap its width. 800 - 680 - 4 margin = 116.
        public const int ItemValueMaxWidth = 116;
        // Selection cursor is fixed at the focus row; items scroll under it (NX 4_itembox cursor.png 497x68).
        public static Rectangle ItemCursorRect => new(413, 193, 497, 68);

        // Panel-top Y of item `index` given the eased scroll position `scroll` (fractional index at focus).
        public static int RowTopY(int index, double scroll) =>
            ItemFocusRowTopY + (int)System.Math.Round((index - scroll) * ItemRowStride);

        // True when the row's box intersects the visible band between header and footer.
        public static bool IsRowVisible(int rowTopY) =>
            (rowTopY + ItemBoxHeight) > ItemVisibleTopY && rowTopY < ItemVisibleBottomY;

        public static Rectangle ItemBoxRect(int rowTopY, int width) =>
            new(ItemListX, rowTopY, width, ItemBoxHeight);
        public static Vector2 ItemNamePos(int rowTopY) =>
            new(ItemListX + ItemNameOffsetX, rowTopY + ItemTextOffsetY);
        public static Vector2 ItemValuePos(int rowTopY) =>
            new(ItemListX + ItemValueOffsetX, rowTopY + ItemTextOffsetY);

        // Description panel — art is white (top) over black (bottom); use both cells.
        public static Rectangle DescriptionPanelRect => new(800, 270, 280, 360);
        public static Vector2 DescriptionTitlePos => new(818, 300);   // white upper region -> dark text
        public static Vector2 DescriptionBodyPos => new(818, 448);    // black lower region -> light text
        public const int DescriptionWrapWidth = 248;
        public const int DescriptionLineHeight = 22;
    }
}
