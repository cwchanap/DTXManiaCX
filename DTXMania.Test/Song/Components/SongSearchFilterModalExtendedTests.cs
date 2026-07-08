using System;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Song.Components
{
    [Trait("Category", "Unit")]
    public class SongSearchFilterModalExtendedTests
    {
        private sealed class FakeSource : ITextInputSource
        {
            public event EventHandler<TextInputEventArgs>? TextInput;
            public void Fire(char c) =>
                TextInput?.Invoke(this, new TextInputEventArgs(c,
                    Microsoft.Xna.Framework.Input.Keys.None));
            public void Dispose() { }
        }

        #region HandleClick – PlayedStatus and SortDirection fallthrough

        [Fact]
        public void HandleClick_WhenClickPlayedStatusArea_ShouldFocusPlayedStatus()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            modal.FocusNext(); // MaxLevel

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(5, modal.CurrentDraft.MaxLevel);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnMaxLevel_ShouldDecrementBy5()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 3; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.PlayedStatus, modal.FocusedField);

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(PlayedStatus.Unplayed, modal.CurrentDraft.PlayedStatus);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnPlayedStatus_ShouldCycleBackward()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 4; i++) modal.FocusNext();

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(SongSortCriteria.Artist, modal.CurrentDraft.SortBy);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnSortBy_ShouldCycleSortCriteriaBackward()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 5; i++) modal.FocusNext();

            Assert.False(modal.CurrentDraft.SortDescending);
            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.True(modal.CurrentDraft.SortDescending);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnSortDirection_ShouldToggleDescending()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
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
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            Assert.Null(modal.CurrentDraft.MinLevel);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);

            Assert.Null(modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void AdjustLevel_WhenAt99AndIncrement_ShouldClampAt99()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 99 });
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);

            Assert.Equal(99, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void AdjustLevel_WhenAt99AndIncrementViaCommand_ShouldClampAt99()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 99 });
            modal.FocusNext(); // MinLevel

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(99, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void AdjustLevel_WhenAt1AndDecrement_ShouldReturnNull()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 1 });
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);

            Assert.Null(modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void AdjustLevel_WhenAtNullAndIncrementMaxLevel_ShouldSetTo5()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            Assert.Equal(PlayedStatus.All, modal.CurrentDraft.PlayedStatus);
            for (int i = 0; i < 3; i++) modal.FocusNext();

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);

            Assert.Equal(PlayedStatus.Cleared, modal.CurrentDraft.PlayedStatus);
        }

        [Fact]
        public void PlayedStatus_WhenCycleForwardFullLoop_ShouldReturnToAll()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            Assert.Equal(SongSortCriteria.Title, modal.CurrentDraft.SortBy);
            for (int i = 0; i < 4; i++) modal.FocusNext();

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.Equal(SongSortCriteria.Level, modal.CurrentDraft.SortBy);
        }

        [Fact]
        public void CycleSort_WhenForwardFullLoop_ShouldReturnToTitle()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { SearchQuery = "test" });

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);

            Assert.Equal("test", modal.CurrentDraft.SearchQuery);
            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_WhenRightOnSearchBox_ShouldNotModifyDraft()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { SearchQuery = "test" });

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);

            Assert.Equal("test", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnResetButton_ShouldNotModifyDraft()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 10 });
            for (int i = 0; i < 6; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.ResetButton, modal.FocusedField);

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.Equal(10, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void HandleCommand_WhenMoveRightOnResetButton_ShouldNotModifyDraft()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 10 });
            for (int i = 0; i < 6; i++) modal.FocusNext();

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(10, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnApplyButton_ShouldNotModifyDraft()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { MaxLevel = 50 });
            for (int i = 0; i < 7; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.ApplyButton, modal.FocusedField);

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.Equal(50, modal.CurrentDraft.MaxLevel);
        }

        [Fact]
        public void HandleCommand_WhenMoveRightOnApplyButton_ShouldNotModifyDraft()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { SortDescending = true });
            for (int i = 0; i < 5; i++) modal.FocusNext();

            Assert.True(modal.CurrentDraft.SortDescending);
            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);
            Assert.False(modal.CurrentDraft.SortDescending);
            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);
            Assert.True(modal.CurrentDraft.SortDescending);
        }

        #endregion
    }
}
