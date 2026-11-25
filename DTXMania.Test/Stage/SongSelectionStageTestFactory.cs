using System.Collections.Generic;
using System.IO;
using DTXMania.Game;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Components;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage
{
    internal static class SongSelectionStageTestFactory
    {
        public static SongSelectionStage CreateStage(BaseGame? game = null)
        {
            game ??= CreateGame();
            // HPA-801: every test stage gets its own game-owned library over a
            // never-initialized temp database (construction is side-effect free).
            // The path lives under the DTXManiaCX_Tests convention folder so any
            // database a test materializes stays out of the %TEMP% root.
            SetPrivateField(game, "_songLibrary", new SongLibrary(new SongDatabase(
                Path.Combine(Path.GetTempPath(), "DTXManiaCX_Tests", "SongSelectionStageTestFactory",
                    $"{System.Guid.NewGuid():N}.db"))));
            return new SongSelectionStage(game);
        }

        // HPA-802 review: stage whose preview controller uses a fake instance-creation
        // delegate so tests can drive the real successful-play path headlessly.
        public static SongSelectionStage CreateStage(
            BaseGame? game,
            Func<DTXMania.Game.Lib.Resources.ISound?, DTXMania.Game.Lib.Resources.ISoundInstance?>? createPreviewInstance)
        {
            game ??= CreateGame();
            SetPrivateField(game, "_songLibrary", new SongLibrary(new SongDatabase(
                Path.Combine(Path.GetTempPath(), "DTXManiaCX_Tests", "SongSelectionStageTestFactory",
                    $"{System.Guid.NewGuid():N}.db"))));
            return new SongSelectionStage(game, createPreviewInstance);
        }

        /// <summary>
        /// A stage whose game exposes the caller's test-owned library (HPA-801).
        /// </summary>
        public static SongSelectionStage CreateStage(SongLibrary library)
        {
            var game = CreateGame();
            SetPrivateField(game, "_songLibrary", library);
            return new SongSelectionStage(game);
        }

        /// <summary>
        /// The test-owned library wired into the stage's game (HPA-801 replaced
        /// the shared SongManager.Instance with per-stage game-owned libraries).
        /// </summary>
        public static SongLibrary LibraryOf(SongSelectionStage stage)
        {
            var game = (DTXMania.Game.Lib.Stage.IStageGame)GetPrivateField<BaseGame>(stage, "_game");
            return game.SongLibrary ?? throw new InvalidOperationException(
                "test game must expose a song library");
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

        public static SongListNode Box(string title, string directoryPath, params SongListNode[] children)
        {
            var box = new SongListNode
            {
                Type = NodeType.Box,
                Title = title,
                DirectoryPath = directoryPath,
                Children = children.ToList(),
            };
            foreach (var child in box.Children)
                child.Parent = box;
            return box;
        }

        public static SongListNode Score(string title, int songId, string chartPath)
        {
            var chart = new SongChart { FilePath = chartPath };
            var song = new SongEntity { Id = songId, Title = title, Charts = new List<SongChart> { chart } };
            chart.Song = song;
            chart.SongId = songId;
            return new SongListNode
            {
                Type = NodeType.Score,
                Title = title,
                DatabaseSongId = songId,
                DatabaseSong = song,
                DatabaseChart = chart,
            };
        }
    }
}
