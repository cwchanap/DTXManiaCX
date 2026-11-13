using System;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Song.Components
{
    [Trait("Category", "Unit")]
    public class SongSearchFilterModalExtendedTests
    {
        #region HandleClick – PlayedStatus and SortDirection fallthrough

        [Fact]
        public void HandleClick_WhenClickPlayedStatusArea_ShouldFocusPlayedStatus()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            // PlayedRowY=144, level row bottom=130. Click at relY=150 → PlayedStatus
            // modal Y=180, so position.Y = 180+150 = 330
            var clicked = modal.HandleClick(new Point(470, 330));

            Assert.True(clicked);
            Assert.Equal(SongSearchFilterModal.Field.PlayedStatus, modal.FocusedField);
        }

        [Fact]
        public void HandleClick_WhenClickBelowSortRow_ShouldFocusSortDirection()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            // SortRowY+30=218. Click at relY=240 → fallthrough to SortDirection
            // modal Y=180, so position.Y = 180+240 = 420
            // relX=130 which is < FieldX+180=310, so in FieldFromPosition it hits the
            // SortBy check first but relY >= 218, falls through to default
            var clicked = modal.HandleClick(new Point(470, 420));

            Assert.True(clicked);
            Assert.Equal(SongSearchFilterModal.Field.SortDirection, modal.FocusedField);
        }

        #endregion

        #region HandleCommand MoveLeft/MoveRight on MaxLevel

        [Fact]
        public void HandleCommand_WhenMoveRightOnMaxLevel_ShouldIncrementBy5()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            modal.FocusNext(); // MaxLevel

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(5, modal.CurrentDraft.MaxLevel);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnMaxLevel_ShouldDecrementBy5()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { MaxLevel = 15 });
            modal.FocusNext(); // MinLevel
            modal.FocusNext(); // MaxLevel

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.Equal(10, modal.CurrentDraft.MaxLevel);
        }

        #endregion

        #region HandleCommand MoveLeft/MoveRight on PlayedStatus

        [Fact]
        public void HandleCommand_WhenMoveRightOnPlayedStatus_ShouldCycleForward()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 3; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.PlayedStatus, modal.FocusedField);

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(PlayedStatus.Unplayed, modal.CurrentDraft.PlayedStatus);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnPlayedStatus_ShouldCycleBackward()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 3; i++) modal.FocusNext();

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.Equal(PlayedStatus.Cleared, modal.CurrentDraft.PlayedStatus);
        }

        #endregion

        #region HandleCommand MoveLeft/MoveRight on SortBy

        [Fact]
        public void HandleCommand_WhenMoveRightOnSortBy_ShouldCycleSortCriteria()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 4; i++) modal.FocusNext();

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(SongSortCriteria.Artist, modal.CurrentDraft.SortBy);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnSortBy_ShouldCycleSortCriteriaBackward()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 4; i++) modal.FocusNext();

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.Equal(SongSortCriteria.Level, modal.CurrentDraft.SortBy);
        }

        #endregion

        #region HandleCommand MoveLeft/MoveRight on SortDirection

        [Fact]
        public void HandleCommand_WhenMoveRightOnSortDirection_ShouldToggleDescending()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 5; i++) modal.FocusNext();

            Assert.False(modal.CurrentDraft.SortDescending);
            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.True(modal.CurrentDraft.SortDescending);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnSortDirection_ShouldToggleDescending()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { SortDescending = true });
            for (int i = 0; i < 5; i++) modal.FocusNext();

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.False(modal.CurrentDraft.SortDescending);
        }

        #endregion

        #region HandleCommand Activate on numeric/cycle fields (default case in HandleEnter)

        [Fact]
        public void HandleCommand_WhenActivateOnMinLevel_ShouldApply()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate);

            Assert.True(applied);
        }

        [Fact]
        public void HandleCommand_WhenActivateOnMaxLevel_ShouldApply()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            modal.FocusNext(); // MaxLevel
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate);

            Assert.True(applied);
        }

        [Fact]
        public void HandleCommand_WhenActivateOnPlayedStatus_ShouldApply()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 3; i++) modal.FocusNext();
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate);

            Assert.True(applied);
        }

        [Fact]
        public void HandleCommand_WhenActivateOnSortBy_ShouldApply()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 4; i++) modal.FocusNext();
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate);

            Assert.True(applied);
        }

        [Fact]
        public void HandleCommand_WhenActivateOnSortDirection_ShouldApply()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 5; i++) modal.FocusNext();
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate);

            Assert.True(applied);
        }

        [Fact]
        public void HandleCommand_WhenActivateOnMinLevelAndLibraryNotReady_ShouldNotApply()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate);

            Assert.False(applied);
            Assert.True(modal.IsOpen);
        }

        #endregion

        #region HandleKey Enter on SortBy and SortDirection

        [Fact]
        public void HandleKey_WhenEnterOnSortBy_ShouldApply()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 4; i++) modal.FocusNext();
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.True(applied);
        }

        [Fact]
        public void HandleKey_WhenEnterOnSortDirection_ShouldApply()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 5; i++) modal.FocusNext();
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.True(applied);
        }

        #endregion

        #region AdjustLevel boundary conditions

        [Fact]
        public void AdjustLevel_WhenNullAndDecrement_ShouldClampToNull()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            Assert.Null(modal.CurrentDraft.MinLevel);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);

            Assert.Null(modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void AdjustLevel_WhenAt99AndIncrement_ShouldClampAt99()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 99 });
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);

            Assert.Equal(99, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void AdjustLevel_WhenAt99AndIncrementViaCommand_ShouldClampAt99()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 99 });
            modal.FocusNext(); // MinLevel

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(99, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void AdjustLevel_WhenAt1AndDecrement_ShouldReturnNull()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 1 });
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);

            Assert.Null(modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void AdjustLevel_WhenAtNullAndIncrementMaxLevel_ShouldSetTo5()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            modal.FocusNext(); // MaxLevel
            Assert.Null(modal.CurrentDraft.MaxLevel);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);

            Assert.Equal(5, modal.CurrentDraft.MaxLevel);
        }

        [Fact]
        public void AdjustLevel_WhenNullAndDecrementMaxLevelViaCommand_ShouldStayNull()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            modal.FocusNext(); // MaxLevel

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.Null(modal.CurrentDraft.MaxLevel);
        }

        #endregion

        #region PlayedStatus cycling edge cases

        [Fact]
        public void PlayedStatus_WhenCycleBackwardFromAll_ShouldWrapToCleared()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            Assert.Equal(PlayedStatus.All, modal.CurrentDraft.PlayedStatus);
            for (int i = 0; i < 3; i++) modal.FocusNext();

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);

            Assert.Equal(PlayedStatus.Cleared, modal.CurrentDraft.PlayedStatus);
        }

        [Fact]
        public void PlayedStatus_WhenCycleForwardFullLoop_ShouldReturnToAll()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 3; i++) modal.FocusNext();

            // All → Unplayed → Played → Cleared → All
            for (int i = 0; i < 4; i++)
                modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(PlayedStatus.All, modal.CurrentDraft.PlayedStatus);
        }

        #endregion

        #region CycleSort edge cases

        [Fact]
        public void CycleSort_WhenBackwardFromTitle_ShouldWrapToLevel()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            Assert.Equal(SongSortCriteria.Title, modal.CurrentDraft.SortBy);
            for (int i = 0; i < 4; i++) modal.FocusNext();

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.Equal(SongSortCriteria.Level, modal.CurrentDraft.SortBy);
        }

        [Fact]
        public void CycleSort_WhenForwardFullLoop_ShouldReturnToTitle()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 4; i++) modal.FocusNext();

            // Title → Artist → Level → Title
            for (int i = 0; i < 3; i++)
                modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(SongSortCriteria.Title, modal.CurrentDraft.SortBy);
        }

        [Fact]
        public void CycleSort_WhenBackwardFromGenreViaHandleCommand_ShouldResetToLevel()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { SortBy = SongSortCriteria.Genre });
            for (int i = 0; i < 4; i++) modal.FocusNext();

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.Equal(SongSortCriteria.Level, modal.CurrentDraft.SortBy);
        }

        #endregion

        #region AdjustFocusedField no-op on non-adjustable fields

        [Fact]
        public void HandleKey_WhenLeftOnSearchBox_ShouldNotModifyDraft()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { SearchQuery = "test" });

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);

            Assert.Equal("test", modal.CurrentDraft.SearchQuery);
            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_WhenRightOnSearchBox_ShouldNotModifyDraft()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { SearchQuery = "test" });

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);

            Assert.Equal("test", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnResetButton_ShouldNotModifyDraft()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 10 });
            for (int i = 0; i < 6; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.ResetButton, modal.FocusedField);

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.Equal(10, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void HandleCommand_WhenMoveRightOnResetButton_ShouldNotModifyDraft()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 10 });
            for (int i = 0; i < 6; i++) modal.FocusNext();

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(10, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnApplyButton_ShouldNotModifyDraft()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { MaxLevel = 50 });
            for (int i = 0; i < 7; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.ApplyButton, modal.FocusedField);

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.Equal(50, modal.CurrentDraft.MaxLevel);
        }

        [Fact]
        public void HandleCommand_WhenMoveRightOnApplyButton_ShouldNotModifyDraft()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { MaxLevel = 50 });
            for (int i = 0; i < 7; i++) modal.FocusNext();

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(50, modal.CurrentDraft.MaxLevel);
        }

        #endregion

        #region SortDirection toggle via HandleKey vs HandleCommand consistency

        [Fact]
        public void SortDirection_WhenToggledTwiceViaHandleKey_ShouldReturnToOriginal()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 5; i++) modal.FocusNext();

            Assert.False(modal.CurrentDraft.SortDescending);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.True(modal.CurrentDraft.SortDescending);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.False(modal.CurrentDraft.SortDescending);
        }

        [Fact]
        public void SortDirection_WhenToggledTwiceViaHandleCommand_ShouldReturnToOriginal()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default with { SortDescending = true });
            for (int i = 0; i < 5; i++) modal.FocusNext();

            Assert.True(modal.CurrentDraft.SortDescending);
            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);
            Assert.False(modal.CurrentDraft.SortDescending);
            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);
            Assert.True(modal.CurrentDraft.SortDescending);
        }

        #endregion

        [Fact]
        public void HandleInput_WhenClosed_ShouldReturnFalse()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());

            Assert.False(modal.HandleInput(null!));
        }

        [Fact]
        public void HandleInput_WhenOpen_ShouldReturnTrue()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);

            Assert.True(modal.HandleInput(null!));
        }

        [Fact]
        public void HandleInput_AfterDispose_WhenStillOpen_ShouldReturnTrue()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            modal.Dispose();

            Assert.True(modal.HandleInput(null!));
        }

        [Fact]
        public void Dispose_WhenModalWasOpen_ShouldUnsubscribeFromTextSource()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            src.Fire('a');
            Assert.Equal("a", modal.CurrentDraft.SearchQuery);

            modal.Dispose();

            src.Fire('b');
            Assert.Equal("a", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Dispose_WhenModalWasNeverOpened_ShouldNotThrow()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());

            var ex = Record.Exception(() => modal.Dispose());

            Assert.Null(ex);
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            modal.Dispose();

            var ex = Record.Exception(() => modal.Dispose());

            Assert.Null(ex);
        }

        [Fact]
        public void LoadingHintText_DefaultShouldBeLibraryStillLoading()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());

            Assert.Equal("Library still loading…", modal.LoadingHintText);
        }

        [Fact]
        public void LoadingHintText_ShouldBeSettable()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource())
            {
                LoadingHintText = "Please wait…"
            };

            Assert.Equal("Please wait…", modal.LoadingHintText);
        }

        [Fact]
        public void IsLibraryReady_DefaultShouldBeTrue()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());

            Assert.True(modal.IsLibraryReady);
        }

        [Fact]
        public void IsLibraryReady_ShouldBeSettable()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource())
            {
                IsLibraryReady = false
            };

            Assert.False(modal.IsLibraryReady);
        }

        [Fact]
        public void IsLibraryReady_SetBackToTrue_ShouldReflectChange()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource())
            {
                IsLibraryReady = false
            };
            modal.IsLibraryReady = true;

            Assert.True(modal.IsLibraryReady);
        }

        [Fact]
        public void WhitePixel_ShouldBeNullByDefault()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());

            Assert.Null(modal.WhitePixel);
        }

        [Fact]
        public void WhitePixel_ShouldBeSettable()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());

            modal.WhitePixel = null;

            Assert.Null(modal.WhitePixel);
        }

        [Fact]
        public void Font_ShouldBeNullByDefault()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());

            Assert.Null(modal.Font);
        }

        [Fact]
        public void Font_ShouldBeSettable()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());

            modal.Font = null;

            Assert.Null(modal.Font);
        }

        [Fact]
        public void SubmitFromSearchBox_WhenLibraryNotReady_ShouldNotFireFilterApplied()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "test" });
            bool applied = false;
            bool reset = false;
            modal.FilterApplied += (_, _) => applied = true;
            modal.FilterReset += (_, _) => reset = true;

            modal.SubmitFromSearchBox();

            Assert.False(applied);
            Assert.False(reset);
            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void SubmitFromSearchBox_WhenLibraryNotReadyWithQCommand_ShouldNotFireReset()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "/q" });
            bool reset = false;
            modal.FilterReset += (_, _) => reset = true;

            modal.SubmitFromSearchBox();

            Assert.False(reset);
            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void SubmitFromSearchBox_WhenLibraryReadyAndQCommand_ShouldFireReset()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource()) { IsLibraryReady = true };
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "/q" });
            bool reset = false;
            modal.FilterReset += (_, _) => reset = true;

            modal.SubmitFromSearchBox();

            Assert.True(reset);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void Apply_WhenLibraryNotReady_ShouldNotFireFilterApplied()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.Apply();

            Assert.False(applied);
        }

        [Fact]
        public void Apply_WhenLibraryNotReady_ShouldNotClose()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);

            modal.Apply();

            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void Apply_WhenLibraryNotReady_ViaEnterOnDefaultField_ShouldStayOpen()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel → default case in HandleEnter calls Apply
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.False(applied);
            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void Apply_WhenLibraryNotReady_ViaCommandOnApplyButton_ShouldStayOpen()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 7; i++) modal.FocusNext();
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate);

            Assert.False(applied);
            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void OnTextInput_WhenModalClosed_ShouldNotModifyDraft()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "existing" });
            modal.Cancel();

            src.Fire('z');

            Assert.Equal("existing", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenFocusedOnMinLevel_ShouldNotModifySearchQuery()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "abc" });
            modal.FocusNext(); // MinLevel

            src.Fire('x');

            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenFocusedOnMaxLevel_ShouldNotModifySearchQuery()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "abc" });
            modal.FocusNext();
            modal.FocusNext(); // MaxLevel

            src.Fire('x');

            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenFocusedOnPlayedStatus_ShouldNotModifySearchQuery()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "abc" });
            for (int i = 0; i < 3; i++) modal.FocusNext(); // PlayedStatus

            src.Fire('x');

            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenFocusedOnSortBy_ShouldNotModifySearchQuery()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "abc" });
            for (int i = 0; i < 4; i++) modal.FocusNext(); // SortBy

            src.Fire('x');

            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenFocusedOnSortDirection_ShouldNotModifySearchQuery()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "abc" });
            for (int i = 0; i < 5; i++) modal.FocusNext(); // SortDirection

            src.Fire('x');

            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenFocusedOnResetButton_ShouldNotModifySearchQuery()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "abc" });
            for (int i = 0; i < 6; i++) modal.FocusNext(); // ResetButton

            src.Fire('x');

            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenFocusedOnApplyButton_ShouldNotModifySearchQuery()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "abc" });
            for (int i = 0; i < 7; i++) modal.FocusNext(); // ApplyButton

            src.Fire('x');

            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleBackspace_WhenQueryIsEmpty_ShouldNotModifyDraft()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            Assert.Equal("", modal.CurrentDraft.SearchQuery);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleBackspace_WhenQueryIsNull_ShouldNotModifyDraft()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            var criteria = SongFilterCriteria.Default with { SearchQuery = null };
            modal.Open(criteria);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Null(modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleBackspace_WhenFocusedOnMinLevel_ShouldNotModifySearchQuery()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            src.Fire('a');
            src.Fire('b');
            Assert.Equal("ab", modal.CurrentDraft.SearchQuery);
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("ab", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleBackspace_WhenFocusedOnPlayedStatus_ShouldNotModifySearchQuery()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            src.Fire('h');
            src.Fire('i');
            for (int i = 0; i < 3; i++) modal.FocusNext(); // PlayedStatus

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("hi", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleBackspace_WhenFocusedOnSortBy_ShouldNotModifySearchQuery()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            src.Fire('x');
            for (int i = 0; i < 4; i++) modal.FocusNext(); // SortBy

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("x", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenSearchQueryIsNull_ShouldAppendSuccessfully()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            var criteria = SongFilterCriteria.Default with { SearchQuery = null };
            modal.Open(criteria);

            src.Fire('a');

            Assert.Equal("a", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenSearchQueryIsNullAndMultipleChars_ShouldBuildString()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            var criteria = SongFilterCriteria.Default with { SearchQuery = null };
            modal.Open(criteria);

            src.Fire('a');
            src.Fire('b');
            src.Fire('c');

            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Close_ThenReopen_ShouldResubscribeToTextSource()
        {
            var src = new FakeTextInputSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            src.Fire('a');
            Assert.Equal("a", modal.CurrentDraft.SearchQuery);
            modal.Cancel();

            modal.Open(SongFilterCriteria.Default);
            src.Fire('b');

            Assert.Equal("b", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleKey_WhenNotOpen_ShouldNotProcessBackspace()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            var src = new FakeTextInputSource();
            modal.Open(SongFilterCriteria.Default);
            modal.Cancel();

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Visible_WhenConstructed_ShouldBeFalse()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());

            Assert.False(modal.Visible);
        }

        [Fact]
        public void Visible_WhenOpened_ShouldBeTrue()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);

            Assert.True(modal.Visible);
        }

        [Fact]
        public void Visible_AfterClose_ShouldBeFalse()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            modal.Close();

            Assert.False(modal.Visible);
        }

        [Fact]
        public void IsOpen_WhenConstructed_ShouldBeFalse()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());

            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void CurrentDraft_WhenNotOpened_ShouldBeDefault()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());

            Assert.Equal(SongFilterCriteria.Default, modal.CurrentDraft);
        }

        [Fact]
        public void FocusedField_WhenNotOpened_ShouldBeSearchBox()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());

            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void Open_WithNonNullInitial_ShouldSetDraft()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            var criteria = SongFilterCriteria.Default with
            {
                SearchQuery = "hello",
                MinLevel = 15,
                MaxLevel = 80,
                PlayedStatus = PlayedStatus.Played,
                SortBy = SongSortCriteria.Level,
                SortDescending = true
            };

            modal.Open(criteria);

            Assert.Equal("hello", modal.CurrentDraft.SearchQuery);
            Assert.Equal(15, modal.CurrentDraft.MinLevel);
            Assert.Equal(80, modal.CurrentDraft.MaxLevel);
            Assert.Equal(PlayedStatus.Played, modal.CurrentDraft.PlayedStatus);
            Assert.Equal(SongSortCriteria.Level, modal.CurrentDraft.SortBy);
            Assert.True(modal.CurrentDraft.SortDescending);
        }

        [Fact]
        public void Cancelled_WhenCancelled_ShouldFireEvent()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource());
            modal.Open(SongFilterCriteria.Default);
            object? sender = null;
            modal.Cancelled += (s, _) => sender = s;

            modal.Cancel();

            Assert.Same(modal, sender);
        }

        [Fact]
        public void HandleCommand_WhenLibraryNotReady_ShouldStillCancelOnBack()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            bool cancelled = false;
            modal.Cancelled += (_, _) => cancelled = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Back);

            Assert.True(cancelled);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void SubmitFromSearchBox_WhenLibraryNotReady_ViaHandleKeyEnter_ShouldStayOpen()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "test" });
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.False(applied);
            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void Reset_WhenInvokedWithLibraryNotReady_ShouldStillReset()
        {
            var modal = new SongSearchFilterModal(new FakeTextInputSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            bool reset = false;
            modal.FilterReset += (_, _) => reset = true;

            modal.Reset();

            Assert.True(reset);
            Assert.False(modal.IsOpen);
        }
    }
}
