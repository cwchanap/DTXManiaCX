using System;
using System.Collections.Generic;
using System.Linq;

namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// Shared label table behind the config-dropdown enum extensions: keeps the
    /// UI-facing (value, label) list in declaration order and owns the lookup in
    /// both directions. <see cref="ToLabel"/> never throws — an undefined enum
    /// value falls back to its name so a corrupted value cannot crash the Config
    /// screen. <see cref="FromLabel"/> throws on unknown input because labels only
    /// ever come from <see cref="Labels"/>.
    /// </summary>
    internal sealed class EnumOptionTable<TEnum> where TEnum : struct, Enum
    {
        private readonly (TEnum Value, string Label)[] _options;

        public EnumOptionTable(params (TEnum Value, string Label)[] options)
        {
            _options = options;
            Labels = options.Select(option => option.Label).ToArray();
        }

        public IReadOnlyList<(TEnum Value, string Label)> Options => _options;

        public IReadOnlyList<string> Labels { get; }

        public string ToLabel(TEnum value)
        {
            foreach (var option in _options)
            {
                if (EqualityComparer<TEnum>.Default.Equals(option.Value, value))
                    return option.Label;
            }
            return value.ToString();
        }

        public TEnum FromLabel(string label) =>
            _options.First(option => string.Equals(
                option.Label, label, StringComparison.OrdinalIgnoreCase)).Value;
    }
}
