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
    public class SongSearchFilterModalLogicTests
    {
        private sealed class FakeSource : ITextInputSource
        {
            public event EventHandler<TextInputEventArgs>? TextInput;
            public void Fire(char c) =>
                TextInput?.Invoke(this, new TextInputEventArgs(c,
                    Microsoft.Xna.Framework.Input.Keys.None));
            public void Dispose() { }
        }

        [Fact]
        public void Open_WithDefaultCriteria_ShouldPopulateFromArgument()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            var initial = SongFilterCriteria.Default with { SearchQuery = "abc" };

            modal.Open(initial);

            Assert.True(modal.IsOpen);
            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Cancel_WhenInvoked_ShouldRaiseCancelledAndClose()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            bool fired = false;
            modal.Cancelled += (_, _) => fired = true;

            modal.Cancel();

            Assert.True(fired);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void Apply_WhenInvoked_ShouldFireFilterAppliedWithDraftAndClose()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            var initial = SongFilterCriteria.Default with { SearchQuery = "x" };
            modal.Open(initial);
            SongFilterCriteria? captured = null;
            modal.FilterApplied += (_, c) => captured = c;

            modal.Apply();

            Assert.NotNull(captured);
            Assert.Equal("x", captured!.SearchQuery);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void Reset_WhenInvoked_ShouldFireFilterResetAndClose()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { SearchQuery = "x" });
            bool fired = false;
            modal.FilterReset += (_, _) => fired = true;

            modal.Reset();

            Assert.True(fired);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void EditDraft_WhenUpdated_ShouldNotMutateInitialCriteria()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            var initial = SongFilterCriteria.Default with { SearchQuery = "orig" };

            modal.Open(initial);
            modal.UpdateDraft(initial with { SearchQuery = "edited" });

            Assert.Equal("orig", initial.SearchQuery);
            Assert.Equal("edited", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void SubmitFromSearchBox_WhenQSlashCommand_ShouldTriggerReset()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "/q" });
            bool resetFired = false;
            bool appliedFired = false;
            modal.FilterReset += (_, _) => resetFired = true;
            modal.FilterApplied += (_, _) => appliedFired = true;

            modal.SubmitFromSearchBox();

            Assert.True(resetFired);
            Assert.False(appliedFired);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void SubmitFromSearchBox_WhenQSlashCommandCaseInsensitive_ShouldTriggerReset()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "/Q" });
            bool resetFired = false;
            modal.FilterReset += (_, _) => resetFired = true;

            modal.SubmitFromSearchBox();

            Assert.True(resetFired);
        }

        [Fact]
        public void SubmitFromSearchBox_WhenNormalQuery_ShouldTriggerApply()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "beatles" });
            bool resetFired = false;
            bool appliedFired = false;
            modal.FilterReset += (_, _) => resetFired = true;
            modal.FilterApplied += (_, _) => appliedFired = true;

            modal.SubmitFromSearchBox();

            Assert.False(resetFired);
            Assert.True(appliedFired);
        }

        [Fact]
        public void SubmitFromSearchBox_WhenQPrefixedQuery_ShouldNotBeResetCommand()
        {
            // "/quiet" is a literal search, not /q
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "/quiet" });
            bool resetFired = false;
            bool appliedFired = false;
            modal.FilterReset += (_, _) => resetFired = true;
            modal.FilterApplied += (_, _) => appliedFired = true;

            modal.SubmitFromSearchBox();

            Assert.False(resetFired);
            Assert.True(appliedFired);
        }

        [Fact]
        public void Open_WhenOpened_ShouldInitiallyFocusSearchBox()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void FocusNext_WhenCalled_ShouldCycleThroughFieldsInOrder()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            var order = new[]
            {
                SongSearchFilterModal.Field.SearchBox,
                SongSearchFilterModal.Field.MinLevel,
                SongSearchFilterModal.Field.MaxLevel,
                SongSearchFilterModal.Field.PlayedStatus,
                SongSearchFilterModal.Field.SortBy,
                SongSearchFilterModal.Field.SortDirection,
                SongSearchFilterModal.Field.ResetButton,
                SongSearchFilterModal.Field.ApplyButton
            };

            for (int i = 0; i < order.Length; i++)
            {
                Assert.Equal(order[i], modal.FocusedField);
                modal.FocusNext();
            }
            // After last, wraps back to SearchBox
            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void FocusPrev_WhenCalled_ShouldReverseOfFocusNext()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.FocusPrev();
            Assert.Equal(SongSearchFilterModal.Field.ApplyButton, modal.FocusedField);
            modal.FocusPrev();
            Assert.Equal(SongSearchFilterModal.Field.ResetButton, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_WhenEscape_ShouldFireCancel()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            bool fired = false;
            modal.Cancelled += (_, _) => fired = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Escape);

            Assert.True(fired);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void HandleKey_WhenTab_ShouldAdvanceFocus()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Tab);

            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_WhenDown_ShouldAdvanceFocus()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Down);

            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_WhenUp_ShouldRetreatFocus()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Up);

            Assert.Equal(SongSearchFilterModal.Field.ApplyButton, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_WhenEnterOnSearchBox_ShouldSubmitFromSearchBox()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "abc" });
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.True(applied);
        }

        [Fact]
        public void HandleKey_WhenEnterOnApplyButton_ShouldFireApply()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            // Move focus to ApplyButton
            for (int i = 0; i < 7; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.ApplyButton, modal.FocusedField);

            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.True(applied);
        }

        [Fact]
        public void HandleKey_WhenEnterOnResetButton_ShouldFireReset()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            // Move focus to ResetButton
            for (int i = 0; i < 6; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.ResetButton, modal.FocusedField);

            bool reset = false;
            modal.FilterReset += (_, _) => reset = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.True(reset);
        }

        [Fact]
        public void HandleKey_WhenLeftRightOnMinLevel_ShouldAdjustBy5()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(5, modal.CurrentDraft.MinLevel);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(10, modal.CurrentDraft.MinLevel);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);
            Assert.Equal(5, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void HandleKey_WhenLeftRightOnPlayedStatus_ShouldCycleEnum()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 3; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.PlayedStatus, modal.FocusedField);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(PlayedStatus.Unplayed, modal.CurrentDraft.PlayedStatus);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(PlayedStatus.Played, modal.CurrentDraft.PlayedStatus);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(PlayedStatus.Cleared, modal.CurrentDraft.PlayedStatus);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            // wraps
            Assert.Equal(PlayedStatus.All, modal.CurrentDraft.PlayedStatus);
        }

        [Fact]
        public void TypingChars_WhenOnSearchBox_ShouldAppendToSearchQuery()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            // SearchBox is initial focus
            src.Fire('h');
            src.Fire('i');

            Assert.Equal("hi", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void TypingChars_WhenNotOnSearchBox_ShouldBeIgnored()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel

            src.Fire('5');

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Backspace_WhenHandleKey_ShouldRemoveLastSearchChar()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            src.Fire('a');
            src.Fire('b');

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("a", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Close_WhenClosed_ShouldUnsubscribeFromTextSource()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            src.Fire('a');
            Assert.Equal("a", modal.CurrentDraft.SearchQuery);

            modal.Cancel(); // closes

            src.Fire('b');
            // After close, characters must not affect the now-stale draft
            Assert.Equal("a", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Apply_WhenLibraryNotReady_ShouldNotFire()
        {
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            bool fired = false;
            modal.FilterApplied += (_, _) => fired = true;

            modal.Apply();

            Assert.False(fired);
            Assert.True(modal.IsOpen); // stays open
        }

        [Fact]
        public void Reset_WhenLibraryNotReady_ShouldStillWork()
        {
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            bool fired = false;
            modal.FilterReset += (_, _) => fired = true;

            modal.Reset();

            Assert.True(fired);
        }

        [Fact]
        public void HandleKey_WhenNotOpen_ShouldBeNoOp()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            bool cancelled = false;
            modal.Cancelled += (_, _) => cancelled = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Escape);

            Assert.False(cancelled);
        }

        [Fact]
        public void HandleKey_WhenLeftRightOnMaxLevel_ShouldAdjustBy5()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            modal.FocusNext(); // MaxLevel
            Assert.Equal(SongSearchFilterModal.Field.MaxLevel, modal.FocusedField);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(5, modal.CurrentDraft.MaxLevel);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(10, modal.CurrentDraft.MaxLevel);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);
            Assert.Equal(5, modal.CurrentDraft.MaxLevel);
        }

        [Fact]
        public void HandleKey_WhenLeftRightOnSortBy_ShouldCycleSortCriteria()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            // Navigate to SortBy (index 4)
            for (int i = 0; i < 4; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.SortBy, modal.FocusedField);

            Assert.Equal(SongSortCriteria.Title, modal.CurrentDraft.SortBy);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(SongSortCriteria.Artist, modal.CurrentDraft.SortBy);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(SongSortCriteria.Level, modal.CurrentDraft.SortBy);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(SongSortCriteria.Title, modal.CurrentDraft.SortBy); // wraps
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);
            Assert.Equal(SongSortCriteria.Level, modal.CurrentDraft.SortBy);
        }

        [Fact]
        public void HandleKey_WhenLeftRightOnSortDirection_ShouldToggle()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 5; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.SortDirection, modal.FocusedField);

            Assert.False(modal.CurrentDraft.SortDescending);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.True(modal.CurrentDraft.SortDescending);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);
            Assert.False(modal.CurrentDraft.SortDescending);
        }

        [Fact]
        public void HandleKey_WhenBackNotOnSearchBox_ShouldBeNoOp()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "abc" });
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleKey_WhenBackAndQueryEmpty_ShouldBeNoOp()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleKey_WhenEnterOnPlayedStatus_ShouldApply()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 3; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.PlayedStatus, modal.FocusedField);
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.True(applied);
        }

        [Fact]
        public void HandleKey_WhenEnterOnMinLevelField_ShouldApply()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.True(applied);
        }

        [Fact]
        public void HandleKey_WhenEnterOnMaxLevelField_ShouldApply()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            modal.FocusNext(); // MaxLevel
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.True(applied);
        }

        [Fact]
        public void OnTextInput_WhenControlCharacters_ShouldBeIgnored()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);

            src.Fire('\t');
            src.Fire('\r');
            src.Fire('\n');
            src.Fire('\u0001'); // control char

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenBackspaceCharCode_ShouldBeIgnored()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);

            src.Fire('\b');

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenModalClosed_ShouldBeIgnored()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.Cancel();

            src.Fire('x');

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void AdjustLevel_WhenAtMin_ShouldClampAt0()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);
            Assert.Null(modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void AdjustLevel_WhenAtMax_ShouldClampAt99()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 95 });
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(99, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void SubmitFromSearchBox_WhenLibraryNotReady_ShouldNotFire()
        {
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "test" });
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.SubmitFromSearchBox();

            Assert.False(applied);
            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void Dispose_WhenCalled_ShouldUnsubscribeFromSource()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            src.Fire('a');
            Assert.Equal("a", modal.CurrentDraft.SearchQuery);

            modal.Dispose();

            src.Fire('b');
            Assert.Equal("a", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Open_WhenNullInitial_ShouldUseDefault()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(null);

            Assert.True(modal.IsOpen);
            Assert.Equal(SongFilterCriteria.Default, modal.CurrentDraft);
        }

        [Fact]
        public void UpdateDraft_WhenNull_ShouldUseDefault()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { SearchQuery = "test" });
            modal.UpdateDraft(null);

            Assert.Equal(SongFilterCriteria.Default, modal.CurrentDraft);
        }

        [Fact]
        public void Constructor_WhenNullTextSource_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new SongSearchFilterModal(null!));
        }

        [Fact]
        public void CycleSort_WhenStartingFromGenre_ShouldResetToArtist()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { SortBy = SongSortCriteria.Genre });
            for (int i = 0; i < 4; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.SortBy, modal.FocusedField);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(SongSortCriteria.Artist, modal.CurrentDraft.SortBy);
        }

        [Fact]
        public void AdjustLevel_WhenDecrementToZero_ShouldReturnNull()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 5 });
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);
            Assert.Null(modal.CurrentDraft.MinLevel);
        }

        #region HandleCommand tests (command-based navigation)

        [Fact]
        public void HandleCommand_WhenMoveDown_ShouldAdvanceFocus()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveDown);

            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);
        }

        [Fact]
        public void HandleCommand_WhenMoveUp_ShouldRetreatFocus()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveUp);

            Assert.Equal(SongSearchFilterModal.Field.ApplyButton, modal.FocusedField);
        }

        [Fact]
        public void HandleCommand_WhenMoveLeftOnMinLevel_ShouldAdjust()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 10 });
            modal.FocusNext(); // MinLevel

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveLeft);

            Assert.Equal(5, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void HandleCommand_WhenMoveRightOnMinLevel_ShouldAdjust()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveRight);

            Assert.Equal(5, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void HandleCommand_WhenActivateOnSearchBox_ShouldSubmit()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { SearchQuery = "abc" });
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate);

            Assert.True(applied);
        }

        [Fact]
        public void HandleCommand_WhenActivateOnApplyButton_ShouldFireApply()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 7; i++) modal.FocusNext();
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate);

            Assert.True(applied);
        }

        [Fact]
        public void HandleCommand_WhenActivateOnResetButton_ShouldFireReset()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 6; i++) modal.FocusNext();
            bool reset = false;
            modal.FilterReset += (_, _) => reset = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate);

            Assert.True(reset);
        }

        [Fact]
        public void HandleCommand_WhenBack_ShouldFireCancel()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            bool fired = false;
            modal.Cancelled += (_, _) => fired = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Back);

            Assert.True(fired);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void HandleCommand_WhenNotOpen_ShouldBeNoOp()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            bool cancelled = false;
            modal.Cancelled += (_, _) => cancelled = true;

            modal.HandleCommand(DTXMania.Game.Lib.Input.InputCommandType.Back);

            Assert.False(cancelled);
        }

        #endregion

        #region HandleClick tests (mouse interaction)

        [Fact]
        public void HandleClick_WhenClickResetButton_ShouldFireReset()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            bool reset = false;
            modal.FilterReset += (_, _) => reset = true;

            // Reset button: modal X=340 + ResetButtonX=200 = 540, Y=180 + ButtonRowY=240 = 420
            var clicked = modal.HandleClick(new Microsoft.Xna.Framework.Point(600, 438));

            Assert.True(clicked);
            Assert.True(reset);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void HandleClick_WhenClickApplyButton_ShouldFireApply()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            // Apply button: modal X=340 + ApplyButtonX=360 = 700, Y=180 + ButtonRowY=240 = 420
            var clicked = modal.HandleClick(new Microsoft.Xna.Framework.Point(760, 438));

            Assert.True(clicked);
            Assert.True(applied);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void HandleClick_WhenClickInsideModalSearchArea_ShouldFocusSearchBox()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            // Click in search box area: modal Y=180 + SearchBoxY=56 + 10 = 246
            var clicked = modal.HandleClick(new Microsoft.Xna.Framework.Point(400, 246));

            Assert.True(clicked);
            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void HandleClick_WhenClickMinLevel_ShouldFocusMinLevel()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            // MinLevel: modal X=340 + LevelMinX=130 = 470, modal Y=180 + LevelRowY=100 + 10 = 290
            var clicked = modal.HandleClick(new Microsoft.Xna.Framework.Point(470, 290));

            Assert.True(clicked);
            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);
        }

        [Fact]
        public void HandleClick_WhenClickMaxLevel_ShouldFocusMaxLevel()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            // MaxLevel: modal X=340 + LevelMaxX=310 = 650, modal Y=180 + LevelRowY=100 + 10 = 290
            var clicked = modal.HandleClick(new Microsoft.Xna.Framework.Point(650, 290));

            Assert.True(clicked);
            Assert.Equal(SongSearchFilterModal.Field.MaxLevel, modal.FocusedField);
        }

        [Fact]
        public void HandleClick_WhenClickSortBy_ShouldFocusSortBy()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            // SortBy: modal X=340 + FieldX=130 = 470, modal Y=180 + SortRowY=188 + 10 = 378
            var clicked = modal.HandleClick(new Microsoft.Xna.Framework.Point(470, 378));

            Assert.True(clicked);
            Assert.Equal(SongSearchFilterModal.Field.SortBy, modal.FocusedField);
        }

        [Fact]
        public void HandleClick_WhenClickSortDirection_ShouldFocusSortDirection()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            // SortDirection: modal X=340 + FieldX+180=310 = 650, modal Y=180 + SortRowY=188 + 10 = 378
            var clicked = modal.HandleClick(new Microsoft.Xna.Framework.Point(650, 378));

            Assert.True(clicked);
            Assert.Equal(SongSearchFilterModal.Field.SortDirection, modal.FocusedField);
        }

        [Fact]
        public void HandleClick_WhenClickOutsideModal_ShouldReturnFalse()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            var clicked = modal.HandleClick(new Microsoft.Xna.Framework.Point(10, 10));

            Assert.False(clicked);
        }

        [Fact]
        public void HandleClick_WhenNotOpen_ShouldReturnFalse()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            // Click right on the modal area, but modal is closed
            var clicked = modal.HandleClick(new Microsoft.Xna.Framework.Point(570, 258));

            Assert.False(clicked);
        }

        #endregion

        #region HandleInput override tests (modal input blocking)

        [Fact]
        public void HandleInput_WhenOpen_ShouldConsumeAllInput()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            // HandleInput should return true (consume) even with a null input state,
            // because the modal is open and should block all background interaction.
            Assert.True(modal.HandleInput(null!));
        }

        [Fact]
        public void HandleInput_WhenClosed_ShouldNotConsumeInput()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            // Modal starts closed — HandleInput must return false so the
            // invisible element doesn't swallow clicks.
            Assert.False(modal.HandleInput(null!));
        }

        [Fact]
        public void HandleInput_AfterClose_ShouldNotConsumeInput()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.Cancel();

            // After closing, the modal must not consume input.
            Assert.False(modal.HandleInput(null!));
        }

        [Fact]
        public void HandleInput_AfterApply_ShouldNotConsumeInput()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.Apply();

            Assert.False(modal.HandleInput(null!));
        }

        [Fact]
        public void Enabled_WhenConstructed_ShouldBeFalse()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            Assert.False(modal.Enabled);
        }

        [Fact]
        public void Enabled_WhenOpened_ShouldBeTrue()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            Assert.True(modal.Enabled);
        }

        [Fact]
        public void Enabled_AfterClose_ShouldBeFalse()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.Cancel();

            Assert.False(modal.Enabled);
        }

        #endregion

        #region Position/Size sync on Open

        [Fact]
        public void Open_WhenCalled_ShouldSetUIElementBoundsToLayout()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            var layout = DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.SearchFilterModal.Bounds;

            modal.Open(SongFilterCriteria.Default);

            Assert.Equal(layout, modal.Bounds);
        }

        #endregion
    }
}
