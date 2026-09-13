using System;
using System.Linq;
using System.Reflection;

namespace DTXMania.Game.Lib.Update;

/// <summary>
/// The single game-code reader of assembly version metadata. Normalizes both the
/// local assembly version and parsed release tags to exactly three components —
/// raw System.Version semantics treat 1.2.3 and 1.2.3.0 as unequal, so every
/// comparison must go through <see cref="Parse"/>.
/// </summary>
internal static class ApplicationVersion
{
    /// <summary>Current version normalized to exactly three components.</summary>
    internal static Version Current { get; } = ReadCurrent();

    /// <summary>Current version as three-component "X.Y.Z" display text.</summary>
    internal static string Display => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    /// <summary>
    /// Version text for UI display (e.g. "0.1.0-beta"): the informational version
    /// with the SDK's "+&lt;commit&gt;" build metadata stripped so prerelease tags
    /// survive. Falls back to <see cref="Display"/> when the attribute is absent.
    /// </summary>
    internal static string DisplayWithPrerelease { get; } = ReadDisplayWithPrerelease();

    /// <summary>
    /// Raw informational/build identifier, preserving revision metadata such as
    /// "1.2.3-beta.1+abc" for crash reports.
    /// </summary>
    internal static string BuildId { get; } = ReadBuildId();

    /// <summary>
    /// Parses "v1.2.3", "1.2.3", "1.2.3.0", or "1.2.3-label+meta" to exactly three
    /// components. Returns null when no numeric major.minor.patch core exists.
    /// </summary>
    internal static Version? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var core = value.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        var parts = core.Split('.');
        if (parts.Length < 3
            || !TryParseComponent(parts[0], out int major)
            || !TryParseComponent(parts[1], out int minor)
            || !TryParseComponent(parts[2], out int patch))
        {
            return null;
        }

        return new Version(major, minor, patch);
    }

    private static Version ReadCurrent()
    {
        var assembly = typeof(ApplicationVersion).Assembly;
        return Parse(ReadBuildId())
            ?? Parse(assembly.GetName().Version?.ToString())
            ?? new Version(0, 0, 0);
    }

    private static string ReadDisplayWithPrerelease()
    {
        var informational = typeof(ApplicationVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational.Substring(0, plusIndex) : informational;
        }
        return Display;
    }

    private static string ReadBuildId()
    {
        var assembly = typeof(ApplicationVersion).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString() ?? "Unknown"
            : informationalVersion;
    }

    private static bool TryParseComponent(string text, out int value)
    {
        // Digits only, so "+1" or whitespace-padded forms cannot smuggle through.
        value = 0;
        return text.Length > 0
            && text.All(char.IsAsciiDigit)
            && int.TryParse(text, out value);
    }
}
