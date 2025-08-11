using System;
using System.Linq;

namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// Base class for configuration items
    /// </summary>
    public abstract class BaseConfigItem : IConfigItem
    {
        public string Name { get; protected set; }

        public event EventHandler ValueChanged;

        protected BaseConfigItem(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public abstract string GetDisplayText();
        public abstract void PreviousValue();
        public abstract void NextValue();
        public abstract void ToggleValue();

        protected virtual void OnValueChanged()
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Configuration item for dropdown/list selection
    /// Similar to DTXManiaNX List type config items
    /// </summary>
    public class DropdownConfigItem : BaseConfigItem
    {
        private readonly Func<string> _getCurrentValue;
        private readonly string[] _availableValues;
        private readonly Action<string> _setValue;
        private int _currentIndex;

        public DropdownConfigItem(string name, Func<string> getCurrentValue, string[] availableValues, Action<string> setValue)
            : base(name)
        {
            _getCurrentValue = getCurrentValue ?? throw new ArgumentNullException(nameof(getCurrentValue));
            _availableValues = availableValues ?? throw new ArgumentNullException(nameof(availableValues));
            _setValue = setValue ?? throw new ArgumentNullException(nameof(setValue));

            if (_availableValues.Length == 0)
                throw new ArgumentException("Available values cannot be empty", nameof(availableValues));

            // Find current index
            var currentValue = _getCurrentValue();
            _currentIndex = Array.IndexOf(_availableValues, currentValue);
            if (_currentIndex < 0)
                _currentIndex = 0;
        }

        public override string GetDisplayText()
        {
            var currentValue = _getCurrentValue();
            return $"{Name}: {currentValue}";
        }

        public override void PreviousValue()
        {
            _currentIndex = (_currentIndex - 1 + _availableValues.Length) % _availableValues.Length;
            _setValue(_availableValues[_currentIndex]);
            OnValueChanged();
        }

        public override void NextValue()
        {
            _currentIndex = (_currentIndex + 1) % _availableValues.Length;
            _setValue(_availableValues[_currentIndex]);
            OnValueChanged();
        }

        public override void ToggleValue()
        {
            // For dropdown, toggle acts like next value
            NextValue();
        }
    }

    /// <summary>
    /// Configuration item for boolean toggle
    /// Similar to DTXManiaNX ONorOFFToggle type config items
    /// </summary>
    public class ToggleConfigItem : BaseConfigItem
    {
        private readonly Func<bool> _getCurrentValue;
        private readonly Action<bool> _setValue;

        public ToggleConfigItem(string name, Func<bool> getCurrentValue, Action<bool> setValue)
            : base(name)
        {
            _getCurrentValue = getCurrentValue ?? throw new ArgumentNullException(nameof(getCurrentValue));
            _setValue = setValue ?? throw new ArgumentNullException(nameof(setValue));
        }

        public override string GetDisplayText()
        {
            var currentValue = _getCurrentValue();
            return $"{Name}: {(currentValue ? "ON" : "OFF")}";
        }

        public override void PreviousValue()
        {
            var currentValue = _getCurrentValue();
            _setValue(!currentValue);
            OnValueChanged();
        }

        public override void NextValue()
        {
            var currentValue = _getCurrentValue();
            _setValue(!currentValue);
            OnValueChanged();
        }

        public override void ToggleValue()
        {
            var currentValue = _getCurrentValue();
            _setValue(!currentValue);
            OnValueChanged();
        }
    }

    /// <summary>
    /// Configuration item for integer values with min/max bounds
    /// Similar to DTXManiaNX Integer type config items
    /// </summary>
    public class IntegerConfigItem : BaseConfigItem
    {
        private readonly Func<int> _getCurrentValue;
        private readonly Action<int> _setValue;
        private readonly int _minValue;
        private readonly int _maxValue;
        private readonly int _step;

        public IntegerConfigItem(string name, Func<int> getCurrentValue, Action<int> setValue, 
            int minValue, int maxValue, int step = 1)
            : base(name)
        {
            _getCurrentValue = getCurrentValue ?? throw new ArgumentNullException(nameof(getCurrentValue));
            _setValue = setValue ?? throw new ArgumentNullException(nameof(setValue));
            _minValue = minValue;
            _maxValue = maxValue;
            _step = step;

            if (_minValue >= _maxValue)
                throw new ArgumentException("Min value must be less than max value");
            if (_step <= 0)
                throw new ArgumentException("Step must be positive");
        }

        public override string GetDisplayText()
        {
            var currentValue = _getCurrentValue();
            return $"{Name}: {currentValue}";
        }

        public override void PreviousValue()
        {
            var currentValue = _getCurrentValue();
            var newValue = Math.Max(_minValue, currentValue - _step);
            _setValue(newValue);
            OnValueChanged();
        }

        public override void NextValue()
        {
            var currentValue = _getCurrentValue();
            var newValue = Math.Min(_maxValue, currentValue + _step);
            _setValue(newValue);
            OnValueChanged();
        }

        public override void ToggleValue()
        {
            // For integer, toggle acts like next value
            NextValue();
        }
    }
}
