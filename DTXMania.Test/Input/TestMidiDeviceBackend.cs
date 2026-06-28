using DTXMania.Game.Lib.Input.Midi;

namespace DTXMania.Game.Lib.Input;

internal sealed class TestMidiDeviceBackend : IMidiDeviceBackend
{
    private IReadOnlyList<IMidiInputDevice> _devices;

    public TestMidiDeviceBackend(params IMidiInputDevice[] devices)
    {
        _devices = devices;
    }

    public int GetInputDevicesCallCount { get; private set; }

    public IReadOnlyList<IMidiInputDevice> GetInputDevices()
    {
        GetInputDevicesCallCount++;
        return _devices;
    }

    public void SetDevices(params IMidiInputDevice[] devices)
    {
        _devices = devices;
    }
}

internal sealed class TestMidiInputDevice : IMidiInputDevice
{
    public TestMidiInputDevice(string stableId, string name)
    {
        StableId = stableId;
        Name = name;
    }

    public string Name { get; }

    public string StableId { get; }

    public event EventHandler<MidiNoteEventArgs>? NoteReceived;

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
