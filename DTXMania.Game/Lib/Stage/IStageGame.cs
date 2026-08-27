#nullable enable
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib.Stage
{
    public interface IStageGame
    {
        GraphicsDevice GraphicsDevice { get; }
        IStageManager StageManager { get; }
        IConfigManager ConfigManager { get; }
        InputManagerCompat InputManager { get; }
        IGraphicsManager GraphicsManager { get; }
        IResourceManager ResourceManager { get; }
        ILoggerFactory LoggerFactory { get; }

        bool CanPerformStageTransition();
        void MarkStageTransition();

        /// <summary>
        /// Maps raw window mouse coordinates into the fixed 1280x720 virtual render target.
        /// Returns null when the point lands outside the letterboxed virtual area.
        /// </summary>
        Point? MapMouseToVirtual(Point windowPoint);

        /// <summary>
        /// Builds a text-input source for OS text events (used by the song search modal).
        /// Returns null in headless/test environments where no OS window is available.
        /// </summary>
        ITextInputSource? GetTextInputSource();

        /// <summary>
        /// Requests game process termination.
        /// </summary>
        void RequestExit();

        /// <summary>
        /// Diagnostic hook fired when the Startup stage is activated.
        /// Default implementation is a best-effort no-op.
        /// </summary>
        void ReportStartupActivated() { }

        /// <summary>
        /// Diagnostic hook fired when the Startup stage renders its first frame.
        /// Default implementation is a best-effort no-op.
        /// </summary>
        void ReportStartupFrameRendered() { }

        /// <summary>
        /// Diagnostic hook fired when the Startup stage requests the song load
        /// summary and transitions to the Title stage. Default implementation is
        /// a best-effort no-op.
        /// </summary>
        void ReportStartupSummaryAndTitleRequested() { }

        /// <summary>
        /// The crash-report inbox the title stage surfaces to the player. The default
        /// implementation is the null-object facade so existing <see cref="IStageGame"/>
        /// implementations and test stubs remain valid without modification; the concrete
        /// <see cref="Game"/> (<c>BaseGame</c>) overrides this to forward the inbox wired into
        /// its <see cref="IGameCrashDiagnostics"/>.
        /// </summary>
        ICrashReportInbox CrashReportInbox => EmptyCrashReportInbox.Instance;

        /// <summary>
        /// Requests a screenshot capture to occur on the next Draw() call, returning the
        /// PNG bytes when fulfilled. Default implementation returns a synchronously
        /// completed null task so headless/test implementations remain valid without
        /// modification; the concrete <see cref="Game"/> (<c>BaseGame</c>) forwards this
        /// to the same capture queue used by the game API.
        /// </summary>
        Task<byte[]?> CaptureScreenshotAsync() => Task.FromResult<byte[]?>(null);
    }
}
