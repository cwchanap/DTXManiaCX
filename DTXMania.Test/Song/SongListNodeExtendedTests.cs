using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Extended tests for SongListNode covering uncovered paths
    /// </summary>
    public class SongListNodeExtendedTests
    {
        #region DisplayTitle Tests

        [Fact]
        public void DisplayTitle_BackBoxType_ShouldReturnBack()
        {
            var node = new SongListNode { Type = NodeType.BackBox, Title = "" };
            Assert.Equal(".. (Back)", node.DisplayTitle);
        }

        [Fact]
        public void DisplayTitle_RandomType_ShouldReturnRandom()
        {
            var node = new SongListNode { Type = NodeType.Random, Title = "" };
            Assert.Equal("Random Select", node.DisplayTitle);
        }

        [Fact]
        public void DisplayTitle_WithDatabaseSongTitle_ShouldUseDatabaseTitle()
        {
            var node = new SongListNode
            {
                Title = "",
                DatabaseSong = new SongEntity { Title = "Database Title" }
            };
            Assert.Equal("Database Title", node.DisplayTitle);
        }

        [Fact]
        public void DisplayTitle_WithDatabaseChartFilePath_ShouldUseFilename()
        {
            var node = new SongListNode
            {
                Title = "",
                DatabaseChart = new SongChart { FilePath = "/songs/mysong.dtx" }
            };
            Assert.Equal("mysong", node.DisplayTitle);
        }

        [Fact]
        public void DisplayTitle_WithDatabaseSongButNoTitle_ShouldReturnUnknownSong()
        {
            var node = new SongListNode
            {
                Title = "",
                DatabaseSong = new SongEntity { Title = "" }
            };
            Assert.Equal("Unknown Song", node.DisplayTitle);
        }

        [Fact]
        public void DisplayTitle_NoDatabaseEntities_ShouldReturnUnknown()
        {
            var node = new SongListNode { Title = "" };
            Assert.Equal("Unknown", node.DisplayTitle);
        }

        #endregion

        #region Factory Method Tests

        [Fact]
        public void CreateSongNode_WithDrumChart_ShouldSetScore()
        {
            var song = new SongEntity { Title = "Test Song" };
            var chart = new SongChart
            {
                FilePath = "/test.dtx",
                HasDrumChart = true,
                DrumNoteCount = 100,
                DrumLevel = 5
            };

            var node = SongListNode.CreateSongNode(song, chart);

            Assert.NotNull(node);
            Assert.Equal("Test Song", node.Title);
            Assert.NotNull(node.Scores[0]);
        }

        [Fact]
        public void CreateSongNode_WithGuitarChart_ShouldSetGuitarScore()
        {
            var song = new SongEntity { Title = "Test Song" };
            var chart = new SongChart
            {
                FilePath = "/test.dtx",
                GuitarNoteCount = 50,
                GuitarLevel = 3
            };

            var node = SongListNode.CreateSongNode(song, chart);

            Assert.Equal(NodeType.Score, node.Type);
        }

        [Fact]
        public void CreateSongNode_NullSong_ShouldThrow()
        {
            var chart = new SongChart { FilePath = "/test.dtx" };
            Assert.Throws<System.ArgumentNullException>(() => SongListNode.CreateSongNode(null, chart));
        }

        [Fact]
        public void CreateSongNode_NullChart_ShouldThrow()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Test" };
            Action act = () => SongListNode.CreateSongNode(song, null);
            Assert.Throws<System.ArgumentNullException>(act);
        }

        [Fact]
        public void CreateBoxNode_ShouldSetTypeAndTitle()
        {
            var node = SongListNode.CreateBoxNode("My Box", "/music/folder");

            Assert.Equal(NodeType.Box, node.Type);
            Assert.Equal("My Box", node.Title);
            Assert.Equal("/music/folder", node.DirectoryPath);
        }

        [Fact]
        public void CreateBoxNode_WithParent_ShouldSetBreadcrumb()
        {
            var parent = SongListNode.CreateBoxNode("Parent", "/parent");
            parent.BreadcrumbPath = "Parent";
            var child = SongListNode.CreateBoxNode("Child", "/child", parent);

            Assert.Contains("Parent", child.BreadcrumbPath);
            Assert.Contains("Child", child.BreadcrumbPath);
        }

        [Fact]
        public void CreateBackNode_ShouldSetBackBoxType()
        {
            var parent = SongListNode.CreateBoxNode("Parent", "/parent");
            parent.BreadcrumbPath = "Parent";
            var backNode = SongListNode.CreateBackNode(parent);

            Assert.Equal(NodeType.BackBox, backNode.Type);
            Assert.Equal(parent, backNode.Parent);
        }

        [Fact]
        public void CreateRandomNode_ShouldSetRandomType()
        {
            var node = SongListNode.CreateRandomNode();

            Assert.Equal(NodeType.Random, node.Type);
            Assert.Equal(Color.Yellow, node.TextColor);
        }

        #endregion

        #region GetClosestDifficultyIndex Tests

        [Fact]
        public void GetClosestDifficultyIndex_WithScoreAtAnchor_ShouldReturnAnchor()
        {
            var node = new SongListNode();
            node.Scores[2] = new SongScore { DifficultyLevel = 5 };

            var index = node.GetClosestDifficultyIndex(2);

            Assert.Equal(2, index);
        }

        [Fact]
        public void GetClosestDifficultyIndex_ScoreAboveAnchor_ShouldReturnAbove()
        {
            var node = new SongListNode();
            node.Scores[3] = new SongScore { DifficultyLevel = 7 };

            var index = node.GetClosestDifficultyIndex(1);

            Assert.Equal(3, index);
        }

        [Fact]
        public void GetClosestDifficultyIndex_ScoreBelowAnchor_ShouldReturnBelow()
        {
            var node = new SongListNode();
            node.Scores[1] = new SongScore { DifficultyLevel = 3 };

            var index = node.GetClosestDifficultyIndex(3);

            Assert.Equal(1, index);
        }

        [Fact]
        public void GetClosestDifficultyIndex_NoScores_ShouldReturnZero()
        {
            var node = new SongListNode();

            var index = node.GetClosestDifficultyIndex(2);

            Assert.Equal(0, index);
        }

        #endregion

        #region SortChildren Tests

        [Fact]
        public void SortChildren_ByTitle_ShouldSortAlphabetically()
        {
            var parent = SongListNode.CreateBoxNode("Parent", "/parent");
            var childC = new SongListNode { Title = "C Song" };
            var childA = new SongListNode { Title = "A Song" };
            var childB = new SongListNode { Title = "B Song" };

            parent.AddChild(childC);
            parent.AddChild(childA);
            parent.AddChild(childB);

            parent.SortChildren(SongSortCriteria.Title);

            Assert.Equal("A Song", parent.Children[0].Title);
            Assert.Equal("B Song", parent.Children[1].Title);
            Assert.Equal("C Song", parent.Children[2].Title);
        }

        [Fact]
        public void SortChildren_ByGenre_ShouldSortByGenre()
        {
            var parent = SongListNode.CreateBoxNode("Parent", "/parent");
            var childB = new SongListNode { Title = "Song B", Genre = "Z Genre" };
            var childA = new SongListNode { Title = "Song A", Genre = "A Genre" };

            parent.AddChild(childB);
            parent.AddChild(childA);

            parent.SortChildren(SongSortCriteria.Genre);

            Assert.Equal("A Genre", parent.Children[0].Genre);
        }

        [Fact]
        public void SortChildren_BoxNodesFirst_ShouldComeBeforeSongs()
        {
            var parent = SongListNode.CreateBoxNode("Root", "/root");
            var song = new SongListNode { Title = "A Song", Type = NodeType.Score };
            var box = SongListNode.CreateBoxNode("Z Box", "/z");

            parent.AddChild(song);
            parent.AddChild(box);

            parent.SortChildren(SongSortCriteria.Title);

            Assert.Equal(NodeType.Box, parent.Children[0].Type);
        }

        [Fact]
        public void SortChildren_ByLevel_ShouldSortByMaxLevel()
        {
            var parent = SongListNode.CreateBoxNode("Parent", "/parent");
            var lowLevel = new SongListNode { Title = "Low" };
            lowLevel.Scores[0] = new SongScore { DifficultyLevel = 2 };
            var highLevel = new SongListNode { Title = "High" };
            highLevel.Scores[0] = new SongScore { DifficultyLevel = 8 };

            parent.AddChild(lowLevel);
            parent.AddChild(highLevel);

            parent.SortChildren(SongSortCriteria.Level);

            // Higher level first (descending)
            Assert.Equal("High", parent.Children[0].Title);
        }

        [Fact]
        public void SortChildren_ByArtist_ShouldSortByArtist()
        {
            var parent = SongListNode.CreateBoxNode("Parent", "/parent");
            var songZ = new SongListNode
            {
                Title = "Song 1",
                DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song { Artist = "Z Artist" }
            };
            var songA = new SongListNode
            {
                Title = "Song 2",
                DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song { Artist = "A Artist" }
            };

            parent.AddChild(songZ);
            parent.AddChild(songA);

            parent.SortChildren(SongSortCriteria.Artist);

            Assert.Equal("A Artist", parent.Children[0].DatabaseSong?.Artist);
        }

        #endregion

        #region RemoveChild Tests

        [Fact]
        public void RemoveChild_ExistingChild_ShouldReturnTrue()
        {
            var parent = SongListNode.CreateBoxNode("Parent", "/parent");
            var child = new SongListNode { Title = "Child" };
            parent.AddChild(child);

            var result = parent.RemoveChild(child);

            Assert.True(result);
            Assert.Empty(parent.Children);
        }

        [Fact]
        public void RemoveChild_ExistingChild_ShouldClearParentRef()
        {
            var parent = SongListNode.CreateBoxNode("Parent", "/parent");
            var child = new SongListNode { Title = "Child" };
            parent.AddChild(child);

            parent.RemoveChild(child);

            Assert.Null(child.Parent);
        }

        [Fact]
        public void RemoveChild_NonExistingChild_ShouldReturnFalse()
        {
            var parent = SongListNode.CreateBoxNode("Parent", "/parent");
            var child = new SongListNode { Title = "Stranger" };

            var result = parent.RemoveChild(child);

            Assert.False(result);
        }

        #endregion

        #region Calculated Properties Tests

        [Fact]
        public void IsPlayable_ScoreNodeWithScores_ShouldBeTrue()
        {
            var node = new SongListNode { Type = NodeType.Score };
            node.Scores[0] = new SongScore { DifficultyLevel = 3 };

            Assert.True(node.IsPlayable);
        }

        [Fact]
        public void IsPlayable_ScoreNodeWithNoScores_ShouldBeFalse()
        {
            var node = new SongListNode { Type = NodeType.Score };
            Assert.False(node.IsPlayable);
        }

        [Fact]
        public void IsPlayable_BoxNode_ShouldBeFalse()
        {
            var node = SongListNode.CreateBoxNode("Box", "/box");
            Assert.False(node.IsPlayable);
        }

        [Fact]
        public void IsFolder_BoxNode_ShouldBeTrue()
        {
            var node = SongListNode.CreateBoxNode("Box", "/box");
            Assert.True(node.IsFolder);
        }

        [Fact]
        public void IsBackNavigation_BackBoxNode_ShouldBeTrue()
        {
            var parent = SongListNode.CreateBoxNode("P", "/p");
            var back = SongListNode.CreateBackNode(parent);
            Assert.True(back.IsBackNavigation);
        }

        [Fact]
        public void AvailableDifficulties_MultipleScores_ShouldCountNonNull()
        {
            var node = new SongListNode();
            node.Scores[0] = new SongScore { DifficultyLevel = 3 };
            node.Scores[2] = new SongScore { DifficultyLevel = 5 };

            Assert.Equal(2, node.AvailableDifficulties);
        }

        [Fact]
        public void MaxDifficultyLevel_NoScores_ShouldReturnZero()
        {
            var node = new SongListNode();
            Assert.Equal(0, node.MaxDifficultyLevel);
        }

        #endregion

        #region GetScore Tests

        [Fact]
        public void GetScore_ValidIndex_ShouldReturnScore()
        {
            var node = new SongListNode();
            var score = new SongScore { DifficultyLevel = 5 };
            node.Scores[1] = score;

            var result = node.GetScore(1);
            Assert.Equal(score, result);
        }

        [Fact]
        public void GetScore_InvalidIndex_ShouldReturnNull()
        {
            var node = new SongListNode();
            Assert.Null(node.GetScore(-1));
            Assert.Null(node.GetScore(10));
        }

        #endregion

        #region SetScore Tests

        [Fact]
        public void SetScore_ValidIndex_ShouldSetScore()
        {
            var node = new SongListNode();
            var score = new SongScore { DifficultyLevel = 7 };
            node.SetScore(2, score);

            Assert.Equal(score, node.Scores[2]);
        }

        [Fact]
        public void SetScore_InvalidIndex_ShouldNotThrow()
        {
            var node = new SongListNode();
            var score = new SongScore { DifficultyLevel = 7 };
            node.SetScore(-1, score); // Should not throw
            node.SetScore(10, score); // Should not throw
        }

        #endregion

        #region Note Tests

        [Fact]
        public void Note_ToString_ShouldContainLaneAndTime()
        {
            var note = new DTXMania.Game.Lib.Song.Components.Note(3, 1, 96, 0x12, "01");
            note.TimeMs = 3000.0;
            var result = note.ToString();
            Assert.Contains("SN", result);
            Assert.Contains("3000", result);
        }

        [Fact]
        public void Note_DefaultConstructor_ShouldCreateNote()
        {
            var note = new DTXMania.Game.Lib.Song.Components.Note();
            Assert.Equal(0, note.LaneIndex);
            Assert.Equal(0, note.Bar);
            Assert.Equal(0, note.Tick);
        }

        #endregion
    }
}
