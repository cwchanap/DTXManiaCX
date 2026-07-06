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
    public class SongSearchFilterModalCoverageTests
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
        public void HandleInput_WhenClosed_ShouldReturnFalse()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            Assert.False(modal.HandleInput(null!));
        }

        [Fact]
        public void HandleInput_WhenOpen_ShouldReturnTrue()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            Assert.True(modal.HandleInput(null!));
        }

        [Fact]
        public void HandleInput_AfterDispose_WhenStillOpen_ShouldReturnTrue()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.Dispose();

            Assert.True(modal.HandleInput(null!));
        }

        [Fact]
        public void Dispose_WhenModalWasOpen_ShouldUnsubscribeFromTextSource()
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
        public void Dispose_WhenModalWasNeverOpened_ShouldNotThrow()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            var ex = Record.Exception(() => modal.Dispose());

            Assert.Null(ex);
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.Dispose();

            var ex = Record.Exception(() => modal.Dispose());

            Assert.Null(ex);
        }

        [Fact]
        public void LoadingHintText_DefaultShouldBeLibraryStillLoading()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            Assert.Equal("Library still loading…", modal.LoadingHintText);
        }

        [Fact]
        public void LoadingHintText_ShouldBeSettable()
        {
            var modal = new SongSearchFilterModal(new FakeSource())
            {
                LoadingHintText = "Please wait…"
            };

            Assert.Equal("Please wait…", modal.LoadingHintText);
        }

        [Fact]
        public void IsLibraryReady_DefaultShouldBeTrue()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            Assert.True(modal.IsLibraryReady);
        }

        [Fact]
        public void IsLibraryReady_ShouldBeSettable()
        {
            var modal = new SongSearchFilterModal(new FakeSource())
            {
                IsLibraryReady = false
            };

            Assert.False(modal.IsLibraryReady);
        }

        [Fact]
        public void IsLibraryReady_SetBackToTrue_ShouldReflectChange()
        {
            var modal = new SongSearchFilterModal(new FakeSource())
            {
                IsLibraryReady = false
            };
            modal.IsLibraryReady = true;

            Assert.True(modal.IsLibraryReady);
        }

        [Fact]
        public void WhitePixel_ShouldBeNullByDefault()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            Assert.Null(modal.WhitePixel);
        }

        [Fact]
        public void WhitePixel_ShouldBeSettable()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            modal.WhitePixel = null;

            Assert.Null(modal.WhitePixel);
        }

        [Fact]
        public void Font_ShouldBeNullByDefault()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            Assert.Null(modal.Font);
        }

        [Fact]
        public void Font_ShouldBeSettable()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            modal.Font = null;

            Assert.Null(modal.Font);
        }

        [Fact]
        public void SubmitFromSearchBox_WhenLibraryNotReady_ShouldNotFireFilterApplied()
        {
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
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
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
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
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = true };
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
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.Apply();

            Assert.False(applied);
        }

        [Fact]
        public void Apply_WhenLibraryNotReady_ShouldNotClose()
        {
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);

            modal.Apply();

            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void Apply_WhenLibraryNotReady_ViaEnterOnDefaultField_ShouldStayOpen()
        {
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
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
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
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
            var src = new FakeSource();
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
            var src = new FakeSource();
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
            var src = new FakeSource();
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
            var src = new FakeSource();
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
            var src = new FakeSource();
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
            var src = new FakeSource();
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
            var src = new FakeSource();
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
            var src = new FakeSource();
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
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            Assert.Equal("", modal.CurrentDraft.SearchQuery);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleBackspace_WhenQueryIsNull_ShouldNotModifyDraft()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            var criteria = SongFilterCriteria.Default with { SearchQuery = null };
            modal.Open(criteria);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Null(modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void HandleBackspace_WhenFocusedOnMinLevel_ShouldNotModifySearchQuery()
        {
            var src = new FakeSource();
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
            var src = new FakeSource();
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
            var src = new FakeSource();
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
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            var criteria = SongFilterCriteria.Default with { SearchQuery = null };
            modal.Open(criteria);

            src.Fire('a');

            Assert.Equal("a", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void OnTextInput_WhenSearchQueryIsNullAndMultipleChars_ShouldBuildString()
        {
            var src = new FakeSource();
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
            var src = new FakeSource();
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
            var modal = new SongSearchFilterModal(new FakeSource());
            var src = new FakeSource();
            modal.Open(SongFilterCriteria.Default);
            modal.Cancel();

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Visible_WhenConstructed_ShouldBeFalse()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            Assert.False(modal.Visible);
        }

        [Fact]
        public void Visible_WhenOpened_ShouldBeTrue()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            Assert.True(modal.Visible);
        }

        [Fact]
        public void Visible_AfterClose_ShouldBeFalse()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.Close();

            Assert.False(modal.Visible);
        }

        [Fact]
        public void IsOpen_WhenConstructed_ShouldBeFalse()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void CurrentDraft_WhenNotOpened_ShouldBeDefault()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            Assert.Equal(SongFilterCriteria.Default, modal.CurrentDraft);
        }

        [Fact]
        public void FocusedField_WhenNotOpened_ShouldBeSearchBox()
        {
            var modal = new SongSearchFilterModal(new FakeSource());

            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void Open_WithNonNullInitial_ShouldSetDraft()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
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
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            object? sender = null;
            modal.Cancelled += (s, _) => sender = s;

            modal.Cancel();

            Assert.Same(modal, sender);
        }

        [Fact]
        public void HandleCommand_WhenLibraryNotReady_ShouldStillCancelOnBack()
        {
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
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
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
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
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            bool reset = false;
            modal.FilterReset += (_, _) => reset = true;

            modal.Reset();

            Assert.True(reset);
            Assert.False(modal.IsOpen);
        }
    }
}
