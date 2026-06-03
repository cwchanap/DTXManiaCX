using DTXMania.Game;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.UI.Components;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage
{
    internal static class SongSelectionStageTestFactory
    {
        public static SongSelectionStage CreateStage(BaseGame? game = null)
        {
            return new SongSelectionStage(game ?? CreateGame());
        }

        public static void AttachCoreUi(
            SongSelectionStage stage,
            SongListDisplay? display = null,
            SongStatusPanel? statusPanel = null,
            PreviewImagePanel? previewPanel = null,
            UILabel? breadcrumb = null)
        {
            SetPrivateField(stage, "_songListDisplay", display ?? new SongListDisplay());
            SetPrivateField(stage, "_statusPanel", statusPanel ?? new SongStatusPanel());
            SetPrivateField(stage, "_previewImagePanel", previewPanel ?? new PreviewImagePanel());
            SetPrivateField(stage, "_breadcrumbLabel", breadcrumb ?? new UILabel());
        }
    }
}
