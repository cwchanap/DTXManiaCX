#nullable enable

using DTXMania.Game.Lib.Stage.Config;

namespace DTXMania.Game.Lib.Stage.KeyAssign
{
    /// <summary>
    /// Interface for a key-assignment sub-panel that temporarily takes over
    /// input handling and rendering within ConfigStage.
    /// </summary>
    public interface IKeyAssignPanel : IConfigOverlayPanel
    {
    }
}
