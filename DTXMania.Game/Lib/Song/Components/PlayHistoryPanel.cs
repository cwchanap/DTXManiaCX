#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Song.Components
{
    /// <summary>
    /// DTXManiaNX-style five-row play history panel for song selection.
    /// </summary>
    public sealed class PlayHistoryPanel : UIElement
    {
        private ITexture? _panelTexture;
        private SpriteFont? _font;
        private IFont? _managedFont;
        private IResourceManager? _resourceManager;
        private string[] _historyLines = Array.Empty<string>();

        /// <summary>
        /// Row text color: "SongSelect.HistoryText" → "UI.TextPrimary" → the NX
        /// default (yellow), so themeless skins render exactly as before.
        /// </summary>
        internal static Color ResolveHistoryTextColor(ISkinTheme theme) =>
            theme.GetColor("SongSelect.HistoryText",
                theme.GetColor("UI.TextPrimary", DTXManiaVisualTheme.FontEffects.DefaultTextColor));

        public PlayHistoryPanel()
        {
            Position = SongSelectionUILayout.PlayHistoryPanel.Position;
            Size = SongSelectionUILayout.PlayHistoryPanel.Size;
            Visible = false;
        }

        /// <summary>
        /// Font used for rendering history rows. Assigning this also derives the
        /// underlying <c>SpriteFont</c>, so this is the single source of truth for
        /// text rendering. There is intentionally no separate raw <c>Font</c>
        /// setter to avoid a dual-source-of-truth where one setter could silently
        /// shadow the other.
        /// </summary>
        public IFont? ManagedFont
        {
            get => _managedFont;
            set
            {
                _managedFont = value;
                _font = value?.SpriteFont;
            }
        }

        public float TextScale { get; set; } = SongSelectionUILayout.PlayHistoryPanel.FontScale;

        public void Initialize(IResourceManager resourceManager)
        {
            ReleaseTexture();
            _resourceManager = resourceManager;

            if (resourceManager == null)
                return;

            try
            {
                _panelTexture = resourceManager.LoadTexture(TexturePath.PlayHistoryPanel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlayHistoryPanel: Failed to load panel texture: {ex.Message}");
                _panelTexture = null;
            }
        }

        /// <summary>
        /// Refreshes the displayed history for the given song/difficulty. Passing
        /// <c>null</c> (or a non-score node) clears and hides the panel. The
        /// parameter is nullable by contract because the selection pipeline can
        /// signal "nothing selected" with null.
        /// </summary>
        public void UpdateSongInfo(SongListNode? song, int difficulty)
        {
            if (song == null || song.Type != NodeType.Score)
            {
                ClearHistory();
                return;
            }

            var score = song.GetScore(difficulty);
            if (score == null)
            {
                ClearHistory();
                return;
            }

            // Render imported/CX history rows verbatim; do not rewrite the status text.
            _historyLines = score.PlayHistoryLines?
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(SongSelectionUILayout.PlayHistoryPanel.MaxRows)
                .ToArray() ?? Array.Empty<string>();

            Visible = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ReleaseTexture();

            base.Dispose(disposing);
        }

        private void ClearHistory()
        {
            _historyLines = Array.Empty<string>();
            Visible = false;
        }

        private void ReleaseTexture()
        {
            _panelTexture?.RemoveReference();
            _panelTexture = null;
        }

        [ExcludeFromCodeCoverage]
        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible)
                return;

            var origin = AbsolutePosition;

            if (_panelTexture != null)
            {
                try
                {
                    _panelTexture.Draw(spriteBatch, origin);
                }
                catch (ObjectDisposedException)
                {
                    // The background texture was disposed out from under us (texture-
                    // lifecycle defect). Release our handle so we stop trying to draw a
                    // dead texture, and surface the condition so it isn't invisible.
                    System.Diagnostics.Debug.WriteLine(
                        "PlayHistoryPanel: Panel texture was disposed unexpectedly during draw; " +
                        "releasing handle. This signals a texture-lifecycle defect upstream.");
                    ReleaseTexture();
                }
            }

            var textOrigin = new Vector2(
                origin.X + SongSelectionUILayout.PlayHistoryPanel.TextOffsetX,
                origin.Y + SongSelectionUILayout.PlayHistoryPanel.TextOffsetY);

            for (int i = 0; i < _historyLines.Length; i++)
            {
                DrawText(
                    spriteBatch,
                    _historyLines[i],
                    textOrigin + new Vector2(0, i * SongSelectionUILayout.PlayHistoryPanel.RowSpacing));
            }

            base.OnDraw(spriteBatch, deltaTime);
        }

        [ExcludeFromCodeCoverage]
        private void DrawText(SpriteBatch spriteBatch, string text, Vector2 position)
        {
            if (spriteBatch == null || string.IsNullOrWhiteSpace(text))
                return;

            var shadow = position + DTXManiaVisualTheme.FontEffects.DefaultShadowOffset;
            var scale = new Vector2(TextScale);
            var textColor = ResolveHistoryTextColor(_resourceManager?.CurrentTheme ?? SkinTheme.Empty);
            if (_font != null)
            {
                spriteBatch.DrawString(_font, text, shadow, DTXManiaVisualTheme.FontEffects.DefaultShadowColor,
                    0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                spriteBatch.DrawString(_font, text, position, textColor,
                    0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
            else if (_managedFont != null)
            {
                _managedFont.DrawString(spriteBatch, text, shadow, DTXManiaVisualTheme.FontEffects.DefaultShadowColor,
                    0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                _managedFont.DrawString(spriteBatch, text, position, textColor,
                    0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }
    }
}
