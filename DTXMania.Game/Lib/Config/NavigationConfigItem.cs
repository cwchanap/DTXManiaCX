using System;

namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// A config list item that navigates to a sub-panel when activated.
    /// Left/right arrows and ToggleValue all invoke the same action.
    /// </summary>
    public class NavigationConfigItem : BaseConfigItem
    {
        private readonly Action _onActivate;

        public NavigationConfigItem(string name, Action onActivate) : base(name)
        {
            _onActivate = onActivate ?? throw new ArgumentNullException(nameof(onActivate));
        }

        public override string GetDisplayText() => $"> {Name}";
        public override void PreviousValue() => _onActivate();
        public override void NextValue() => _onActivate();
        public override void ToggleValue() => _onActivate();
        protected override void OnValueChanged() { }
    }
}
