#nullable enable

namespace DTXMania.Game.Lib.Input.Midi;

public interface IMidiNoteInjector
{
    bool TryInjectNote(int noteNumber, int velocity, bool isPressed);
}
