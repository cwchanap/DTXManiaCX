using System;

namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// A config list item that navigates to a sub-panel, a different stage, or runs an
    /// action when activated. All three action methods (PreviousValue/NextValue/ToggleValue)
    /// invoke the same callback so the item stays drop-in compatible with value-cycling
    /// items; however, ConfigStage drives these items from the Activate path only — see
    /// <c>ConfigStage.HandleItemInput</c>, which restricts navigation items to Activate and
    /// ignores Left/Right. Left/Right are value-adjust keys and must never trigger a stage
    /// change or an async import. (This restriction lives in the stage, not in the item, so
    /// it matters only if a second consumer of this item appears.)
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
        // Navigation items have no cyclic value; ValueChanged is intentionally never raised.
        protected override void OnValueChanged() { }
    }
}
