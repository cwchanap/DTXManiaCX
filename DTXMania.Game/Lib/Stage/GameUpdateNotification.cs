#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Update;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Focused title-stage component that surfaces the Windows auto-update to the player. It owns
    /// only its open/closed state and action focus; every frame it re-reads the immutable snapshot
    /// from the process-owned <see cref="IGameUpdateService"/>, so state changes (download progress,
    /// failure, installer launch) are observed without any component-side caching.
    ///
    /// The single input-ownership seam is <see cref="HandleInput"/>: when it returns true the title
    /// stage must NOT run its own menu/exit path for that frame. While a download or launched
    /// installer is in flight the component consumes Start/Config navigation and activation, but
    /// deliberately does NOT consume Back, which falls through to the title's exit path. F9 is a
    /// fixed, raw, edge-triggered, non-remappable title shortcut read directly from the polled
    /// keyboard snapshots; no <see cref="InputCommandType"/> is added for it.
    /// </summary>
    public sealed class GameUpdateNotification
    {
        private const int ActionCount = 2;

        /// <summary>
        /// The two review actions. Index 0 is the primary action ("Update Now" while
        /// <see cref="GameUpdateState.Available"/>, "Retry" while <see cref="GameUpdateState.Failed"/>);
        /// index 1 is always "Later". The enum values are contiguous from 0 so they cast to and from
        /// the int focus index used by keyboard navigation and hit-testing.
        /// </summary>
        private enum UpdateAction
        {
            Primary = 0,
            Later = 1
        }

        private static readonly Keys F9Key = Keys.F9;

        // The component owns its layout privately (no skin assets, no layout framework). The virtual
        // canvas matches the rest of the title (1280x720). The banner sits directly below the crash
        // banner so the two never overlap; crash review stays first priority.
        private const int VirtualWidth = 1280;
        private const int VirtualHeight = 720;

        private static readonly Rectangle BannerRegion = new(940, 80, 324, 56);
        private static readonly Rectangle PanelRegion = new(160, 90, 960, 540);

        // Action-button geometry is pure constant arithmetic; compute once and reuse for both
        // drawing and hit-testing so neither path allocates per call.
        private static readonly Rectangle[] ActionRects = BuildActionRects();

        private readonly IGameUpdateService _service;

        private bool _isOpen;
        private int _actionFocus;

        public GameUpdateNotification(IGameUpdateService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        // internal test accessors (InternalsVisibleTo lets the test assembly observe state without
        // exposing it on the public surface).
        internal bool IsOpen => _isOpen;
        internal int ActionFocus => _actionFocus;
        internal bool IsBannerVisible => !_isOpen && _service.GetSnapshot().State == GameUpdateState.Available;

        /// <summary>
        /// The status line rendered while an install operation is in flight: exactly
        /// "DOWNLOADING UPDATE — vX.Y.Z (N%)" when the percent is known, or
        /// "DOWNLOADING UPDATE — vX.Y.Z" when it is not. Null in every other state.
        /// </summary>
        internal string? StatusText => StatusTextFor(_service.GetSnapshot());

        internal static string? StatusTextFor(GameUpdateSnapshot snapshot)
        {
            if (snapshot.State != GameUpdateState.Downloading || snapshot.AvailableVersion is null)
            {
                return null;
            }

            return snapshot.DownloadPercent is { } percent
                ? $"DOWNLOADING UPDATE — {snapshot.AvailableVersion} ({percent}%)"
                : $"DOWNLOADING UPDATE — {snapshot.AvailableVersion}";
        }

        /// <summary>
        /// The single notification input-ownership seam. Returns true when the notification consumes
        /// the frame's input, in which case the title stage must skip its own menu/exit handling for
        /// that same frame. A closed banner consumes NOTHING (the title keeps every input); F9 is
        /// read raw (non-remappable) and edge-triggered against the polled keyboard snapshots;
        /// panel navigation/actions use the remappable <see cref="IInputManager"/> commands.
        /// </summary>
        public bool HandleInput(
            KeyboardState currentKeyboard,
            KeyboardState previousKeyboard,
            IInputManager? inputManager,
            Point? virtualMouse,
            bool leftMouseClick)
        {
            var snapshot = _service.GetSnapshot();
            var state = snapshot.State;

            // An install operation is in flight: consume the frame so the title's Start/Config
            // navigation and activation cannot run, but let Back fall through to the title's exit
            // path (there is deliberately no cancel-update flow).
            if (state is GameUpdateState.Downloading or GameUpdateState.InstallerLaunched)
            {
                _isOpen = false;
                return inputManager?.IsBackActionTriggered() != true;
            }

            bool f9Edge = IsF9Triggered(currentKeyboard, previousKeyboard);

            if (!_isOpen)
            {
                // Closed: only F9 or a banner click can open, and only while there is something
                // to review (an offered update or a retryable failure). The closed banner consumes
                // nothing else: title Activate/movement/Back all fall through (return false).
                if (f9Edge && state is GameUpdateState.Available or GameUpdateState.Failed)
                {
                    OpenPanel();
                    return true;
                }

                if (leftMouseClick && virtualMouse is { } point && state == GameUpdateState.Available &&
                    BannerRegion.Contains(point))
                {
                    OpenPanel();
                    return true;
                }

                return false;
            }

            // Open: the panel owns input for the frame. Every branch consumes.
            if (state is not (GameUpdateState.Available or GameUpdateState.Failed))
            {
                // The offer disappeared (e.g. dismissed) — close and hand the frame back.
                _isOpen = false;
                return false;
            }

            if (inputManager?.IsBackActionTriggered() == true)
            {
                ClosePanel();
                return true;
            }

            if (inputManager?.IsCommandPressed(InputCommandType.MoveLeft) == true)
            {
                _actionFocus = (int)UpdateAction.Primary;
                return true;
            }

            if (inputManager?.IsCommandPressed(InputCommandType.MoveRight) == true)
            {
                _actionFocus = (int)UpdateAction.Later;
                return true;
            }

            // Mouse: clicking an action button focuses and invokes it in one motion.
            if (leftMouseClick && virtualMouse is { } clickPoint && HitTestAction(clickPoint) is { } hit)
            {
                _actionFocus = (int)hit;
                InvokeFocusedAction(snapshot);
                return true;
            }

            if (inputManager?.IsCommandPressed(InputCommandType.Activate) == true)
            {
                InvokeFocusedAction(snapshot);
                return true;
            }

            return true;
        }

        /// <summary>
        /// Draws the closed banner (while an update is available), the downloading status line,
        /// and the review panel (while open). Reuses the title's SpriteBatch/font/white-pixel
        /// resources.
        /// </summary>
        [ExcludeFromCodeCoverage]
        public void Draw(SpriteBatch spriteBatch, IFont? font, Texture2D? whitePixel)
        {
            if (spriteBatch == null || whitePixel == null)
            {
                return;
            }

            var snapshot = _service.GetSnapshot();

            if (StatusTextFor(snapshot) is { } statusText)
            {
                DrawStatusBanner(spriteBatch, font, whitePixel, statusText);
                return;
            }

            if (_isOpen)
            {
                DrawPanel(spriteBatch, snapshot, font, whitePixel);
                return;
            }

            if (snapshot.State == GameUpdateState.Available)
            {
                DrawBanner(spriteBatch, font, whitePixel);
            }
        }

        private void OpenPanel()
        {
            _isOpen = true;
            _actionFocus = (int)UpdateAction.Primary;
        }

        private void ClosePanel()
        {
            _isOpen = false;
        }

        private void InvokeFocusedAction(GameUpdateSnapshot snapshot)
        {
            switch ((UpdateAction)_actionFocus)
            {
                case UpdateAction.Primary:
                    // "Update Now" / "Retry": the download status banner takes over from the panel.
                    _service.BeginUpdate();
                    ClosePanel();
                    break;
                case UpdateAction.Later:
                    // "Later": declines an Available offer for the rest of the process (the service
                    // no-ops in the retryable-Failed state, where Later simply closes the panel).
                    _service.DismissForProcess();
                    ClosePanel();
                    break;
            }
        }

        private static bool IsF9Triggered(KeyboardState current, KeyboardState previous)
        {
            return current.IsKeyDown(F9Key) && !previous.IsKeyDown(F9Key);
        }

        private static UpdateAction? HitTestAction(Point point)
        {
            for (int i = 0; i < ActionRects.Length; i++)
            {
                if (ActionRects[i].Contains(point))
                {
                    return (UpdateAction)i;
                }
            }

            return null;
        }

        // ---------------------------------------------------------------------------------------------
        // Layout / drawing (private geometry; no skin assets)
        // ---------------------------------------------------------------------------------------------

        [ExcludeFromCodeCoverage]
        private void DrawBanner(SpriteBatch spriteBatch, IFont? font, Texture2D whitePixel)
        {
            spriteBatch.Draw(
                whitePixel,
                BannerRegion,
                new Color(28, 84, 32, 220));

            DrawHollowRect(spriteBatch, whitePixel, BannerRegion, new Color(140, 230, 140, 230));

            if (font != null)
            {
                font.DrawString(
                    spriteBatch,
                    $"UPDATE AVAILABLE — v{_service.GetSnapshot().AvailableVersion}",
                    new Vector2(BannerRegion.X + 12, BannerRegion.Y + 8),
                    Color.White);
                font.DrawString(
                    spriteBatch,
                    "Press F9 or click to review",
                    new Vector2(BannerRegion.X + 12, BannerRegion.Y + 28),
                    new Color(200, 255, 200));
            }
        }

        [ExcludeFromCodeCoverage]
        private void DrawStatusBanner(SpriteBatch spriteBatch, IFont? font, Texture2D whitePixel, string statusText)
        {
            spriteBatch.Draw(
                whitePixel,
                BannerRegion,
                new Color(28, 84, 32, 220));

            DrawHollowRect(spriteBatch, whitePixel, BannerRegion, new Color(140, 230, 140, 230));

            font?.DrawString(
                spriteBatch,
                statusText,
                new Vector2(BannerRegion.X + 12, BannerRegion.Y + 18),
                Color.White);
        }

        [ExcludeFromCodeCoverage]
        private void DrawPanel(SpriteBatch spriteBatch, GameUpdateSnapshot snapshot, IFont? font, Texture2D whitePixel)
        {
            // Dim the backdrop so the panel reads as modal.
            spriteBatch.Draw(
                whitePixel,
                new Rectangle(0, 0, VirtualWidth, VirtualHeight),
                new Color(0, 0, 0, 160));

            spriteBatch.Draw(
                whitePixel,
                PanelRegion,
                new Color(18, 34, 22, 245));
            DrawHollowRect(spriteBatch, whitePixel, PanelRegion, new Color(120, 200, 130, 230));

            float x = PanelRegion.X + 24;
            float y = PanelRegion.Y + 20;

            var header = snapshot.State == GameUpdateState.Failed
                ? "Update failed — the installer can be retried."
                : $"A newer DTXManiaCX is available: v{snapshot.AvailableVersion}.";

            font?.DrawString(spriteBatch, header, new Vector2(x, y), new Color(200, 255, 200));
            y += 30;
            font?.DrawString(
                spriteBatch,
                "Move to choose, Activate to confirm, Back to close.",
                new Vector2(x, y),
                new Color(170, 170, 170));

            DrawActions(spriteBatch, snapshot, font, whitePixel);
        }

        [ExcludeFromCodeCoverage]
        private void DrawActions(SpriteBatch spriteBatch, GameUpdateSnapshot snapshot, IFont? font, Texture2D whitePixel)
        {
            var primaryLabel = snapshot.State == GameUpdateState.Failed ? "Retry" : "Update Now";
            var labels = new[] { primaryLabel, "Later" };

            var color = new Color(40, 78, 48, 230);
            var focusedColor = new Color(80, 160, 96, 240);

            for (int i = 0; i < ActionRects.Length; i++)
            {
                var rect = ActionRects[i];
                spriteBatch.Draw(whitePixel, rect, i == _actionFocus ? focusedColor : color);
                DrawHollowRect(spriteBatch, whitePixel, rect, Color.White * 0.6f);

                font?.DrawString(
                    spriteBatch,
                    labels[i],
                    new Vector2(rect.X + 10, rect.Y + 6),
                    Color.White);
            }
        }

        [ExcludeFromCodeCoverage]
        private static void DrawHollowRect(SpriteBatch spriteBatch, Texture2D whitePixel, Rectangle rect, Color color)
        {
            spriteBatch.Draw(whitePixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
            spriteBatch.Draw(whitePixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
            spriteBatch.Draw(whitePixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
            spriteBatch.Draw(whitePixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
        }

        private static Rectangle[] BuildActionRects()
        {
            const int buttonWidth = 220;
            const int buttonHeight = 32;
            const int gap = 12;
            int totalWidth = ActionCount * buttonWidth + (ActionCount - 1) * gap;
            int startX = PanelRegion.X + (PanelRegion.Width - totalWidth) / 2;
            int y = PanelRegion.Bottom - buttonHeight - 24;

            var rects = new Rectangle[ActionCount];
            for (int i = 0; i < ActionCount; i++)
            {
                rects[i] = new Rectangle(startX + i * (buttonWidth + gap), y, buttonWidth, buttonHeight);
            }

            return rects;
        }
    }
}
