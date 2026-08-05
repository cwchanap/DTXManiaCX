#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal sealed class CrashContextSnapshotStore : ICrashContextSink, ICrashSensitiveDataSink
{
    private readonly object _gate = new();
    private readonly Dictionary<CrashContextKind, CrashContextSnapshot> _snapshots = new();
    private readonly HashSet<string> _sensitivePaths = new(AppPaths.SkinPathComparer);

    public void SetSnapshot(CrashContextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var normalizedSnapshot = new CrashContextSnapshot(
            snapshot.Kind,
            snapshot.Status,
            NormalizeFields(snapshot.Kind, snapshot.Fields),
            CrashLogFieldPolicy.Default.NormalizeContextFailureCode(
                snapshot.Kind,
                snapshot.FailureCode));

        lock (_gate)
        {
            _snapshots[snapshot.Kind] = normalizedSnapshot;
        }
    }

    public void RegisterPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var normalizedPath = Path.GetFullPath(path);
            lock (_gate)
            {
                _sensitivePaths.Add(normalizedPath);
            }
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or NotSupportedException
                                          or PathTooLongException
                                          or UnauthorizedAccessException
                                          or System.Security.SecurityException)
        {
        }
    }

    internal IReadOnlyList<CrashContextSnapshot> Snapshot()
    {
        lock (_gate)
        {
            var snapshots = new List<CrashContextSnapshot>(_snapshots.Count);
            foreach (var kind in Enum.GetValues<CrashContextKind>())
            {
                if (_snapshots.TryGetValue(kind, out var snapshot))
                {
                    snapshots.Add(snapshot);
                }
            }

            return snapshots.ToArray();
        }
    }

    internal IReadOnlyList<string> SensitivePathSnapshot()
    {
        lock (_gate)
        {
            return _sensitivePaths.ToArray();
        }
    }

    private static IReadOnlyDictionary<string, object?> NormalizeFields(
        CrashContextKind kind,
        IReadOnlyDictionary<string, object?> fields)
    {
        var normalizedFields = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (CrashLogFieldPolicy.Default.TryNormalizeContextProperty(
                    kind,
                    field.Key,
                    field.Value,
                    out var normalizedValue))
            {
                normalizedFields[field.Key] = normalizedValue;
            }
        }

        return normalizedFields.Count == 0
            ? ReadOnlyDictionary<string, object?>.Empty
            : new ReadOnlyDictionary<string, object?>(normalizedFields);
    }
}
