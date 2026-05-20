#pragma warning disable SYSLIB0050

using System;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class SkillPanelDisplayCoverageTests
    {
        #region Constructor Guard Tests

        [Fact]
        public void Constructor_NullResourceManager_ShouldThrowArgumentNullException()
        {
            var graphicsDevice = CreateUninitialized<GraphicsDevice>();

            var ex = Assert.Throws<ArgumentNullException>(
                () => new SkillPanelDisplay(null!, graphicsDevice, null));

            Assert.Equal("resourceManager", ex.ParamName);
        }

        [Fact]
        public void Constructor_NullGraphicsDevice_ShouldThrowArgumentNullException()
        {
            var resourceManager = new Mock<IResourceManager>().Object;

            var ex = Assert.Throws<ArgumentNullException>(
                () => new SkillPanelDisplay(resourceManager, null!, null));

            Assert.Equal("graphicsDevice", ex.ParamName);
        }

        [Fact]
        public void Constructor_TextureLoadFails_ShouldSetMaxBadgeTextureNull()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(rm => rm.LoadTexture(It.IsAny<string>()))
                .Throws(new Exception("texture load failure"));

            var display = WithManagedFontUnavailable(() =>
                new SkillPanelDisplay(resourceManager.Object, CreateUninitialized<GraphicsDevice>(), null));

            Assert.Null(GetPrivateField<ITexture?>(display, "_maxBadgeTexture"));
            display.Dispose();
        }

        [Fact]
        public void Constructor_TextureLoadSucceeds_ShouldStoreMaxBadgeTexture()
        {
            var resourceManager = new Mock<IResourceManager>();
            var maxBadgeTexture = new Mock<ITexture>().Object;
            resourceManager
                .Setup(rm => rm.LoadTexture(TexturePath.SkillMax))
                .Returns(maxBadgeTexture);

            var display = WithManagedFontUnavailable(() =>
                new SkillPanelDisplay(resourceManager.Object, CreateUninitialized<GraphicsDevice>(), null));

            Assert.Same(maxBadgeTexture, GetPrivateField<ITexture?>(display, "_maxBadgeTexture"));
            display.Dispose();
        }

        #endregion

        #region Update Tests

        [Fact]
        public void Update_ShouldNotThrow()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();

            var exception = Record.Exception(() => display.Update(1.0));
            Assert.Null(exception);
        }

        #endregion

        #region Draw Guard Tests

        [Fact]
        public void Draw_WhenDisposed_ShouldReturnImmediately()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();
            SetPrivateField(display, "_disposed", true);
            SetPrivateField(display, "_font", CreateUninitialized<ManagedFont>());

            var exception = Record.Exception(() => display.Draw(null!));
            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WhenSpriteBatchNull_ShouldReturnImmediately()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_font", CreateUninitialized<ManagedFont>());

            var exception = Record.Exception(() => display.Draw(null!));
            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WhenFontNull_ShouldReturnImmediately()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();
            SetPrivateField(display, "_disposed", false);
            SetPrivateField(display, "_font", null);

            var exception = Record.Exception(() => display.Draw(null!));
            Assert.Null(exception);
        }

        #endregion

        #region Property Tests

        [Fact]
        public void Skill_Property_ShouldGetAndSet()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();

            display.Skill = 87.5;
            Assert.Equal(87.5, display.Skill);

            display.Skill = 0.0;
            Assert.Equal(0.0, display.Skill);
        }

        [Fact]
        public void ShowMax_Property_ShouldGetAndSet()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();

            display.ShowMax = true;
            Assert.True(display.ShowMax);

            display.ShowMax = false;
            Assert.False(display.ShowMax);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_ShouldReleaseFontAndTextures()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();
            var mockTexture = new Mock<ITexture>();
            SetPrivateField(display, "_font", null);
            SetPrivateField(display, "_maxBadgeTexture", mockTexture.Object);
            SetPrivateField(display, "_disposed", false);

            display.Dispose();

            Assert.Null(GetPrivateField<ManagedFont?>(display, "_font"));
            Assert.Null(GetPrivateField<ITexture?>(display, "_maxBadgeTexture"));
            Assert.True(GetPrivateField<bool>(display, "_disposed"));
            mockTexture.Verify(t => t.RemoveReference(), Times.Once);
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var display = CreateUninitialized<SkillPanelDisplay>();
            SetPrivateField(display, "_font", null);
            SetPrivateField(display, "_maxBadgeTexture", null);
            SetPrivateField(display, "_disposed", false);

            display.Dispose();
            var exception = Record.Exception(() => display.Dispose());
            Assert.Null(exception);
        }

        #endregion

        #region Helper Methods

        private static T WithManagedFontUnavailable<T>(Func<T> action)
        {
            var managedFontType = typeof(ManagedFont);
            var factoryLock = managedFontType
                .GetField("_fontFactoryLock", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;

            lock (factoryLock)
            {
                var contentManagerField = managedFontType.GetField("_contentManager", BindingFlags.Static | BindingFlags.NonPublic)!;
                var originalContentManager = contentManagerField.GetValue(null);

                var loadedFontsField = managedFontType.GetField("_loadedFonts", BindingFlags.Static | BindingFlags.NonPublic)!;
                var loadedFonts = (System.Collections.IDictionary)loadedFontsField.GetValue(null)!;
                var originalEntries = new System.Collections.DictionaryEntry[loadedFonts.Count];
                loadedFonts.CopyTo(originalEntries, 0);

                try
                {
                    contentManagerField.SetValue(null, null);
                    loadedFonts.Clear();
                    return action();
                }
                finally
                {
                    loadedFonts.Clear();
                    foreach (System.Collections.DictionaryEntry entry in originalEntries)
                    {
                        loadedFonts[entry.Key] = entry.Value;
                    }
                    contentManagerField.SetValue(null, originalContentManager);
                }
            }
        }

        #endregion
    }
}
