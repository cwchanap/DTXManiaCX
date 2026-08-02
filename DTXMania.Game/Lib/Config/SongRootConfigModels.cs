#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace DTXMania.Game.Lib.Config
{
    public enum SongRootUpdateStatus
    {
        Updated,
        Unchanged,
        ValidationFailed,
        PersistenceFailed,
    }

    public sealed record SongRootDiagnostic(
        string Path,
        string Message,
        bool IsWarning);

    public sealed record SongRootUpdateResult
    {
        public SongRootUpdateStatus Status { get; }
        public IReadOnlyList<string> CanonicalRoots { get; }
        public IReadOnlyList<SongRootDiagnostic> Diagnostics { get; }

        public SongRootUpdateResult(
            SongRootUpdateStatus status,
            IReadOnlyList<string> canonicalRoots,
            IReadOnlyList<SongRootDiagnostic> diagnostics)
        {
            ArgumentNullException.ThrowIfNull(canonicalRoots);
            ArgumentNullException.ThrowIfNull(diagnostics);

            Status = status;
            CanonicalRoots = Array.AsReadOnly(canonicalRoots.ToArray());
            Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        }

        public void Deconstruct(
            out SongRootUpdateStatus status,
            out IReadOnlyList<string> canonicalRoots,
            out IReadOnlyList<SongRootDiagnostic> diagnostics)
        {
            status = Status;
            canonicalRoots = CanonicalRoots;
            diagnostics = Diagnostics;
        }
    }

    public sealed class SongRootsChangedEventArgs : EventArgs
    {
        public IReadOnlyList<string> OldRoots { get; }
        public IReadOnlyList<string> NewRoots { get; }

        public SongRootsChangedEventArgs(
            IReadOnlyList<string> oldRoots,
            IReadOnlyList<string> newRoots)
        {
            ArgumentNullException.ThrowIfNull(oldRoots);
            ArgumentNullException.ThrowIfNull(newRoots);

            OldRoots = Array.AsReadOnly(oldRoots.ToArray());
            NewRoots = Array.AsReadOnly(newRoots.ToArray());
        }
    }
}
