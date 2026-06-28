#nullable enable

using System.Collections.Generic;

namespace DTXMania.Game.Lib.Input.Midi;

public interface IMidiDeviceBackend
{
    IReadOnlyList<IMidiInputDevice> GetInputDevices();
}
