using System.Reflection;

namespace DTXMania.Game.Lib.Utilities
{
    /// <summary>
    /// Resolves the user-facing version string from the game assembly. Release builds stamp
    /// APP_VERSION into <see cref="AssemblyInformationalVersionAttribute"/>, which preserves
    /// prerelease tags (e.g. "0.1.0-beta") that <see cref="System.Reflection.AssemblyName.Version"/>
    /// cannot carry; the SDK may append "+&lt;commit&gt;" build metadata, which is stripped here.
    /// </summary>
    public static class AppVersion
    {
        /// <summary>
        /// The version string for UI display (e.g. "0.1.0-beta"), without any "+&lt;sha&gt;"
        /// build-metadata suffix. Falls back to the numeric assembly version when the
        /// informational attribute is absent.
        /// </summary>
        public static string GetDisplayVersion()
        {
            var assembly = typeof(AppVersion).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrEmpty(informational))
            {
                int plusIndex = informational.IndexOf('+');
                return plusIndex >= 0 ? informational.Substring(0, plusIndex) : informational;
            }
            return assembly.GetName().Version?.ToString(3) ?? "unknown";
        }
    }
}
