using System.Collections.Generic;
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
