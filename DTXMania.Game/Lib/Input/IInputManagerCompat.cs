namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Interface for the InputManagerCompat that provides access to both
    /// basic input management and the advanced ModularInputManager
    /// </summary>
    public interface IInputManagerCompat : IInputManager
    {
        /// <summary>
        /// Gets the underlying ModularInputManager for advanced features
        /// </summary>
        ModularInputManager ModularInputManager { get; }
    }
}