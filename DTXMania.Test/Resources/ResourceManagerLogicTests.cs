using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Utilities;
using Moq;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Resources
{
    [Trait("Category", "Unit")]
    public class ResourceManagerLogicTests : IDisposable
    {
        private readonly string _testDataPath;
        private readonly string _defaultSkinRoot;
        private readonly string _customSkinRoot;
        private readonly string _boxDefSkinRoot;

        public ResourceManagerLogicTests()
        {
            _testDataPath = Path.Combine(Path.GetTempPath(), "DTXManiaCX_Tests", Guid.NewGuid().ToString());
            _defaultSkinRoot = Path.Combine(_testDataPath, "System");
            _customSkinRoot = Path.Combine(_defaultSkinRoot, "Custom");
            _boxDefSkinRoot = Path.Combine(_testDataPath, "Songs", "BoxSkin");

            CreateSkinRoot(_defaultSkinRoot);
            CreateSkinRoot(_customSkinRoot);
            CreateSkinRoot(_boxDefSkinRoot);

            File.WriteAllText(Path.Combine(_defaultSkinRoot, "Graphics", "fallback_only.png"), "fallback");
            File.WriteAllText(Path.Combine(_customSkinRoot, "Graphics", "current_only.png"), "current");
            File.WriteAllText(Path.Combine(_boxDefSkinRoot, "Graphics", "box_only.png"), "box");
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void SetSkinPath_WithNullOrEmpty_ShouldThrowArgumentException(string? skinPath)
        {
            var resourceManager = CreateResourceManager();

            Assert.Throws<ArgumentException>(() => resourceManager.SetSkinPath(skinPath!));
        }

        [Fact]
        public void SetSkinPath_WithValidSkin_ShouldNormalizePathAndRaiseSkinChanged()
        {
            var resourceManager = CreateResourceManager();
            SkinChangedEventArgs? eventArgs = null;
            resourceManager.SkinChanged += (_, args) => eventArgs = args;

            resourceManager.SetSkinPath(_customSkinRoot);

            var expectedPath = NormalizeDirectory(_customSkinRoot);
            Assert.Equal(expectedPath, resourceManager.GetCurrentEffectiveSkinPath());
            Assert.NotNull(eventArgs);
            Assert.Equal(NormalizeDirectory(AppPaths.GetDefaultSystemSkinRoot()), eventArgs!.OldSkinPath);
            Assert.Equal(expectedPath, eventArgs.NewSkinPath);
        }

        [Fact]
        public void ResolvePath_WithBoxDefEnabled_ShouldUseBoxDefSkin()
        {
            var resourceManager = CreateResourceManager(_customSkinRoot, _defaultSkinRoot);
            resourceManager.SetBoxDefSkinPath(_boxDefSkinRoot);
            resourceManager.SetUseBoxDefSkin(true);

            var resolvedPath = resourceManager.ResolvePath("Graphics/box_only.png");

            Assert.Equal(
                Path.GetFullPath(Path.Combine(_boxDefSkinRoot, "Graphics", "box_only.png")),
                resolvedPath);
        }

        [Fact]
        public void ResolvePath_WithBoxDefDisabled_ShouldUseCurrentSkin()
        {
            var resourceManager = CreateResourceManager(_customSkinRoot, _defaultSkinRoot);
            resourceManager.SetBoxDefSkinPath(_boxDefSkinRoot);
            resourceManager.SetUseBoxDefSkin(false);

            var resolvedPath = resourceManager.ResolvePath("Graphics/current_only.png");

            Assert.Equal(
                Path.GetFullPath(Path.Combine(_customSkinRoot, "Graphics", "current_only.png")),
                resolvedPath);
        }

        [Fact]
        public void ResolvePath_WithAbsolutePath_ShouldReturnAbsolutePath()
        {
            var resourceManager = CreateResourceManager();
            var absolutePath = Path.GetFullPath(Path.Combine(_testDataPath, "absolute.txt"));
            File.WriteAllText(absolutePath, "absolute");

            var resolvedPath = resourceManager.ResolvePath(absolutePath);

            Assert.Equal(absolutePath, resolvedPath);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void LoadTexture_WithNullOrEmptyPath_ShouldThrowArgumentException(string? path)
        {
            var resourceManager = CreateResourceManager();

            Assert.Throws<ArgumentException>(() => resourceManager.LoadTexture(path!));
        }

        [Fact]
        public void LoadTexture_WhenCachedTextureExists_ShouldAddReferenceIncrementHitsAndReturnCachedTexture()
        {
            var resourceManager = CreateResourceManager();
            var textureCache = GetPrivateField<ConcurrentDictionary<string, ITexture>>(resourceManager, "_textureCache");
            var cachedTexture = CreateTextureMock();
            textureCache["graphics/test.png|False"] = cachedTexture.Object;

            var loadedTexture = resourceManager.LoadTexture("Graphics/Test.png");

            Assert.Same(cachedTexture.Object, loadedTexture);
            cachedTexture.Verify(x => x.AddReference(), Times.Once);
            Assert.Equal(1, GetPrivateField<int>(resourceManager, "_cacheHits"));
            Assert.Equal(0, GetPrivateField<int>(resourceManager, "_cacheMisses"));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void LoadFont_WithNullOrEmptyPath_ShouldThrowArgumentException(string? path)
        {
            var resourceManager = CreateResourceManager();

            Assert.Throws<ArgumentException>(() => resourceManager.LoadFont(path!, 24));
        }

        [Fact]
        public void LoadFont_WhenCachedFontExists_ShouldAddReferenceIncrementHitsAndReturnCachedFont()
        {
            var resourceManager = CreateResourceManager();
            var fontCache = GetPrivateField<ConcurrentDictionary<string, IFont>>(resourceManager, "_fontCache");
            var cachedFont = CreateFontMock();
            fontCache["fonts/test.ttf|24|Regular"] = cachedFont.Object;

            var loadedFont = resourceManager.LoadFont("Fonts/Test.ttf", 24);

            Assert.Same(cachedFont.Object, loadedFont);
            cachedFont.Verify(x => x.AddReference(), Times.Once);
            Assert.Equal(1, GetPrivateField<int>(resourceManager, "_cacheHits"));
            Assert.Equal(0, GetPrivateField<int>(resourceManager, "_cacheMisses"));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void LoadSound_WithNullOrEmptyPath_ShouldThrowArgumentException(string? path)
        {
            var resourceManager = CreateResourceManager();

            Assert.Throws<ArgumentException>(() => resourceManager.LoadSound(path!));
        }

        [Fact]
        public void LoadSound_WhenCachedSoundExists_ShouldAddReferenceIncrementHitsAndReturnCachedSound()
        {
            var resourceManager = CreateResourceManager();
            var soundCache = GetPrivateField<ConcurrentDictionary<string, ISound>>(resourceManager, "_soundCache");
            var cachedSound = CreateSoundMock();
            soundCache["sounds/test.ogg".ToLowerInvariant()] = cachedSound.Object;

            var loadedSound = resourceManager.LoadSound("Sounds/Test.ogg");

            Assert.Same(cachedSound.Object, loadedSound);
            cachedSound.Verify(x => x.AddReference(), Times.Once);
            Assert.Equal(1, GetPrivateField<int>(resourceManager, "_cacheHits"));
            Assert.Equal(0, GetPrivateField<int>(resourceManager, "_cacheMisses"));
        }

        [Fact]
        public void CreateTextureFromColor_WhenCachedTextureExists_ShouldAddReferenceIncrementHitsAndReturnCachedTexture()
        {
            var resourceManager = CreateResourceManager();
            var textureCache = GetPrivateField<ConcurrentDictionary<string, ITexture>>(resourceManager, "_textureCache");
            var cachedTexture = CreateTextureMock();
            var cacheKey = $"__Color|{Color.CornflowerBlue.PackedValue}";
            textureCache[cacheKey] = cachedTexture.Object;

            var createdTexture = resourceManager.CreateTextureFromColor(Color.CornflowerBlue);

            Assert.Same(cachedTexture.Object, createdTexture);
            cachedTexture.Verify(x => x.AddReference(), Times.Once);
            Assert.Equal(1, GetPrivateField<int>(resourceManager, "_cacheHits"));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void ResourceExists_WithNullOrEmptyPath_ShouldReturnFalse(string? relativePath)
        {
            var resourceManager = CreateResourceManager(_customSkinRoot, _defaultSkinRoot);

            Assert.False(resourceManager.ResourceExists(relativePath!));
        }

        [Fact]
        public void ResourceExists_WhenPresentInFallbackSkin_ShouldReturnTrue()
        {
            var resourceManager = CreateResourceManager(_customSkinRoot, _defaultSkinRoot);

            var exists = resourceManager.ResourceExists("Graphics/fallback_only.png");

            Assert.True(exists);
        }

        [Fact]
        public void ResourceExists_WhenPresentInCurrentSkin_ShouldReturnTrue()
        {
            var resourceManager = CreateResourceManager(_customSkinRoot, _defaultSkinRoot);

            var exists = resourceManager.ResourceExists("Graphics/current_only.png");

            Assert.True(exists);
        }

        [Fact]
        public void ResourceExists_WhenMissingEverywhere_ShouldReturnFalse()
        {
            var resourceManager = CreateResourceManager(_customSkinRoot, _defaultSkinRoot);

            Assert.False(resourceManager.ResourceExists("Graphics/missing.png"));
        }

        [Fact]
        public void SetBoxDefSkinPath_WithRelativePath_ShouldPreserveRelativePath()
        {
            var resourceManager = CreateResourceManager();

            resourceManager.SetBoxDefSkinPath("songs/mysong/skin");

            var effectivePath = resourceManager.GetCurrentEffectiveSkinPath();
            Assert.False(Path.IsPathRooted(effectivePath));
            Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), effectivePath);
            Assert.Contains("songs/mysong/skin", effectivePath.Replace('\\', '/'));
        }

        [Fact]
        public void SetBoxDefSkinPath_WithEmptyValue_ShouldClearOverride()
        {
            var resourceManager = CreateResourceManager();
            resourceManager.SetBoxDefSkinPath("songs/mysong/skin");

            resourceManager.SetBoxDefSkinPath(string.Empty);

            Assert.Equal(NormalizeDirectory(AppPaths.GetDefaultSystemSkinRoot()), resourceManager.GetCurrentEffectiveSkinPath());
        }

        [Fact]
        public void GetUsageInfo_ShouldReportCacheCountsAndApproximateMemory()
        {
            var resourceManager = CreateResourceManager();
            var textureCache = GetPrivateField<ConcurrentDictionary<string, ITexture>>(resourceManager, "_textureCache");
            var fontCache = GetPrivateField<ConcurrentDictionary<string, IFont>>(resourceManager, "_fontCache");
            var soundCache = GetPrivateField<ConcurrentDictionary<string, ISound>>(resourceManager, "_soundCache");

            textureCache["texture-a"] = CreateTextureMock(referenceCount: 1, memoryUsage: 10).Object;
            textureCache["texture-b"] = CreateTextureMock(referenceCount: 2, memoryUsage: 20).Object;
            fontCache["font-a"] = CreateFontMock(referenceCount: 1).Object;
            soundCache["sound-a"] = CreateSoundMock(referenceCount: 1).Object;

            SetPrivateField(resourceManager, "_cacheHits", 7);
            SetPrivateField(resourceManager, "_cacheMisses", 3);

            var usageInfo = resourceManager.GetUsageInfo();

            Assert.Equal(2, usageInfo.LoadedTextures);
            Assert.Equal(1, usageInfo.LoadedFonts);
            Assert.Equal(1, usageInfo.LoadedSounds);
            Assert.Equal(10 + 20 + 1024 + 512, usageInfo.TotalMemoryUsage);
            Assert.Equal(7, usageInfo.CacheHits);
            Assert.Equal(3, usageInfo.CacheMisses);
        }

        [Fact]
        public void UnloadByPattern_ShouldDisposeAndRemoveMatchingResources()
        {
            var resourceManager = CreateResourceManager();
            var textureCache = GetPrivateField<ConcurrentDictionary<string, ITexture>>(resourceManager, "_textureCache");
            var fontCache = GetPrivateField<ConcurrentDictionary<string, IFont>>(resourceManager, "_fontCache");
            var soundCache = GetPrivateField<ConcurrentDictionary<string, ISound>>(resourceManager, "_soundCache");

            var matchingTexture = CreateTextureMock();
            var otherTexture = CreateTextureMock();
            var matchingFont = CreateFontMock();
            var otherFont = CreateFontMock();
            var matchingSound = CreateSoundMock();
            var otherSound = CreateSoundMock();

            textureCache["Songs/texture"] = matchingTexture.Object;
            textureCache["Graphics/texture"] = otherTexture.Object;
            fontCache["Songs/font"] = matchingFont.Object;
            fontCache["Fonts/font"] = otherFont.Object;
            soundCache["Songs/sound"] = matchingSound.Object;
            soundCache["Sounds/sound"] = otherSound.Object;

            resourceManager.UnloadByPattern("Songs");

            matchingTexture.Verify(x => x.Dispose(), Times.Once);
            matchingFont.Verify(x => x.Dispose(), Times.Once);
            matchingSound.Verify(x => x.Dispose(), Times.Once);
            otherTexture.Verify(x => x.Dispose(), Times.Never);
            otherFont.Verify(x => x.Dispose(), Times.Never);
            otherSound.Verify(x => x.Dispose(), Times.Never);
            Assert.Single(textureCache);
            Assert.Single(fontCache);
            Assert.Single(soundCache);
        }

        [Fact]
        public void CollectUnusedResources_ShouldDisposeOnlyZeroReferenceEntries()
        {
            var resourceManager = CreateResourceManager();
            var textureCache = GetPrivateField<ConcurrentDictionary<string, ITexture>>(resourceManager, "_textureCache");
            var fontCache = GetPrivateField<ConcurrentDictionary<string, IFont>>(resourceManager, "_fontCache");
            var soundCache = GetPrivateField<ConcurrentDictionary<string, ISound>>(resourceManager, "_soundCache");

            var unusedTexture = CreateTextureMock(referenceCount: 0);
            var usedTexture = CreateTextureMock(referenceCount: 1);
            var unusedFont = CreateFontMock(referenceCount: 0);
            var usedFont = CreateFontMock(referenceCount: 1);
            var unusedSound = CreateSoundMock(referenceCount: 0);
            var usedSound = CreateSoundMock(referenceCount: 1);

            textureCache["unused-texture"] = unusedTexture.Object;
            textureCache["used-texture"] = usedTexture.Object;
            fontCache["unused-font"] = unusedFont.Object;
            fontCache["used-font"] = usedFont.Object;
            soundCache["unused-sound"] = unusedSound.Object;
            soundCache["used-sound"] = usedSound.Object;

            resourceManager.CollectUnusedResources();

            unusedTexture.Verify(x => x.Dispose(), Times.Once);
            unusedFont.Verify(x => x.Dispose(), Times.Once);
            unusedSound.Verify(x => x.Dispose(), Times.Once);
            usedTexture.Verify(x => x.Dispose(), Times.Never);
            usedFont.Verify(x => x.Dispose(), Times.Never);
            usedSound.Verify(x => x.Dispose(), Times.Never);
            Assert.Single(textureCache);
            Assert.Single(fontCache);
            Assert.Single(soundCache);
        }

        [Fact]
        public void Dispose_ShouldUnloadAllCachesAndMarkInstanceDisposed()
        {
            var resourceManager = CreateResourceManager();
            var textureCache = GetPrivateField<ConcurrentDictionary<string, ITexture>>(resourceManager, "_textureCache");
            var fontCache = GetPrivateField<ConcurrentDictionary<string, IFont>>(resourceManager, "_fontCache");
            var soundCache = GetPrivateField<ConcurrentDictionary<string, ISound>>(resourceManager, "_soundCache");

            var texture = CreateTextureMock();
            var font = CreateFontMock();
            var sound = CreateSoundMock();

            textureCache["texture"] = texture.Object;
            fontCache["font"] = font.Object;
            soundCache["sound"] = sound.Object;

            resourceManager.Dispose();

            texture.Verify(x => x.Dispose(), Times.Once);
            font.Verify(x => x.Dispose(), Times.Once);
            sound.Verify(x => x.Dispose(), Times.Once);
            Assert.Empty(textureCache);
            Assert.Empty(fontCache);
            Assert.Empty(soundCache);
            Assert.True(GetPrivateField<bool>(resourceManager, "_disposed"));
        }

        [Fact]
        public void UnloadAll_ShouldDisposeCachesForAllResourceTypes()
        {
            var resourceManager = CreateResourceManager();
            var textureCache = GetPrivateField<ConcurrentDictionary<string, ITexture>>(resourceManager, "_textureCache");
            var fontCache = GetPrivateField<ConcurrentDictionary<string, IFont>>(resourceManager, "_fontCache");
            var soundCache = GetPrivateField<ConcurrentDictionary<string, ISound>>(resourceManager, "_soundCache");

            var texture1 = CreateTextureMock();
            var texture2 = CreateTextureMock();
            var font1 = CreateFontMock();
            var sound1 = CreateSoundMock();

            textureCache["texture-a"] = texture1.Object;
            textureCache["texture-b"] = texture2.Object;
            fontCache["font-a"] = font1.Object;
            soundCache["sound-a"] = sound1.Object;

            resourceManager.UnloadAll();

            texture1.Verify(x => x.Dispose(), Times.Once);
            texture2.Verify(x => x.Dispose(), Times.Once);
            font1.Verify(x => x.Dispose(), Times.Once);
            sound1.Verify(x => x.Dispose(), Times.Once);
            Assert.Empty(textureCache);
            Assert.Empty(fontCache);
            Assert.Empty(soundCache);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void UnloadByPattern_WithEmptyOrNullPattern_ShouldReturnEarly(string? pathPattern)
        {
            var resourceManager = CreateResourceManager();
            var textureCache = GetPrivateField<ConcurrentDictionary<string, ITexture>>(resourceManager, "_textureCache");

            var texture = CreateTextureMock();
            textureCache["any-path"] = texture.Object;

            resourceManager.UnloadByPattern(pathPattern!);

            texture.Verify(x => x.Dispose(), Times.Never);
            Assert.Single(textureCache);
        }

        [Fact]
        public void GetCurrentEffectiveSkinPath_WithBoxDefNotSet_ShouldReturnCurrentSkinPath()
        {
            var resourceManager = CreateResourceManager(_customSkinRoot, _defaultSkinRoot);

            var effectivePath = resourceManager.GetCurrentEffectiveSkinPath();

            Assert.Equal(NormalizeDirectory(_customSkinRoot), effectivePath);
        }

        [Fact]
        public void GetCurrentEffectiveSkinPath_WithBoxDefSetButDisabled_ShouldReturnCurrentSkinPath()
        {
            var resourceManager = CreateResourceManager(_customSkinRoot, _defaultSkinRoot);
            resourceManager.SetBoxDefSkinPath(_boxDefSkinRoot);
            resourceManager.SetUseBoxDefSkin(false);

            var effectivePath = resourceManager.GetCurrentEffectiveSkinPath();

            Assert.Equal(NormalizeDirectory(_customSkinRoot), effectivePath);
        }

        [Fact]
        public void InitializeDefaultSkinPath_WhenValidationFails_ShouldStillUseDefaultPathAndCreateStructure()
        {
            var nonExistentRoot = Path.Combine(_testDataPath, "MissingSystem");
            var resourceManager = CreateResourceManagerWithoutInitialization();
            SetPrivateField(resourceManager, "_currentSkinPath", string.Empty);
            SetPrivateField(resourceManager, "_fallbackSkinPath", string.Empty);
            SetPrivateField(resourceManager, "_cachedAppDataRoot", nonExistentRoot);

            InvokePrivateMethod(resourceManager, "InitializeDefaultSkinPath");

            var currentSkinPath = GetPrivateField<string>(resourceManager, "_currentSkinPath");
            var fallbackSkinPath = GetPrivateField<string>(resourceManager, "_fallbackSkinPath");
            var expectedSystemPath = Path.Combine(nonExistentRoot, "System");
            var normalizedExpectedPath = expectedSystemPath.Replace('\\', Path.DirectorySeparatorChar)
                                                            .Replace('/', Path.DirectorySeparatorChar);
            if (!normalizedExpectedPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                normalizedExpectedPath += Path.DirectorySeparatorChar;
            }

            Assert.Equal(normalizedExpectedPath, currentSkinPath);
            Assert.Equal(normalizedExpectedPath, fallbackSkinPath);

            // Verify directory structure was created
            Assert.True(Directory.Exists(expectedSystemPath), "System directory should be created");
            Assert.True(Directory.Exists(Path.Combine(expectedSystemPath, "Graphics")), "Graphics subdirectory should be created");
            Assert.True(Directory.Exists(Path.Combine(expectedSystemPath, "Fonts")), "Fonts subdirectory should be created");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDataPath))
                {
                    Directory.Delete(_testDataPath, true);
                }
            }
            catch
            {
                // Best-effort cleanup for temp test directories.
            }
        }

        private static ResourceManager CreateResourceManager(string? currentSkinPath = null, string? fallbackSkinPath = null)
        {
            var resourceManager = CreateResourceManagerWithoutInitialization();
            SetPrivateField(resourceManager, "_currentSkinPath", NormalizeDirectory(currentSkinPath ?? AppPaths.GetDefaultSystemSkinRoot()));
            SetPrivateField(resourceManager, "_fallbackSkinPath", NormalizeDirectory(fallbackSkinPath ?? currentSkinPath ?? AppPaths.GetDefaultSystemSkinRoot()));
            return resourceManager;
        }

        private static ResourceManager CreateResourceManagerWithoutInitialization()
        {
#pragma warning disable SYSLIB0050
            var resourceManager = (ResourceManager)FormatterServices.GetUninitializedObject(typeof(ResourceManager));
#pragma warning restore SYSLIB0050

            SetPrivateField(resourceManager, "_textureCache", new ConcurrentDictionary<string, ITexture>());
            SetPrivateField(resourceManager, "_fontCache", new ConcurrentDictionary<string, IFont>());
            SetPrivateField(resourceManager, "_soundCache", new ConcurrentDictionary<string, ISound>());
            SetPrivateField(resourceManager, "_lockObject", new object());
            SetPrivateField(resourceManager, "_cachedAppDataRoot", AppPaths.GetAppDataRoot());
            SetPrivateField(resourceManager, "_totalLoadTime", new Stopwatch());
            SetPrivateField(resourceManager, "_boxDefSkinPath", string.Empty);
            SetPrivateField(resourceManager, "_useBoxDefSkin", true);
            SetPrivateField(resourceManager, "_disposed", false);

            return resourceManager;
        }

        private static Mock<ITexture> CreateTextureMock(int referenceCount = 1, long memoryUsage = 0)
        {
            var mock = new Mock<ITexture>();
            mock.SetupGet(x => x.ReferenceCount).Returns(referenceCount);
            mock.SetupGet(x => x.MemoryUsage).Returns(memoryUsage);
            mock.SetupGet(x => x.SourcePath).Returns("mock-texture");
            return mock;
        }

        private static Mock<IFont> CreateFontMock(int referenceCount = 1)
        {
            var mock = new Mock<IFont>();
            mock.SetupGet(x => x.ReferenceCount).Returns(referenceCount);
            mock.SetupGet(x => x.SourcePath).Returns("mock-font");
            return mock;
        }

        private static Mock<ISound> CreateSoundMock(int referenceCount = 1)
        {
            var mock = new Mock<ISound>();
            mock.SetupGet(x => x.ReferenceCount).Returns(referenceCount);
            mock.SetupGet(x => x.SourcePath).Returns("mock-sound");
            return mock;
        }

        private static void CreateSkinRoot(string rootPath)
        {
            Directory.CreateDirectory(Path.Combine(rootPath, "Graphics"));
            File.WriteAllText(Path.Combine(rootPath, "Graphics", "1_background.jpg"), "background");
        }

        private static string NormalizeDirectory(string path)
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? fullPath
                : fullPath + Path.DirectorySeparatorChar;
        }
    }
}
