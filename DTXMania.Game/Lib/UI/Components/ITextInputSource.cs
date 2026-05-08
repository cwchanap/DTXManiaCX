using System;
using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.UI.Components
{
    public interface ITextInputSource
    {
        event EventHandler<TextInputEventArgs> TextInput;
    }
}
