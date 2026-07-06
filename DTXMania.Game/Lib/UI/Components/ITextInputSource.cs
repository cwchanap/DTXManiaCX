using System;
using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.UI.Components
{
    /// <summary>
    ///   Adapts a host text-input mechanism (e.g. <see cref="Microsoft.Xna.Framework.GameWindow.TextInput"/>)
    ///   into a uniform event source consumed by UI components such as <see cref="UITextInput"/>.
    /// </summary>
    /// <remarks>
    ///   Implementors are owned and disposed by the component that obtains them. For example,
    ///   <c>SongSelectionStage</c> disposes the source it retrieves from
    ///   <c>IStageGame.GetTextInputSource()</c>. The interface therefore extends
    ///   <see cref="IDisposable"/> so callers can release the underlying hook (e.g. detach from
    ///   <c>GameWindow.TextInput</c>) without depending on the concrete implementor.
    /// </remarks>
    public interface ITextInputSource : IDisposable
    {
        event EventHandler<TextInputEventArgs> TextInput;
    }
}
