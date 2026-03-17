using System;
using DTXMania.Game.Lib.Config;
using Xunit;

namespace DTXMania.Test.Config
{
    /// <summary>
    /// Tests for config item classes: DropdownConfigItem, ToggleConfigItem, IntegerConfigItem
    /// </summary>
    [Trait("Category", "Unit")]
    public class DropdownConfigItemTests
    {
        private string _currentValue = "Option1";

        private DropdownConfigItem CreateItem(string[] values = null)
        {
            values ??= new[] { "Option1", "Option2", "Option3" };
            return new DropdownConfigItem(
                "TestDropdown",
                () => _currentValue,
                values,
                v => _currentValue = v
            );
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_ValidArgs_ShouldInitialize()
        {
            var item = CreateItem();
            Assert.NotNull(item);
            Assert.Equal("TestDropdown", item.Name);
        }

        [Fact]
        public void Constructor_NullName_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new DropdownConfigItem(
                null, () => "A", new[] { "A" }, v => { }));
        }

        [Fact]
        public void Constructor_NullGetCurrentValue_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new DropdownConfigItem(
                "Name", null, new[] { "A" }, v => { }));
        }

        [Fact]
        public void Constructor_NullAvailableValues_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new DropdownConfigItem(
                "Name", () => "A", null, v => { }));
        }

        [Fact]
        public void Constructor_NullSetValue_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new DropdownConfigItem(
                "Name", () => "A", new[] { "A" }, null));
        }

        [Fact]
        public void Constructor_EmptyValues_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() => new DropdownConfigItem(
                "Name", () => "A", new string[0], v => { }));
        }

        [Fact]
        public void Constructor_ValueNotInList_ShouldDefaultToFirst()
        {
            _currentValue = "NotInList";
            var item = CreateItem();
            // Internal index should fall back to 0; verify by advancing one step and checking value = Options[1]
            item.NextValue();
            Assert.Equal("Option2", _currentValue);
        }

        #endregion

        #region GetDisplayText Tests

        [Fact]
        public void GetDisplayText_ShouldIncludeNameAndValue()
        {
            _currentValue = "Option2";
            var item = CreateItem();
            var text = item.GetDisplayText();
            Assert.Contains("TestDropdown", text);
            Assert.Contains("Option2", text);
        }

        #endregion

        #region NextValue Tests

        [Fact]
        public void NextValue_ShouldMoveToNextOption()
        {
            _currentValue = "Option1";
            var item = CreateItem();
            item.NextValue();
            Assert.Equal("Option2", _currentValue);
        }

        [Fact]
        public void NextValue_AtLastOption_ShouldWrapAround()
        {
            _currentValue = "Option3";
            var item = CreateItem();
            item.NextValue();
            Assert.Equal("Option1", _currentValue);
        }

        [Fact]
        public void NextValue_RaisesValueChangedEvent()
        {
            var item = CreateItem();
            bool eventRaised = false;
            item.ValueChanged += (s, e) => eventRaised = true;

            item.NextValue();

            Assert.True(eventRaised);
        }

        #endregion

        #region PreviousValue Tests

        [Fact]
        public void PreviousValue_ShouldMoveToPreviousOption()
        {
            _currentValue = "Option2";
            var item = CreateItem();
            item.PreviousValue();
            Assert.Equal("Option1", _currentValue);
        }

        [Fact]
        public void PreviousValue_AtFirstOption_ShouldWrapAround()
        {
            _currentValue = "Option1";
            var item = CreateItem();
            item.PreviousValue();
            Assert.Equal("Option3", _currentValue);
        }

        [Fact]
        public void PreviousValue_RaisesValueChangedEvent()
        {
            var item = CreateItem();
            bool eventRaised = false;
            item.ValueChanged += (s, e) => eventRaised = true;

            item.PreviousValue();

            Assert.True(eventRaised);
        }

        #endregion

        #region ToggleValue Tests

        [Fact]
        public void ToggleValue_ShouldActLikeNextValue()
        {
            _currentValue = "Option1";
            var item = CreateItem();
            item.ToggleValue();
            Assert.Equal("Option2", _currentValue);
        }

        #endregion
    }

    [Trait("Category", "Unit")]
    public class ToggleConfigItemTests
    {
        private bool _currentValue = false;

        private ToggleConfigItem CreateItem()
        {
            return new ToggleConfigItem("TestToggle", () => _currentValue, v => _currentValue = v);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_ValidArgs_ShouldInitialize()
        {
            var item = CreateItem();
            Assert.Equal("TestToggle", item.Name);
        }

        [Fact]
        public void Constructor_NullGetValue_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new ToggleConfigItem("Name", null, v => { }));
        }

        [Fact]
        public void Constructor_NullSetValue_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new ToggleConfigItem("Name", () => false, null));
        }

        #endregion

        #region GetDisplayText Tests

        [Theory]
        [InlineData(false, "OFF")]
        [InlineData(true, "ON")]
        public void GetDisplayText_ShowsExpected_ON_OFF(bool currentValue, string expected)
        {
            _currentValue = currentValue;
            var item = CreateItem();
            Assert.Contains(expected, item.GetDisplayText());
        }

        #endregion

        #region PreviousValue Tests

        [Fact]
        public void PreviousValue_ShouldToggle()
        {
            _currentValue = true;
            var item = CreateItem();
            item.PreviousValue();
            Assert.False(_currentValue);
        }

        [Fact]
        public void PreviousValue_RaisesValueChangedEvent()
        {
            var item = CreateItem();
            bool eventRaised = false;
            item.ValueChanged += (s, e) => eventRaised = true;

            item.PreviousValue();

            Assert.True(eventRaised);
        }

        #endregion

        #region NextValue Tests

        [Fact]
        public void NextValue_ShouldToggle()
        {
            _currentValue = false;
            var item = CreateItem();
            item.NextValue();
            Assert.True(_currentValue);
        }

        [Fact]
        public void NextValue_RaisesValueChangedEvent()
        {
            var item = CreateItem();
            bool eventRaised = false;
            item.ValueChanged += (s, e) => eventRaised = true;

            item.NextValue();

            Assert.True(eventRaised);
        }

        #endregion

        #region ToggleValue Tests

        [Fact]
        public void ToggleValue_ShouldFlipBooleanValue()
        {
            _currentValue = false;
            var item = CreateItem();
            item.ToggleValue();
            Assert.True(_currentValue);

            item.ToggleValue();
            Assert.False(_currentValue);
        }

        #endregion
    }

    [Trait("Category", "Unit")]
    public class IntegerConfigItemTests
    {
        private int _currentValue = 50;

        private IntegerConfigItem CreateItem(int min = 0, int max = 100, int step = 10)
        {
            return new IntegerConfigItem(
                "TestInteger",
                () => _currentValue,
                v => _currentValue = v,
                min, max, step
            );
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_ValidArgs_ShouldInitialize()
        {
            var item = CreateItem();
            Assert.Equal("TestInteger", item.Name);
        }

        [Fact]
        public void Constructor_MinEqualsMax_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() =>
                new IntegerConfigItem("Name", () => 5, v => { }, 5, 5));
        }

        [Fact]
        public void Constructor_MinGreaterThanMax_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() =>
                new IntegerConfigItem("Name", () => 5, v => { }, 10, 5));
        }

        [Fact]
        public void Constructor_ZeroStep_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() =>
                new IntegerConfigItem("Name", () => 5, v => { }, 0, 10, 0));
        }

        [Fact]
        public void Constructor_NegativeStep_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() =>
                new IntegerConfigItem("Name", () => 5, v => { }, 0, 10, -1));
        }

        #endregion

        #region GetDisplayText Tests

        [Fact]
        public void GetDisplayText_ShouldIncludeNameAndValue()
        {
            _currentValue = 75;
            var item = CreateItem();
            var text = item.GetDisplayText();
            Assert.Contains("TestInteger", text);
            Assert.Contains("75", text);
        }

        #endregion

        #region NextValue Tests

        [Fact]
        public void NextValue_ShouldIncreaseByStep()
        {
            _currentValue = 40;
            var item = CreateItem();
            item.NextValue();
            Assert.Equal(50, _currentValue);
        }

        [Fact]
        public void NextValue_AtMax_ShouldStayAtMax()
        {
            _currentValue = 100;
            var item = CreateItem();
            item.NextValue();
            Assert.Equal(100, _currentValue);
        }

        [Fact]
        public void NextValue_RaisesValueChangedEvent()
        {
            var item = CreateItem();
            bool eventRaised = false;
            item.ValueChanged += (s, e) => eventRaised = true;

            item.NextValue();

            Assert.True(eventRaised);
        }

        #endregion

        #region PreviousValue Tests

        [Fact]
        public void PreviousValue_ShouldDecreaseByStep()
        {
            _currentValue = 60;
            var item = CreateItem();
            item.PreviousValue();
            Assert.Equal(50, _currentValue);
        }

        [Fact]
        public void PreviousValue_AtMin_ShouldStayAtMin()
        {
            _currentValue = 0;
            var item = CreateItem();
            item.PreviousValue();
            Assert.Equal(0, _currentValue);
        }

        [Fact]
        public void PreviousValue_RaisesValueChangedEvent()
        {
            var item = CreateItem();
            bool eventRaised = false;
            item.ValueChanged += (s, e) => eventRaised = true;

            item.PreviousValue();

            Assert.True(eventRaised);
        }

        #endregion

        #region ToggleValue Tests

        [Fact]
        public void ToggleValue_ShouldActLikeNextValue()
        {
            _currentValue = 30;
            var item = CreateItem();
            item.ToggleValue();
            Assert.Equal(40, _currentValue);
        }

        #endregion
    }
}
