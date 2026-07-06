using System;
using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.UI.Components
{
    /// <summary>
    ///   Adapts a host text-input mechanism (e.g. <see cref="Microsoft.Xna.Framework.GameWindow.TextInput"/>)
    ///   into a uniform event source consumed by UI components such as <see cref="UITextInput"/>.
    /// </summary>
    /// <remarks>Implementors wrap an OS text-input hook (e.g. a game window's
    /// <c>TextInput</c> event) and are disposed by their owner (e.g. a stage that
    /// obtains the source via <c>IStageGame.GetTextInputSource</c>) when no longer needed.</remarks>
    public interface ITextInputSource : IDisposable
    {
        event EventHandler<TextInputEventArgs> TextInput;
    }
}
