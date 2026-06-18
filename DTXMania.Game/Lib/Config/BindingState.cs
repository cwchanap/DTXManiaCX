using System.Collections.Generic;

namespace DTXMania.Game.Lib.Config
{
    /// <summary>
    /// Immutable, opaque snapshot of the binding-related collections on <see cref="ConfigData"/>:
    /// the drum <see cref="ConfigData.KeyBindings"/>, the <see cref="ConfigData.UnboundDrumLanes"/>/
    /// <see cref="ConfigData.UnboundDrumButtons"/> sets, and <see cref="ConfigData.SystemKeyBindings"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by the config stages' save-with-rollback flow: a snapshot taken before the in-memory
    /// mutation is restored if the disk write throws, so live state never diverges from disk.
    /// </para>
    /// <para>
    /// Centralizing these four collections here means a newly-added binding field is a two-point
    /// edit (<see cref="ConfigData.SnapshotBindingState"/> + <see cref="ConfigData.RestoreBindingState"/>)
    /// rather than a four-place manual Clear/refill that was previously duplicated across
    /// <c>DrumConfigStage</c> and <c>ConfigStage</c> with no compiler reminder — the exact fragility
    /// that motivated this type.
    /// </para>
    /// <para>
    /// The snapshot holds shallow copies; the collection elements are immutable value types / strings,
    /// so a shallow copy is a correct point-in-time capture. Members are internal: the type is public
    /// only because it is returned from a public method, and the data is read solely by
    /// <see cref="ConfigData"/> (same assembly) during restore.
    /// </para>
    /// </remarks>
    public readonly struct BindingState
    {
        internal IReadOnlyDictionary<string, int> KeyBindings { get; }
        internal IReadOnlySet<int> UnboundDrumLanes { get; }
        internal IReadOnlySet<string> UnboundDrumButtons { get; }
        internal IReadOnlyDictionary<string, string> SystemKeyBindings { get; }

        internal BindingState(
            Dictionary<string, int> keyBindings,
            HashSet<int> unboundDrumLanes,
            HashSet<string> unboundDrumButtons,
            Dictionary<string, string> systemKeyBindings)
        {
            KeyBindings = keyBindings;
            UnboundDrumLanes = unboundDrumLanes;
            UnboundDrumButtons = unboundDrumButtons;
            SystemKeyBindings = systemKeyBindings;
        }
    }
}
