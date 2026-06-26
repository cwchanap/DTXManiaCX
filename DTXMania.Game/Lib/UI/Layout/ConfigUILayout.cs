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
        public static Rectangle ItemBarRect => new(400, 0, 18, 720);

        // Header / footer / title / instructions / import status.
        public static Rectangle HeaderRect => new(0, 0, 1280, 105);
        public static Rectangle FooterRect => new(0, 690, 1280, 30);
        public const int TitleY = 40;
        public const string InstructionsText =
            "UP/DOWN select   LEFT/RIGHT change   ENTER choose   ESC back (saves automatically)";
        public static Vector2 InstructionsPos => new(16, 696);
        public static Vector2 ImportStatusPos => new(430, 600);

        // Left category menu.
        public static Rectangle MenuPanelRect => new(245, 140, 180, 172);
        public const int MenuFirstRowY = 156;
        public const int MenuRowStride = 34;
        public const int MenuLabelCenterX = 335;
        public const int MenuCursorWidth = 150;
        public const int MenuCursorHeight = 30;
        public static int MenuRowY(int index) => MenuFirstRowY + index * MenuRowStride;
        public static Rectangle MenuCursorRect(int index) =>
            new(250, MenuRowY(index) - 3, MenuCursorWidth, MenuCursorHeight);

        // Right item list.
        public const int ItemListX = 430;
        public const int ItemFirstRowY = 150;
        public const int ItemRowStride = 60;
        public const int ItemBoxWidth = 360;
        public const int ItemBoxHeight = 54;
        public const int ItemNameInsetX = 24;
        public const int ItemValueInsetX = 330;
        public const int ItemTextInsetY = 14;
        public static int ItemRowY(int row) => ItemFirstRowY + row * ItemRowStride;
        public static Rectangle ItemRowRect(int row) =>
            new(ItemListX, ItemRowY(row), ItemBoxWidth, ItemBoxHeight);
        public static Rectangle ItemCursorRect(int row) =>
            new(ItemListX - 4, ItemRowY(row) - 3, ItemBoxWidth + 8, ItemRowStride);
        public static Vector2 ItemNamePos(int row) =>
            new(ItemListX + ItemNameInsetX, ItemRowY(row) + ItemTextInsetY);
        public static Vector2 ItemValuePos(int row) =>
            new(ItemListX + ItemValueInsetX, ItemRowY(row) + ItemTextInsetY);

        // Description panel.
        public static Rectangle DescriptionPanelRect => new(800, 270, 280, 360);
        public static Vector2 DescriptionTextPos => new(818, 288);
        public const int DescriptionWrapWidth = 248;
        public const int DescriptionLineHeight = 22;
    }
}
