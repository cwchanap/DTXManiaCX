using System;
using System.IO;
using System.Runtime.Serialization;
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
                : base((GraphicsDevice)FormatterServices.GetUninitializedObject(typeof(GraphicsDevice)))
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
    }
}
