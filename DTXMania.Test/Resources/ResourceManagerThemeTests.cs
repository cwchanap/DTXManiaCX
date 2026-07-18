using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Trait("Category", "Unit")]
    public class ResourceManagerThemeTests : IDisposable
    {
        private sealed class HeadlessResourceManager : ResourceManager
        {
            public HeadlessResourceManager()
                : base((GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice)))
            {
            }
        }

        private readonly string _testDataPath;
        private readonly string _skinRoot;
        private readonly HeadlessResourceManager _resourceManager;

        public ResourceManagerThemeTests()
        {
            _testDataPath = Path.Combine(Path.GetTempPath(), "DTXManiaCX_Tests", Guid.NewGuid().ToString());
            _skinRoot = Path.Combine(_testDataPath, "System", "CXNeon");
            Directory.CreateDirectory(Path.Combine(_skinRoot, "Graphics"));
            _resourceManager = new HeadlessResourceManager();
        }

        public void Dispose()
        {
            _resourceManager.Dispose();
            try { Directory.Delete(_testDataPath, recursive: true); } catch { }
        }

        [Fact]
        public void CurrentTheme_WithNoThemeFile_ShouldReturnEmptyBehavior()
        {
            // Isolate from host-installed fallback / bundled skins so a Theme.ini
            // outside this test's temp tree cannot satisfy ResolveThemeFilePath.
            typeof(ResourceManager)
                .GetField("_fallbackSkinPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(_resourceManager, string.Empty);
            typeof(ResourceManager)
                .GetField("_bundledSystemSkinRoot", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(_resourceManager, null);

            _resourceManager.SetSkinPath(_skinRoot);
            Assert.Equal(Color.Red, _resourceManager.CurrentTheme.GetColor("UI.Accent", Color.Red));
        }

        [Fact]
        public void CurrentTheme_WithThemeFileAtSkinRoot_ShouldLoadValues()
        {
            File.WriteAllText(Path.Combine(_skinRoot, "Theme.ini"), "[Palette]\nUI.Accent=#22D3EE\n");
            _resourceManager.SetSkinPath(_skinRoot);

            var color = _resourceManager.CurrentTheme.GetColor("UI.Accent", Color.White);
            Assert.Equal(new Color(0x22, 0xD3, 0xEE), color);
        }

        [Fact]
        public void CurrentTheme_AfterSkinSwitch_ShouldReloadTheme()
        {
            File.WriteAllText(Path.Combine(_skinRoot, "Theme.ini"), "UI.Accent=#22D3EE\n");
            _resourceManager.SetSkinPath(_skinRoot);
            Assert.Equal(new Color(0x22, 0xD3, 0xEE), _resourceManager.CurrentTheme.GetColor("UI.Accent", Color.White));

            var otherSkin = Path.Combine(_testDataPath, "System", "Other");
            Directory.CreateDirectory(otherSkin);
            File.WriteAllText(Path.Combine(otherSkin, "Theme.ini"), "UI.Accent=#E879F9\n");
            _resourceManager.SetSkinPath(otherSkin);

            Assert.Equal(new Color(0xE8, 0x79, 0xF9), _resourceManager.CurrentTheme.GetColor("UI.Accent", Color.White));
        }

        [Fact]
        public void CurrentTheme_WithBoxDefSkinActive_ShouldUseBoxDefTheme()
        {
            File.WriteAllText(Path.Combine(_skinRoot, "Theme.ini"), "UI.Accent=#22D3EE\n");
            _resourceManager.SetSkinPath(_skinRoot);

            var boxDefSkin = Path.Combine(_testDataPath, "Songs", "BoxSkin");
            Directory.CreateDirectory(boxDefSkin);
            File.WriteAllText(Path.Combine(boxDefSkin, "Theme.ini"), "UI.Accent=#EF4444\n");
            _resourceManager.SetBoxDefSkinPath(boxDefSkin);
            _resourceManager.SetUseBoxDefSkin(true);

            Assert.Equal(new Color(0xEF, 0x44, 0x44), _resourceManager.CurrentTheme.GetColor("UI.Accent", Color.White));

            _resourceManager.SetUseBoxDefSkin(false);
            Assert.Equal(new Color(0x22, 0xD3, 0xEE), _resourceManager.CurrentTheme.GetColor("UI.Accent", Color.White));
        }

        /// <summary>
        /// Sets a private field on the ResourceManager using reflection, searching up
        /// the inheritance chain (the field may be declared on the base class).
        /// </summary>
        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            var type = target.GetType();
            FieldInfo? field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                type = type.BaseType;
            }
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }

        [Fact]
        public void CurrentTheme_WithFallbackSkinTheme_ShouldUseFallbackWhenEffectiveHasNone()
        {
            // Effective skin has no Theme.ini; the fallback skin path does.
            // ResolveThemeFilePath should skip the effective path and load from fallback.
            var skinWithoutTheme = Path.Combine(_testDataPath, "System", "NoTheme");
            Directory.CreateDirectory(skinWithoutTheme);
            _resourceManager.SetSkinPath(skinWithoutTheme);

            var fallbackSkin = Path.Combine(_testDataPath, "System", "FallbackSkin");
            Directory.CreateDirectory(fallbackSkin);
            File.WriteAllText(Path.Combine(fallbackSkin, "Theme.ini"), "UI.Accent=#EF4444\n");
            SetPrivateField(_resourceManager, "_fallbackSkinPath", fallbackSkin + Path.DirectorySeparatorChar);

            Assert.Equal(new Color(0xEF, 0x44, 0x44), _resourceManager.CurrentTheme.GetColor("UI.Accent", Color.White));
        }

        [Fact]
        public void SetSkinPath_WhenSkinChanges_ShouldInvalidateThemeBeforeRaisingSkinChanged()
        {
            // Invariant: _currentTheme is invalidated BEFORE OnSkinChanged is
            // raised, so a synchronous SkinChanged handler that reads
            // CurrentTheme observes the new skin's theme, not the cached one
            // from the previous skin. Parallel to the texture-eviction
            // ordering test in ResourceManagerLogicTests.
            File.WriteAllText(Path.Combine(_skinRoot, "Theme.ini"), "UI.Accent=#22D3EE\n");
            _resourceManager.SetSkinPath(_skinRoot);
            // Prime the theme cache for the first skin.
            Assert.Equal(new Color(0x22, 0xD3, 0xEE),
                _resourceManager.CurrentTheme.GetColor("UI.Accent", Color.White));

            var otherSkin = Path.Combine(_testDataPath, "System", "Other");
            Directory.CreateDirectory(otherSkin);
            File.WriteAllText(Path.Combine(otherSkin, "Theme.ini"), "UI.Accent=#E879F9\n");

            Color? colorObservedByHandler = null;
            _resourceManager.SkinChanged += (_, _) =>
            {
                // Read CurrentTheme from within the handler. If theme
                // invalidation ran before the event, this reloads for the new
                // skin; if it didn't, the stale cached theme is returned.
                colorObservedByHandler = _resourceManager.CurrentTheme.GetColor("UI.Accent", Color.White);
            };

            _resourceManager.SetSkinPath(otherSkin);

            Assert.NotNull(colorObservedByHandler);
            Assert.Equal(new Color(0xE8, 0x79, 0xF9), colorObservedByHandler!.Value);
        }

        [Fact]
        public void CurrentTheme_WithBundledSkinTheme_ShouldUseBundledWhenNeitherEffectiveNorFallbackHasOne()
        {
            // Neither effective nor fallback skin has Theme.ini, but the bundled
            // System skin root does. ResolveThemeFilePath should fall through both
            // and load from the bundled tier.
            var skinWithoutTheme = Path.Combine(_testDataPath, "System", "NoTheme");
            Directory.CreateDirectory(skinWithoutTheme);
            _resourceManager.SetSkinPath(skinWithoutTheme);

            var fallbackWithoutTheme = Path.Combine(_testDataPath, "System", "FallbackEmpty");
            Directory.CreateDirectory(fallbackWithoutTheme);
            SetPrivateField(_resourceManager, "_fallbackSkinPath", fallbackWithoutTheme + Path.DirectorySeparatorChar);

            var bundledSkin = Path.Combine(_testDataPath, "System", "BundledSkin");
            Directory.CreateDirectory(bundledSkin);
            File.WriteAllText(Path.Combine(bundledSkin, "Theme.ini"), "UI.Accent=#F59E0B\n");
            SetPrivateField(_resourceManager, "_bundledSystemSkinRoot", bundledSkin + Path.DirectorySeparatorChar);

            Assert.Equal(new Color(0xF5, 0x9E, 0x0B), _resourceManager.CurrentTheme.GetColor("UI.Accent", Color.White));
        }
    }
}
