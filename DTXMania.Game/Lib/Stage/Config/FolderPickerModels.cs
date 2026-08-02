#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Config;

namespace DTXMania.Game.Lib.Stage.Config
{
    /// <summary>Outcome of a native folder-selection request.</summary>
    public enum FolderPickerStatus
    {
        Selected,
        Cancelled,
        Unavailable,
        Failed,
    }

    /// <summary>
    /// Immutable result of a native folder-selection request. Only a successful
    /// <see cref="FolderPickerStatus.Selected"/> result carries a usable path.
    /// </summary>
    public sealed record FolderPickerResult
    {
        public FolderPickerResult(
            FolderPickerStatus status,
            string? path = null,
            string? message = null)
        {
            if (status == FolderPickerStatus.Selected && string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "A selected folder result requires a non-empty path.",
                    nameof(path));
            }

            if (status != FolderPickerStatus.Selected && path != null)
            {
                throw new ArgumentException(
                    "Only a selected folder result may provide a path.",
                    nameof(path));
            }

            Status = status;
            Path = path;
            Message = message;
        }

        public FolderPickerStatus Status { get; }

        public string? Path { get; }

        public string? Message { get; }

        public static FolderPickerResult Selected(string path) =>
            new(FolderPickerStatus.Selected, path);

        public static FolderPickerResult Cancelled() =>
            new(FolderPickerStatus.Cancelled);

        public static FolderPickerResult Unavailable(string? message = null) =>
            new(FolderPickerStatus.Unavailable, message: message);

        public static FolderPickerResult Failed(string? message = null) =>
            new(FolderPickerStatus.Failed, message: message);
    }

    /// <summary>Provides the native folder picker for the current platform.</summary>
    public interface IFolderPickerService
    {
        Task<FolderPickerResult> PickFolderAsync(
            string? initialDirectory,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Result of the Config-owned apply handoff. This is intentionally separate
    /// from the persisted <see cref="SongRootUpdateStatus"/> contract so later
    /// operation coordination can add transient states without changing
    /// <c>IConfigManager</c>.
    /// </summary>
    internal sealed record SongFolderApplyResult
    {
        internal SongFolderApplyResult(
            SongFolderApplyStatus status,
            IReadOnlyList<string> canonicalRoots,
            IReadOnlyList<SongRootDiagnostic> diagnostics)
        {
            ArgumentNullException.ThrowIfNull(canonicalRoots);
            ArgumentNullException.ThrowIfNull(diagnostics);

            Status = status;
            CanonicalRoots = Array.AsReadOnly(canonicalRoots.ToArray());
            Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        }

        internal SongFolderApplyStatus Status { get; }

        internal IReadOnlyList<string> CanonicalRoots { get; }

        internal IReadOnlyList<SongRootDiagnostic> Diagnostics { get; }
    }

    internal enum SongFolderApplyStatus
    {
        Updated,
        Unchanged,
        Busy,
        ValidationFailed,
        PersistenceFailed,
        Started,
    }
}
