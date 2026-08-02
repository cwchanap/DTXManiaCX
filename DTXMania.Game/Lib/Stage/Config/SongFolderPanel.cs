#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Game.Lib.Stage.Config
{
    /// <summary>
    /// Config overlay for editing the ordered configured-song-folder list. The
    /// panel owns only a draft; Config owns persistence through the injected
    /// apply delegate.
    /// </summary>
    internal sealed class SongFolderPanel : IConfigOverlayPanel
    {
        private enum ActionRow
        {
            AddFolder,
            Remove,
            MoveUp,
            MoveDown,
            Apply,
            Cancel,
        }

        private static readonly ActionRow[] Actions =
        {
            ActionRow.AddFolder,
            ActionRow.Remove,
            ActionRow.MoveUp,
            ActionRow.MoveDown,
            ActionRow.Apply,
            ActionRow.Cancel,
        };

        private static readonly Color BackdropColor = new(0, 0, 0, 200);
        private static readonly Color BoardColor = new(14, 16, 34, 236);
        private static readonly Color BoardBorderColor = new(74, 62, 150, 235);
        private static readonly Color RowColor = new(30, 34, 60, 220);
        private static readonly Color SelectionColor = new(120, 92, 30, 190);
        private static readonly Color PrimaryTextColor = new(235, 238, 248);
        private static readonly Color SelectedTextColor = new(255, 238, 120);
        private static readonly Color WarningTextColor = new(255, 196, 96);
        private static readonly Color ErrorTextColor = new(255, 96, 96);

        private IReadOnlyList<string> _configuredRoots;
        private readonly IFolderPickerService _folderPicker;
        private readonly SongRootPolicy _rootPolicy;
        private readonly Func<IReadOnlyList<string>, SongFolderApplyResult> _apply;
        private readonly List<string> _draftRoots = new();
        private readonly ConcurrentQueue<PickerCompletion> _pickerCompletions = new();

        private CancellationTokenSource? _pickerCancellation;
        private int _activationGeneration;
        private int _selectedIndex;
        private int _selectedRootIndex;

        internal SongFolderPanel(
            IReadOnlyList<string> configuredRoots,
            IFolderPickerService folderPicker,
            SongRootPolicy rootPolicy,
            Func<IReadOnlyList<string>, SongFolderApplyResult> apply)
        {
            ArgumentNullException.ThrowIfNull(configuredRoots);
            ArgumentNullException.ThrowIfNull(folderPicker);
            ArgumentNullException.ThrowIfNull(rootPolicy);
            ArgumentNullException.ThrowIfNull(apply);

            if (configuredRoots.Count == 0)
            {
                throw new ArgumentException(
                    "At least one configured song folder is required.",
                    nameof(configuredRoots));
            }

            _configuredRoots = Array.AsReadOnly(configuredRoots.ToArray());
            _folderPicker = folderPicker;
            _rootPolicy = rootPolicy;
            _apply = apply;
        }

        public bool IsActive { get; private set; }

        public event EventHandler? Saved;

        public event EventHandler? Closed;

        /// <summary>Copied snapshot used by rendering and diagnostic consumers.</summary>
        internal IReadOnlyList<string> DraftRoots => Array.AsReadOnly(_draftRoots.ToArray());

        /// <summary>Non-blocking warning or error currently shown by the overlay.</summary>
        internal string? StatusMessage { get; private set; }

        /// <summary>Most recent Config-owned apply outcome for the stage event handler.</summary>
        internal SongFolderApplyStatus? LastApplyStatus { get; private set; }

        public void Activate()
        {
            CancelPendingPicker();
            IncrementActivationGeneration();

            _draftRoots.Clear();
            _draftRoots.AddRange(_configuredRoots);
            _selectedIndex = 0;
            _selectedRootIndex = 0;
            LastApplyStatus = null;
            IsActive = true;
            SetValidationStatus(_rootPolicy.Validate(_draftRoots));
        }

        public void Deactivate()
        {
            if (!IsActive)
                return;

            IsActive = false;
            IncrementActivationGeneration();
            CancelPendingPicker();
        }

        public void Update(double deltaTime, KeyboardState current, KeyboardState previous)
        {
            DrainPickerCompletions();
            if (!IsActive)
                return;

            if (IsBackPressed(current, previous))
            {
                CancelAndClose();
                return;
            }

            if (_pickerCancellation != null)
                return;

            if (IsJustPressed(current, previous, Keys.Up))
            {
                _selectedIndex = (_selectedIndex - 1 + RowCount) % RowCount;
                RememberSelectedRoot();
            }
            else if (IsJustPressed(current, previous, Keys.Down))
            {
                _selectedIndex = (_selectedIndex + 1) % RowCount;
                RememberSelectedRoot();
            }
            else if (IsJustPressed(current, previous, Keys.Enter))
            {
                ActivateSelectedRow();
            }
        }

        public void Draw(
            SpriteBatch spriteBatch,
            IFont? font,
            IFont? boldFont,
            Texture2D? whitePixel,
            int virtualWidth,
            int virtualHeight)
        {
            if (!IsActive || spriteBatch == null)
                return;

            const int boardWidth = 760;
            const int boardHeight = 560;
            const int rowHeight = 38;
            const int rowPadding = 20;
            var boardX = (virtualWidth - boardWidth) / 2;
            var boardY = (virtualHeight - boardHeight) / 2;

            if (whitePixel != null)
            {
                spriteBatch.Draw(whitePixel,
                    new Rectangle(0, 0, virtualWidth, virtualHeight), BackdropColor);
                spriteBatch.Draw(whitePixel,
                    new Rectangle(boardX - 4, boardY - 4, boardWidth + 8, boardHeight + 8),
                    BoardBorderColor);
                spriteBatch.Draw(whitePixel,
                    new Rectangle(boardX, boardY, boardWidth, boardHeight), BoardColor);
            }

            const string title = "SONG FOLDERS";
            DrawCenteredText(spriteBatch, boldFont ?? font, title, virtualWidth, boardY + 20, PrimaryTextColor);

            var y = boardY + 74;
            for (var rootIndex = 0; rootIndex < _draftRoots.Count; rootIndex++)
            {
                DrawRow(spriteBatch, font, boldFont, whitePixel, boardX, y, boardWidth, rowHeight,
                    rootIndex, _draftRoots[rootIndex]);
                y += rowHeight;
            }

            y += 10;
            for (var actionIndex = 0; actionIndex < Actions.Length; actionIndex++)
            {
                var rowIndex = _draftRoots.Count + actionIndex;
                DrawRow(spriteBatch, font, boldFont, whitePixel, boardX, y, boardWidth, rowHeight,
                    rowIndex, GetActionLabel(Actions[actionIndex]));
                y += rowHeight;
            }

            if (!string.IsNullOrWhiteSpace(StatusMessage))
            {
                var color = IsWarningStatus() ? WarningTextColor : ErrorTextColor;
                font?.DrawString(spriteBatch, StatusMessage,
                    new Vector2(boardX + rowPadding, boardY + boardHeight - 64), color);
            }

            var instruction = _pickerCancellation == null
                ? "UP/DOWN: Navigate | ENTER: Select | ESC: Cancel"
                : "Waiting for folder picker... ESC: Cancel";
            DrawCenteredText(spriteBatch, font, instruction, virtualWidth,
                boardY + boardHeight - 34, PrimaryTextColor);
        }

        private int RowCount => _draftRoots.Count + Actions.Length;

        private void ActivateSelectedRow()
        {
            if (_selectedIndex < _draftRoots.Count)
            {
                _selectedRootIndex = _selectedIndex;
                return;
            }

            var actionIndex = _selectedIndex - _draftRoots.Count;
            if (actionIndex < 0 || actionIndex >= Actions.Length)
                return;

            switch (Actions[actionIndex])
            {
                case ActionRow.AddFolder:
                    BeginFolderPicker();
                    break;
                case ActionRow.Remove:
                    RemoveSelectedRoot();
                    break;
                case ActionRow.MoveUp:
                    MoveSelectedRoot(-1);
                    break;
                case ActionRow.MoveDown:
                    MoveSelectedRoot(1);
                    break;
                case ActionRow.Apply:
                    ApplyDraft();
                    break;
                case ActionRow.Cancel:
                    CancelAndClose();
                    break;
            }
        }

        private void BeginFolderPicker()
        {
            if (_pickerCancellation != null)
                return;

            var cancellation = new CancellationTokenSource();
            _pickerCancellation = cancellation;
            StatusMessage = null;
            var initialDirectory = _selectedRootIndex >= 0 && _selectedRootIndex < _draftRoots.Count
                ? _draftRoots[_selectedRootIndex]
                : null;
            _ = CompletePickerAsync(_activationGeneration, cancellation, initialDirectory);
        }

        private async Task CompletePickerAsync(
            int generation,
            CancellationTokenSource cancellation,
            string? initialDirectory)
        {
            FolderPickerResult result;
            try
            {
                result = await _folderPicker
                    .PickFolderAsync(initialDirectory, cancellation.Token)
                    .ConfigureAwait(false);
                result ??= FolderPickerResult.Failed("The folder picker returned no result.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                result = FolderPickerResult.Cancelled();
            }
            catch (Exception exception)
            {
                result = FolderPickerResult.Failed(exception.Message);
            }

            _pickerCompletions.Enqueue(new PickerCompletion(generation, cancellation, result));
        }

        private void DrainPickerCompletions()
        {
            while (_pickerCompletions.TryDequeue(out var completion))
            {
                var isCurrent = IsActive &&
                                completion.Generation == _activationGeneration &&
                                ReferenceEquals(completion.Cancellation, _pickerCancellation);
                if (!isCurrent)
                {
                    completion.Cancellation.Dispose();
                    continue;
                }

                _pickerCancellation = null;
                completion.Cancellation.Dispose();
                ApplyPickerResult(completion.Result);
            }
        }

        private void ApplyPickerResult(FolderPickerResult result)
        {
            switch (result.Status)
            {
                case FolderPickerStatus.Selected:
                    AddSelectedFolder(result.Path!);
                    return;
                case FolderPickerStatus.Cancelled:
                    StatusMessage = null;
                    return;
                case FolderPickerStatus.Unavailable:
                    StatusMessage = result.Message ?? "Folder picker is unavailable.";
                    return;
                case FolderPickerStatus.Failed:
                    StatusMessage = result.Message ?? "Folder picker failed.";
                    return;
                default:
                    StatusMessage = "Folder picker returned an unknown result.";
                    return;
            }
        }

        private void AddSelectedFolder(string selectedPath)
        {
            var proposedRoots = new List<string>(_draftRoots) { selectedPath };
            var validation = _rootPolicy.Validate(proposedRoots);
            if (!validation.IsValid)
            {
                SetValidationStatus(validation);
                return;
            }

            _draftRoots.Clear();
            _draftRoots.AddRange(validation.CanonicalRoots);
            _selectedRootIndex = _draftRoots.Count - 1;
            _selectedIndex = _selectedRootIndex;
            SetValidationStatus(validation);
        }

        private void RemoveSelectedRoot()
        {
            if (_draftRoots.Count <= 1)
            {
                StatusMessage = "At least one song folder is required.";
                return;
            }

            var rootIndex = Math.Clamp(_selectedRootIndex, 0, _draftRoots.Count - 1);
            _draftRoots.RemoveAt(rootIndex);
            _selectedRootIndex = Math.Min(rootIndex, _draftRoots.Count - 1);
            _selectedIndex = _selectedRootIndex;
            SetValidationStatus(_rootPolicy.Validate(_draftRoots));
        }

        private void MoveSelectedRoot(int direction)
        {
            var rootIndex = Math.Clamp(_selectedRootIndex, 0, _draftRoots.Count - 1);
            var targetIndex = rootIndex + direction;
            if (targetIndex < 0 || targetIndex >= _draftRoots.Count)
                return;

            (_draftRoots[rootIndex], _draftRoots[targetIndex]) =
                (_draftRoots[targetIndex], _draftRoots[rootIndex]);
            _selectedRootIndex = targetIndex;
        }

        private void ApplyDraft()
        {
            var validation = _rootPolicy.Validate(_draftRoots);
            if (!validation.IsValid)
            {
                SetValidationStatus(validation);
                return;
            }

            SongFolderApplyResult result;
            try
            {
                result = _apply(validation.CanonicalRoots);
            }
            catch (Exception exception)
            {
                LastApplyStatus = SongFolderApplyStatus.PersistenceFailed;
                StatusMessage = exception.Message;
                return;
            }

            LastApplyStatus = result.Status;
            switch (result.Status)
            {
                case SongFolderApplyStatus.Updated:
                    _configuredRoots = Array.AsReadOnly(result.CanonicalRoots.ToArray());
                    CommitAndClose();
                    return;
                case SongFolderApplyStatus.Unchanged:
                    CloseSilently();
                    return;
                case SongFolderApplyStatus.Busy:
                case SongFolderApplyStatus.ValidationFailed:
                case SongFolderApplyStatus.PersistenceFailed:
                    StatusMessage = GetDiagnosticMessage(result.Diagnostics, "Could not save song folders.");
                    return;
                case SongFolderApplyStatus.Started:
                    // Slice 2 has no live reload. A later Config-owned coordinator
                    // may use this status to drive progress without changing this
                    // panel's persistence boundary.
                    StatusMessage = "Song-folder update has started.";
                    return;
                default:
                    StatusMessage = "Could not save song folders.";
                    return;
            }
        }

        private void CommitAndClose()
        {
            Deactivate();
            Saved?.Invoke(this, EventArgs.Empty);
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void CloseSilently()
        {
            Deactivate();
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void CancelAndClose()
        {
            LastApplyStatus = null;
            CloseSilently();
        }

        private void RememberSelectedRoot()
        {
            if (_selectedIndex < _draftRoots.Count)
                _selectedRootIndex = _selectedIndex;
        }

        private void SetValidationStatus(SongRootValidationResult validation)
        {
            StatusMessage = GetDiagnosticMessage(validation.Diagnostics, fallback: null);
        }

        private bool IsWarningStatus()
        {
            if (string.IsNullOrWhiteSpace(StatusMessage))
                return false;

            return _rootPolicy.Validate(_draftRoots).Diagnostics.Any(diagnostic =>
                diagnostic.IsWarning && diagnostic.Message == StatusMessage);
        }

        private static string? GetDiagnosticMessage(
            IReadOnlyList<SongRootDiagnostic> diagnostics,
            string? fallback)
        {
            return diagnostics.FirstOrDefault(diagnostic => !diagnostic.IsWarning)?.Message
                ?? diagnostics.FirstOrDefault()?.Message
                ?? fallback;
        }

        private void CancelPendingPicker()
        {
            var cancellation = _pickerCancellation;
            _pickerCancellation = null;
            if (cancellation == null)
                return;

            cancellation.Cancel();
        }

        private void IncrementActivationGeneration()
        {
            unchecked
            {
                _activationGeneration++;
            }
        }

        private static bool IsBackPressed(KeyboardState current, KeyboardState previous) =>
            IsJustPressed(current, previous, Keys.Escape) ||
            IsJustPressed(current, previous, Keys.Back);

        private static bool IsJustPressed(KeyboardState current, KeyboardState previous, Keys key) =>
            current.IsKeyDown(key) && !previous.IsKeyDown(key);

        private static string GetActionLabel(ActionRow action) => action switch
        {
            ActionRow.AddFolder => "Add Folder",
            ActionRow.Remove => "Remove",
            ActionRow.MoveUp => "Move Up",
            ActionRow.MoveDown => "Move Down",
            ActionRow.Apply => "Apply",
            ActionRow.Cancel => "Cancel",
            _ => string.Empty,
        };

        private void DrawRow(
            SpriteBatch spriteBatch,
            IFont? font,
            IFont? boldFont,
            Texture2D? whitePixel,
            int boardX,
            int y,
            int boardWidth,
            int rowHeight,
            int rowIndex,
            string label)
        {
            var selected = rowIndex == _selectedIndex;
            if (whitePixel != null)
            {
                spriteBatch.Draw(whitePixel,
                    new Rectangle(boardX + 20, y, boardWidth - 40, rowHeight - 4),
                    selected ? SelectionColor : RowColor);
            }

            var displayLabel = rowIndex < _draftRoots.Count
                ? $"{rowIndex + 1}. {label}"
                : label;
            (selected ? boldFont ?? font : font)?.DrawString(spriteBatch, displayLabel,
                new Vector2(boardX + 36, y + 8), selected ? SelectedTextColor : PrimaryTextColor);
        }

        private static void DrawCenteredText(
            SpriteBatch spriteBatch,
            IFont? font,
            string text,
            int virtualWidth,
            int y,
            Color color)
        {
            if (font == null)
                return;

            var size = font.MeasureString(text);
            font.DrawString(spriteBatch, text, new Vector2((virtualWidth - size.X) / 2f, y), color);
        }

        private sealed record PickerCompletion(
            int Generation,
            CancellationTokenSource Cancellation,
            FolderPickerResult Result);
    }
}
