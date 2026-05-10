using System;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Song.Components
{
    public class SongSearchFilterModalLogicTests
    {
        private sealed class FakeSource : ITextInputSource
        {
            public event EventHandler<TextInputEventArgs>? TextInput;
            public void Fire(char c) =>
                TextInput?.Invoke(this, new TextInputEventArgs(c,
                    Microsoft.Xna.Framework.Input.Keys.None));
        }

        [Fact]
        public void Open_DefaultCriteria_PopulatesFromArgument()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            var initial = SongFilterCriteria.Default with { SearchQuery = "abc" };

            modal.Open(initial);

            Assert.True(modal.IsOpen);
            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Cancel_FiresCancelledEvent_AndCloses()
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
        public void Apply_FiresFilterAppliedWithDraft_AndCloses()
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
        public void Reset_FiresFilterResetAndCloses()
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
        public void EditDraft_DoesNotMutateInitialCriteria()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            var initial = SongFilterCriteria.Default with { SearchQuery = "orig" };

            modal.Open(initial);
            modal.UpdateDraft(initial with { SearchQuery = "edited" });

            Assert.Equal("orig", initial.SearchQuery);
            Assert.Equal("edited", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void SubmitFromSearchBox_QSlashCommand_TriggersReset()
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
        public void SubmitFromSearchBox_QSlashCommand_CaseInsensitive()
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
        public void SubmitFromSearchBox_NormalQuery_TriggersApply()
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
        public void SubmitFromSearchBox_QPrefixedQuery_NotResetCommand()
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
        public void Open_SearchBoxIsInitiallyFocused()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void FocusNext_CyclesThroughFieldsInOrder()
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
        public void FocusPrev_ReverseOfFocusNext()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.FocusPrev();
            Assert.Equal(SongSearchFilterModal.Field.ApplyButton, modal.FocusedField);
            modal.FocusPrev();
            Assert.Equal(SongSearchFilterModal.Field.ResetButton, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_Escape_FiresCancel()
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
        public void HandleKey_Tab_AdvancesFocus()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Tab);

            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_Down_AdvancesFocus()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Down);

            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_Up_RetreatsFocus()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Up);

            Assert.Equal(SongSearchFilterModal.Field.ApplyButton, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_Enter_OnSearchBox_SubmitsFromSearchBox()
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
        public void HandleKey_Enter_OnApplyButton_FiresApply()
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
        public void HandleKey_Enter_OnResetButton_FiresReset()
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
        public void HandleKey_LeftRight_OnMinLevel_AdjustsBy5()
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
        public void HandleKey_LeftRight_OnPlayedStatus_CyclesEnum()
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
        public void TypingChars_OnSearchBox_AppendsToSearchQuery()
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
        public void TypingChars_WhenNotOnSearchBox_Ignored()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel

            src.Fire('5');

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Backspace_HandleKey_RemovesLastSearchChar()
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
        public void Close_UnsubscribesFromTextSource()
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
        public void Apply_WhenLibraryNotReady_DoesNotFire()
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
        public void Reset_StillWorksWhenLibraryNotReady()
        {
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            bool fired = false;
            modal.FilterReset += (_, _) => fired = true;

            modal.Reset();

            Assert.True(fired);
        }

        [Fact]
        public void HandleKey_WhenNotOpen_IsNoOp()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            bool cancelled = false;
            modal.Cancelled += (_, _) => cancelled = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Escape);

            Assert.False(cancelled);
        }

        [Fact]
        public void HandleKey_LeftRight_OnMaxLevel_AdjustsBy5()
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
        public void HandleKey_LeftRight_OnSortBy_CyclesSortCriteria()
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
        public void HandleKey_LeftRight_OnSortDirection_Toggles()
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
        public void HandleKey_Back_WhenNotOnSearchBox_IsNoOp()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "abc" });
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleKey_Back_WhenQueryEmpty_IsNoOp()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleKey_Enter_OnPlayedStatus_FieldsApplies()
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
        public void HandleKey_Enter_OnMinLevelField_Applies()
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
        public void HandleKey_Enter_OnMaxLevelField_Applies()
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
        public void OnTextInput_ControlCharacters_Ignored()
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
        public void OnTextInput_BackspaceCharCode_Ignored()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);

            src.Fire('\b');

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenModalClosed_Ignored()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.Cancel();

            src.Fire('x');

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void AdjustLevel_ClampsAtMin0()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);
            Assert.Null(modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void AdjustLevel_ClampsAtMax99()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 95 });
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(99, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void SubmitFromSearchBox_WhenLibraryNotReady_DoesNotFire()
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
        public void Dispose_UnsubscribesFromSource()
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
        public void Open_NullInitial_UsesDefault()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(null);

            Assert.True(modal.IsOpen);
            Assert.Equal(SongFilterCriteria.Default, modal.CurrentDraft);
        }

        [Fact]
        public void UpdateDraft_Null_UsesDefault()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { SearchQuery = "test" });
            modal.UpdateDraft(null);

            Assert.Equal(SongFilterCriteria.Default, modal.CurrentDraft);
        }

        [Fact]
        public void Constructor_NullTextSource_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SongSearchFilterModal(null!));
        }

        [Fact]
        public void CycleSort_StartFromGenre_ResetsToArtist()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { SortBy = SongSortCriteria.Genre });
            for (int i = 0; i < 4; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.SortBy, modal.FocusedField);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(SongSortCriteria.Artist, modal.CurrentDraft.SortBy);
        }

        [Fact]
        public void AdjustLevel_DecrementToZero_ReturnsNull()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { MinLevel = 5 });
            modal.FocusNext(); // MinLevel

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);
            Assert.Null(modal.CurrentDraft.MinLevel);
        }
    }
}
