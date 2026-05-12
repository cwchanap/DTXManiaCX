using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Moq;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;

using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.Stage
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongSelectionStagePreviewAndBgmTests
    {
        private static SongSelectionStage CreateStage()
        {
            return new SongSelectionStage(CreateGame());
        }

        private static SongListNode CreateScoreNode(string title, SongScore[]? scores = null)
        {
            scores ??= CreateScores(0);
            return new SongListNode
            {
                Type = NodeType.Score,
                Title = title,
                Scores = scores
            };
        }

        private static SongScore[] CreateScores(params int[] difficulties)
        {
            var result = new SongScore[5];
            foreach (var difficulty in difficulties)
            {
                result[difficulty] = new SongScore
                {
                    DifficultyLevel = difficulty,
                    DifficultyLabel = $"Level {difficulty}"
                };
            }
            return result;
        }

        private static SongListNode CreateBoxNode(string title, params SongListNode[] children)
        {
            return new SongListNode
            {
                Type = NodeType.Box,
                Title = title,
                Children = new List<SongListNode>(children)
            };
        }

        private static void AttachCoreUi(
            SongSelectionStage stage,
            SongListDisplay? display = null,
            SongStatusPanel? statusPanel = null)
        {
            SetPrivateField(stage, "_songListDisplay", display ?? new SongListDisplay());
            SetPrivateField(stage, "_statusPanel", statusPanel ?? new SongStatusPanel());
            SetPrivateField(stage, "_uiManager", new UIManager());
            SetPrivateField(stage, "_mainPanel", new UIPanel());
            SetPrivateField(stage, "_navigationStack", new Stack<SongListNode>());
            SetPrivateField(stage, "_breadcrumbLabel", new UILabel(""));
            SetPrivateField(stage, "_titleLabel", new UILabel(""));
            SetPrivateField(stage, "_selectionPhase", SongSelectionPhase.Normal);
            SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
        }

        private static object? InvokeStaticMethod(Type type, string methodName, params object[] args)
        {
            var method = type.GetMethod(methodName,
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            Assert.NotNull(method);
            return method!.Invoke(null, args);
        }

        private static T? InvokeStaticMethod<T>(Type type, string methodName, params object[] args)
        {
            var result = InvokeStaticMethod(type, methodName, args);
            if (result is null) return default;
            return (T)result;
        }

        [Fact]
        public void SetBackgroundMusic_WhenPreviousBgmExists_ShouldReleaseReference()
        {
            var stage = CreateStage();
            var oldSound = new Mock<ISound>();
            var oldInstance = new Mock<ISoundInstance>();
            var newSound = new Mock<ISound>();
            var newInstance = new Mock<ISoundInstance>();

            SetPrivateField(stage, "_backgroundMusic", oldSound.Object);
            SetPrivateField(stage, "_backgroundMusicInstance", oldInstance.Object);

            stage.SetBackgroundMusic(newSound.Object, newInstance.Object);

            oldSound.Verify(x => x.RemoveReference(), Times.Once);
            oldInstance.Verify(x => x.Stop(), Times.Once);
            oldInstance.Verify(x => x.Dispose(), Times.Once);
            Assert.Same(newSound.Object, GetPrivateField<ISound>(stage, "_backgroundMusic"));
            Assert.Same(newInstance.Object, GetPrivateField<ISoundInstance>(stage, "_backgroundMusicInstance"));
        }

        [Fact]
        public void SetBackgroundMusic_WhenPreviousBgmRemoveReferenceThrows_ShouldNotThrow()
        {
            var stage = CreateStage();
            var oldSound = new Mock<ISound>();
            oldSound.Setup(x => x.RemoveReference()).Throws(new Exception("boom"));
            var newSound = new Mock<ISound>();

            SetPrivateField(stage, "_backgroundMusic", oldSound.Object);
            SetPrivateField(stage, "_backgroundMusicInstance", null);

            var exception = Record.Exception(() => stage.SetBackgroundMusic(newSound.Object, null));

            Assert.Null(exception);
            Assert.Same(newSound.Object, GetPrivateField<ISound>(stage, "_backgroundMusic"));
        }

        [Fact]
        public void SetBackgroundMusic_WhenPreviousInstanceStopThrows_ShouldNotThrow()
        {
            var stage = CreateStage();
            var oldSound = new Mock<ISound>();
            var oldInstance = new Mock<ISoundInstance>();
            oldInstance.Setup(x => x.Stop()).Throws(new Exception("stop fail"));

            SetPrivateField(stage, "_backgroundMusic", oldSound.Object);
            SetPrivateField(stage, "_backgroundMusicInstance", oldInstance.Object);

            var exception = Record.Exception(() => stage.SetBackgroundMusic(null, null));

            Assert.Null(exception);
            oldInstance.Verify(x => x.Dispose(), Times.Once);
        }

        [Fact]
        public void SetBackgroundMusic_WhenPreviousInstanceDisposeThrows_ShouldNotThrow()
        {
            var stage = CreateStage();
            var oldSound = new Mock<ISound>();
            var oldInstance = new Mock<ISoundInstance>();
            oldInstance.Setup(x => x.Dispose()).Throws(new Exception("dispose fail"));

            SetPrivateField(stage, "_backgroundMusic", oldSound.Object);
            SetPrivateField(stage, "_backgroundMusicInstance", oldInstance.Object);

            var exception = Record.Exception(() => stage.SetBackgroundMusic(null, null));

            Assert.Null(exception);
        }

        [Fact]
        public void SetBackgroundMusic_WhenExistingBgmIsNull_ShouldSetDirectly()
        {
            var stage = CreateStage();
            var newSound = new Mock<ISound>();
            var newInstance = new Mock<ISoundInstance>();

            SetPrivateField(stage, "_backgroundMusic", null);
            SetPrivateField(stage, "_backgroundMusicInstance", null);

            stage.SetBackgroundMusic(newSound.Object, newInstance.Object);

            Assert.Same(newSound.Object, GetPrivateField<ISound>(stage, "_backgroundMusic"));
            Assert.Same(newInstance.Object, GetPrivateField<ISoundInstance>(stage, "_backgroundMusicInstance"));
        }

        [Fact]
        public void StopCurrentPreview_WhenPreviewSoundInstanceIsNull_ShouldResetTimersAndStartBgmFadeIn()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_previewSoundInstance", null);
            SetPrivateField(stage, "_previewSound", null);
            SetPrivateField(stage, "_previewPlayDelay", 5.0);
            SetPrivateField(stage, "_isPreviewDelayActive", true);

            InvokePrivateMethod(stage, "StopCurrentPreview");

            Assert.Equal(0.0, GetPrivateField<double>(stage, "_previewPlayDelay"));
            Assert.False(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
            Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
        }

        [Fact]
        public void StopCurrentPreview_WhenPreviewSoundInstanceStopThrows_ShouldStillDisposeAndClear()
        {
            var stage = CreateStage();
            var mockInstance = new Mock<ISoundInstance>();
            mockInstance.Setup(x => x.State).Returns(SoundState.Playing);
            mockInstance.Setup(x => x.Stop()).Throws(new Exception("stop error"));

            SetPrivateField(stage, "_previewSoundInstance", mockInstance.Object);
            SetPrivateField(stage, "_previewSound", null);

            InvokePrivateMethod(stage, "StopCurrentPreview");

            mockInstance.Verify(x => x.Dispose(), Times.Once);
            Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
            Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
        }

        [Fact]
        public void StopCurrentPreview_WhenPreviewSoundInstanceIsNotPlaying_ShouldNotCallStop()
        {
            var stage = CreateStage();
            var mockInstance = new Mock<ISoundInstance>();
            mockInstance.Setup(x => x.State).Returns(SoundState.Stopped);

            SetPrivateField(stage, "_previewSoundInstance", mockInstance.Object);
            SetPrivateField(stage, "_previewSound", null);

            InvokePrivateMethod(stage, "StopCurrentPreview");

            mockInstance.Verify(x => x.Stop(), Times.Never);
            mockInstance.Verify(x => x.Dispose(), Times.Once);
            Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
        }

        [Fact]
        public void StopCurrentPreview_WhenPreviewSoundExists_ShouldReleaseReference()
        {
            var stage = CreateStage();
            var mockSound = new Mock<ISound>();
            var mockInstance = new Mock<ISoundInstance>();
            mockInstance.Setup(x => x.State).Returns(SoundState.Stopped);

            SetPrivateField(stage, "_previewSoundInstance", mockInstance.Object);
            SetPrivateField(stage, "_previewSound", mockSound.Object);

            InvokePrivateMethod(stage, "StopCurrentPreview");

            mockSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<ISound>(stage, "_previewSound"));
        }

        [Fact]
        public void StartBGMFade_WithFadeOutTrue_ShouldSetFadeOutState()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_isBgmFadingIn", true);
            SetPrivateField(stage, "_isBgmFadingOut", false);

            InvokePrivateMethod(stage, "StartBGMFade", true);

            Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
            Assert.Equal(0.0, GetPrivateField<double>(stage, "_bgmFadeOutTimer"));
        }

        [Fact]
        public void StartBGMFade_WithFadeOutFalse_ShouldSetFadeInState()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_isBgmFadingOut", true);
            SetPrivateField(stage, "_isBgmFadingIn", false);

            InvokePrivateMethod(stage, "StartBGMFade", false);

            Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
            Assert.Equal(0.0, GetPrivateField<double>(stage, "_bgmFadeInTimer"));
        }

        [Fact]
        public void UpdatePreviewSoundTimers_BgmFadeOut_WhenTimerExceedsDuration_ShouldStopFading()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_isPreviewDelayActive", false);
            SetPrivateField(stage, "_isBgmFadingOut", true);
            SetPrivateField(stage, "_bgmFadeOutTimer", 0.4);
            SetPrivateField(stage, "_backgroundMusicInstance", null);
            SetPrivateField(stage, "_isBgmFadingIn", false);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.2);

            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
            Assert.Equal(SongSelectionUILayout.Audio.BgmFadeOutDuration,
                GetPrivateField<double>(stage, "_bgmFadeOutTimer"));
        }

        [Fact]
        public void UpdatePreviewSoundTimers_BgmFadeOut_WithPlayingBgmInstance_ShouldApplyVolumeFade()
        {
            var stage = CreateStage();
            var bgmInstance = new Mock<ISoundInstance>();
            bgmInstance.Setup(x => x.State).Returns(SoundState.Playing);

            SetPrivateField(stage, "_isPreviewDelayActive", false);
            SetPrivateField(stage, "_isBgmFadingOut", true);
            SetPrivateField(stage, "_bgmFadeOutTimer", 0.0);
            SetPrivateField(stage, "_backgroundMusicInstance", bgmInstance.Object);
            SetPrivateField(stage, "_isBgmFadingIn", false);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.25);

            bgmInstance.VerifySet(x => x.Volume = It.IsAny<float>(), Times.Once);
        }

        [Fact]
        public void UpdatePreviewSoundTimers_BgmFadeOut_WhenVolumeSetThrows_ShouldStopFading()
        {
            var stage = CreateStage();
            var bgmInstance = new Mock<ISoundInstance>();
            bgmInstance.Setup(x => x.State).Returns(SoundState.Playing);
            bgmInstance.SetupSet(x => x.Volume = It.IsAny<float>()).Throws(new Exception("vol error"));

            SetPrivateField(stage, "_isPreviewDelayActive", false);
            SetPrivateField(stage, "_isBgmFadingOut", true);
            SetPrivateField(stage, "_bgmFadeOutTimer", 0.0);
            SetPrivateField(stage, "_backgroundMusicInstance", bgmInstance.Object);
            SetPrivateField(stage, "_isBgmFadingIn", false);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.1);

            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
        }

        [Fact]
        public void UpdatePreviewSoundTimers_BgmFadeIn_WhenTimerExceedsDuration_ShouldStopFading()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_isPreviewDelayActive", false);
            SetPrivateField(stage, "_isBgmFadingOut", false);
            SetPrivateField(stage, "_isBgmFadingIn", true);
            SetPrivateField(stage, "_bgmFadeInTimer", 0.9);
            SetPrivateField(stage, "_backgroundMusicInstance", null);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.2);

            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
            Assert.Equal(SongSelectionUILayout.Audio.BgmFadeInDuration,
                GetPrivateField<double>(stage, "_bgmFadeInTimer"));
        }

        [Fact]
        public void UpdatePreviewSoundTimers_BgmFadeIn_WithPlayingBgmInstance_ShouldApplyVolumeFade()
        {
            var stage = CreateStage();
            var bgmInstance = new Mock<ISoundInstance>();
            bgmInstance.Setup(x => x.State).Returns(SoundState.Playing);

            SetPrivateField(stage, "_isPreviewDelayActive", false);
            SetPrivateField(stage, "_isBgmFadingOut", false);
            SetPrivateField(stage, "_isBgmFadingIn", true);
            SetPrivateField(stage, "_bgmFadeInTimer", 0.0);
            SetPrivateField(stage, "_backgroundMusicInstance", bgmInstance.Object);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.5);

            bgmInstance.VerifySet(x => x.Volume = It.IsAny<float>(), Times.Once);
        }

        [Fact]
        public void UpdatePreviewSoundTimers_BgmFadeIn_WhenVolumeSetThrows_ShouldStopFading()
        {
            var stage = CreateStage();
            var bgmInstance = new Mock<ISoundInstance>();
            bgmInstance.Setup(x => x.State).Returns(SoundState.Playing);
            bgmInstance.SetupSet(x => x.Volume = It.IsAny<float>()).Throws(new Exception("vol error"));

            SetPrivateField(stage, "_isPreviewDelayActive", false);
            SetPrivateField(stage, "_isBgmFadingOut", false);
            SetPrivateField(stage, "_isBgmFadingIn", true);
            SetPrivateField(stage, "_bgmFadeInTimer", 0.0);
            SetPrivateField(stage, "_backgroundMusicInstance", bgmInstance.Object);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.1);

            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
        }

        [Fact]
        public void PlayCursorMoveSound_WhenSoundIsNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_cursorMoveSound", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "PlayCursorMoveSound"));

            Assert.Null(exception);
        }

        [Fact]
        public void PlayCursorMoveSound_WhenPlayThrows_ShouldNotThrow()
        {
            var stage = CreateStage();
            var mockSound = new Mock<ISound>();
            mockSound.Setup(x => x.Play(It.IsAny<float>())).Throws(new Exception("audio error"));

            SetPrivateField(stage, "_cursorMoveSound", mockSound.Object);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "PlayCursorMoveSound"));

            Assert.Null(exception);
        }

        [Fact]
        public void PlayCursorMoveSound_WithValidSound_ShouldPlayWithCorrectVolume()
        {
            var stage = CreateStage();
            var mockSound = new Mock<ISound>();

            SetPrivateField(stage, "_cursorMoveSound", mockSound.Object);

            InvokePrivateMethod(stage, "PlayCursorMoveSound");

            mockSound.Verify(x => x.Play(SongSelectionUILayout.Audio.NavigationSoundVolume), Times.Once);
        }

        [Fact]
        public void PlayGameStartSound_WhenSoundIsNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_gameStartSound", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "PlayGameStartSound"));

            Assert.Null(exception);
        }

        [Fact]
        public void PlayGameStartSound_WhenPlayThrows_ShouldNotThrow()
        {
            var stage = CreateStage();
            var mockSound = new Mock<ISound>();
            mockSound.Setup(x => x.Play(It.IsAny<float>())).Throws(new Exception("audio error"));

            SetPrivateField(stage, "_gameStartSound", mockSound.Object);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "PlayGameStartSound"));

            Assert.Null(exception);
        }

        [Fact]
        public void PlayGameStartSound_WithValidSound_ShouldPlayWithCorrectVolume()
        {
            var stage = CreateStage();
            var mockSound = new Mock<ISound>();

            SetPrivateField(stage, "_gameStartSound", mockSound.Object);

            InvokePrivateMethod(stage, "PlayGameStartSound");

            mockSound.Verify(x => x.Play(SongSelectionUILayout.Audio.GameStartSoundVolume), Times.Once);
        }

        [Fact]
        public void SummarizeFilter_WithAllFieldsSet_ShouldIncludeAllParts()
        {
            var criteria = new SongFilterCriteria(
                "test",
                10,
                50,
                PlayedStatus.Played,
                SongSortCriteria.Artist,
                true);

            var result = SongSelectionStage.SummarizeFilter(criteria);

            Assert.Contains("test", result);
            Assert.Contains("Lv 10-50", result);
            Assert.Contains("Played", result);
            Assert.Contains("Artist", result);
        }

        [Fact]
        public void SummarizeFilter_WithOnlyPlayedStatusNotAll_ShouldIncludeStatus()
        {
            var criteria = new SongFilterCriteria(
                "",
                null,
                null,
                PlayedStatus.Unplayed,
                SongSortCriteria.Title,
                false);

            var result = SongSelectionStage.SummarizeFilter(criteria);

            Assert.Contains("Unplayed", result);
            Assert.DoesNotContain("Lv", result);
        }

        [Fact]
        public void SummarizeFilter_WithOnlySortNonDefault_ShouldIncludeSort()
        {
            var criteria = new SongFilterCriteria(
                "",
                null,
                null,
                PlayedStatus.All,
                SongSortCriteria.Level,
                true);

            var result = SongSelectionStage.SummarizeFilter(criteria);

            Assert.Contains("Level", result);
            Assert.DoesNotContain("Lv", result);
        }

        [Fact]
        public void SummarizeFilter_WithEmptyCriteria_ShouldReturnEmptyString()
        {
            var result = SongSelectionStage.SummarizeFilter(SongFilterCriteria.Default);

            Assert.Equal("", result);
        }

        [Fact]
        public void SummarizeFilter_WithSearchQueryOnly_ShouldIncludeSearchQuery()
        {
            var criteria = new SongFilterCriteria(
                "hello",
                null,
                null,
                PlayedStatus.All,
                SongSortCriteria.Title,
                false);

            var result = SongSelectionStage.SummarizeFilter(criteria);

            Assert.Contains("hello", result);
            Assert.StartsWith("Filtered:", result);
        }

        [Fact]
        public void FormatLevel_WithInvertedMinMax_ShouldNormalize()
        {
            var result = InvokeStaticMethod<string>(typeof(SongSelectionStage), "FormatLevel", 50, (int?)10);

            Assert.Equal("Lv 10-50", result);
        }

        [Fact]
        public void FormatLevel_WithOnlyMinSet_ShouldReturnPlusFormat()
        {
            var result = InvokeStaticMethod<string>(typeof(SongSelectionStage), "FormatLevel", 30, (int?)null);

            Assert.Equal("Lv 30+", result);
        }

        [Fact]
        public void FormatLevel_WithOnlyMaxSet_ShouldReturnLessThanOrEqualFormat()
        {
            var result = InvokeStaticMethod<string>(typeof(SongSelectionStage), "FormatLevel", null, (int?)50);

            Assert.Equal("Lv <=50", result);
        }

        [Fact]
        public void FormatLevel_WithBothNull_ShouldReturnNull()
        {
            var result = InvokeStaticMethod<string>(typeof(SongSelectionStage), "FormatLevel", null, (int?)null);

            Assert.Null(result);
        }

        [Fact]
        public void FormatLevel_WithEqualMinMax_ShouldReturnRange()
        {
            var result = InvokeStaticMethod<string>(typeof(SongSelectionStage), "FormatLevel", 25, (int?)25);

            Assert.Equal("Lv 25-25", result);
        }

        [Fact]
        public void FormatSort_WithDefaultTitleAscending_ShouldReturnNull()
        {
            var result = InvokeStaticMethod<string>(typeof(SongSelectionStage), "FormatSort", SongSortCriteria.Title, false);

            Assert.Null(result);
        }

        [Fact]
        public void FormatSort_WithTitleDescending_ShouldReturnTitleV()
        {
            var result = InvokeStaticMethod<string>(typeof(SongSelectionStage), "FormatSort", SongSortCriteria.Title, true);

            Assert.Equal("Titlev", result);
        }

        [Fact]
        public void FormatSort_WithArtistAscending_ShouldReturnArtistCaret()
        {
            var result = InvokeStaticMethod<string>(typeof(SongSelectionStage), "FormatSort", SongSortCriteria.Artist, false);

            Assert.Equal("Artist^", result);
        }

        [Fact]
        public void FormatSort_WithGenreDescending_ShouldReturnGenrev()
        {
            var result = InvokeStaticMethod<string>(typeof(SongSelectionStage), "FormatSort", SongSortCriteria.Genre, true);

            Assert.Equal("Genrev", result);
        }

        [Fact]
        public void FormatSort_WithLevelAscending_ShouldReturnLevelCaret()
        {
            var result = InvokeStaticMethod<string>(typeof(SongSelectionStage), "FormatSort", SongSortCriteria.Level, false);

            Assert.Equal("Level^", result);
        }

        [Fact]
        public void ClampSelectionIndex_WithNullPreviousSelected_ShouldReturnZero()
        {
            var list = new List<SongListNode> { CreateScoreNode("A") };

            var result = SongSelectionStage.ClampSelectionIndex(null, list);

            Assert.Equal(0, result);
        }

        [Fact]
        public void ClampSelectionIndex_WithNullNewList_ShouldReturnZero()
        {
            var node = CreateScoreNode("A");

            var result = SongSelectionStage.ClampSelectionIndex(node, null);

            Assert.Equal(0, result);
        }

        [Fact]
        public void ClampSelectionIndex_WithEmptyList_ShouldReturnZero()
        {
            var node = CreateScoreNode("A");

            var result = SongSelectionStage.ClampSelectionIndex(node, new List<SongListNode>());

            Assert.Equal(0, result);
        }

        [Fact]
        public void ClampSelectionIndex_WithPreviousSelectedFound_ShouldReturnIndex()
        {
            var nodeA = CreateScoreNode("A");
            var nodeB = CreateScoreNode("B");
            var nodeC = CreateScoreNode("C");
            var list = new List<SongListNode> { nodeA, nodeB, nodeC };

            var result = SongSelectionStage.ClampSelectionIndex(nodeB, list);

            Assert.Equal(1, result);
        }

        [Fact]
        public void ClampSelectionIndex_WithPreviousSelectedNotFound_ShouldReturnZero()
        {
            var nodeA = CreateScoreNode("A");
            var nodeB = CreateScoreNode("B");
            var nodeC = CreateScoreNode("C");
            var nodeD = CreateScoreNode("D");
            var list = new List<SongListNode> { nodeA, nodeB, nodeC };

            var result = SongSelectionStage.ClampSelectionIndex(nodeD, list);

            Assert.Equal(0, result);
        }

        [Fact]
        public void ClampSelectionIndex_WithBothNull_ShouldReturnZero()
        {
            var result = SongSelectionStage.ClampSelectionIndex(null, null);

            Assert.Equal(0, result);
        }

        [Fact]
        public void HandleActivateInput_WhenInStatusPanelWithScoreSong_ShouldCallSelectSong()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = new SongSelectionStage(game);
            var stageManager = new Mock<IStageManager>();
            var selectedSong = CreateScoreNode("Song");

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_selectedSong", selectedSong);
            SetPrivateField(stage, "_currentDifficulty", 0);
            SetPrivateField(stage, "_isInStatusPanel", true);

            InvokePrivateMethod(stage, "HandleActivateInput");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Once);
        }

        [Fact]
        public void HandleActivateInput_WhenNotInStatusPanelWithScoreSong_ShouldEnterStatusPanel()
        {
            var stage = CreateStage();
            var statusPanel = new SongStatusPanel { Visible = false };
            var selectedSong = CreateScoreNode("Song");

            AttachCoreUi(stage, statusPanel: statusPanel);
            SetPrivateField(stage, "_selectedSong", selectedSong);
            SetPrivateField(stage, "_isInStatusPanel", false);

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.True(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.True(statusPanel.Visible);
        }

        [Fact]
        public void HandleActivateInput_WhenNotInStatusPanelWithNonScore_ShouldCallHandleSongActivation()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var child = CreateScoreNode("Child");
            var box = CreateBoxNode("Folder", child);

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_selectedSong", box);
            SetPrivateField(stage, "_isInStatusPanel", false);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { box });

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.Same(box.Children, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
        }

        [Fact]
        public void HandleActivateInput_WhenFilteredViewActiveAndNonScoreSelected_ShouldReturnEarly()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var box = CreateBoxNode("Folder");
            var filteredView = new List<FilteredSongResult> { new(CreateScoreNode("S"), "f") };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_filteredView", filteredView);
            SetPrivateField(stage, "_selectedSong", box);
            SetPrivateField(stage, "_isInStatusPanel", false);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { box });

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.NotSame(box.Children, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
        }

        [Fact]
        public void HandleActivateInput_WhenFilteredViewActiveAndScoreSelected_ShouldEnterStatusPanel()
        {
            var stage = CreateStage();
            var statusPanel = new SongStatusPanel { Visible = false };
            var song = CreateScoreNode("Song");
            var display = new SongListDisplay { CurrentList = new List<SongListNode> { song } };
            var filteredView = new List<FilteredSongResult> { new(song, "f") };

            AttachCoreUi(stage, display: display, statusPanel: statusPanel);
            SetPrivateField(stage, "_filteredView", filteredView);
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_isInStatusPanel", false);

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.True(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.True(statusPanel.Visible);
        }

        [Fact]
        public void ReleaseManagedSound_WithNullSound_ShouldNotThrow()
        {
            var method = typeof(SongSelectionStage).GetMethod("ReleaseManagedSound",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            object[] args = new object[] { null! };
            var exception = Record.Exception(() => method!.Invoke(null, args));

            Assert.Null(exception);
            Assert.Null(args[0]);
        }

        [Fact]
        public void ReleaseManagedSound_WithNonNullSound_ShouldCallRemoveReferenceAndClear()
        {
            var mockSound = new Mock<ISound>();
            var method = typeof(SongSelectionStage).GetMethod("ReleaseManagedSound",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);

            object[] args = new object[] { mockSound.Object };
            method!.Invoke(null, args);

            mockSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(args[0]);
        }

        [Fact]
        public void CreatePreviewSoundInstance_WithNullSound_ShouldReturnNull()
        {
            var result = InvokeStaticMethod<ISoundInstance>(typeof(SongSelectionStage), "CreatePreviewSoundInstance", new object?[] { null });

            Assert.Null(result);
        }

        [Fact]
        public void CreatePreviewSoundInstance_WithSoundThatReturnsNullInstance_ShouldReturnNull()
        {
            var mockSound = new Mock<ISound>();
            mockSound.Setup(x => x.CreateInstance()).Returns((SoundEffectInstance?)null);

            var result = InvokeStaticMethod<object>(typeof(SongSelectionStage), "CreatePreviewSoundInstance", mockSound.Object);

            Assert.Null(result);
        }

        [Fact]
        public void OnScrollSpeedChanged_ShouldNotThrow()
        {
            var stage = CreateStage();
            var args = new ScrollSpeedChangedEventArgs(50, 75);

            var exception = Record.Exception(() =>
                InvokePrivateMethod(stage, "OnScrollSpeedChanged", stage, args));

            Assert.Null(exception);
        }
    }
}
