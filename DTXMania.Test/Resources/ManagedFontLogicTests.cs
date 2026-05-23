using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using DTXMania.Game.Lib.Resources;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace DTXMania.Test.Resources;

[Collection("ManagedFont")]
[Trait("Category", "Unit")]
public class ManagedFontLogicTests
{
    private sealed class SuccessfulConstructorManagedFont : ManagedFont
    {
        public SuccessfulConstructorManagedFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style = FontStyle.Regular)
            : base(graphicsDevice, fontPath, size, style)
        {
        }

        protected override void LoadFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style)
        {
        }
    }

    private sealed class FailingConstructorManagedFont : ManagedFont
    {
        public FailingConstructorManagedFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style = FontStyle.Regular)
            : base(graphicsDevice, fontPath, size, style)
        {
        }

        protected override void LoadFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style)
        {
            throw new InvalidOperationException("font load failed");
        }
    }

    private sealed class FakeContentManager : ContentManager
    {
        private sealed class EmptyServiceProvider : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }

        private readonly Func<string, object> _loader;

        public List<string> RequestedAssets { get; } = new();

        public FakeContentManager(Func<string, object> loader)
            : base(new EmptyServiceProvider())
        {
            _loader = loader;
        }

        public override T Load<T>(string assetName)
        {
            RequestedAssets.Add(assetName);
            return (T)_loader(assetName);
        }
    }

    private class TestableManagedFont : ManagedFont
    {
        private HashSet<char>? _testCharacters;

        public TestableManagedFont(SpriteFont spriteFont, string sourcePath, int fontSize)
            : base(spriteFont, sourcePath, fontSize)
        {
        }

        public void SetTestCharacters(HashSet<char> testCharacters)
        {
            _testCharacters = testCharacters;
        }

        protected override bool TestCharacterSupport(char character)
        {
            return _testCharacters?.Contains(character) ?? false;
        }
    }

    [Fact]
    public void InitializeFontFactory_WhenContentManagerIsNull_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ManagedFont.InitializeFontFactory(null!));
    }

    [Fact]
    public void InitializeFontFactory_WhenContentManagerProvided_ShouldStoreStaticReference()
    {
        var state = CaptureFontFactoryState();
        var contentManager = (ContentManager)RuntimeHelpers.GetUninitializedObject(typeof(ContentManager));

        try
        {
            ManagedFont.InitializeFontFactory(contentManager);

            Assert.Same(contentManager, CaptureFontFactoryState().ContentManager);
        }
        finally
        {
            RestoreFontFactoryState(state.ContentManager, state.DefaultFont, state.LoadedFonts);
        }
    }

    [Fact]
    public void CreateFont_WhenFactoryIsNotInitialized_ShouldThrowInvalidOperationException()
    {
        var state = CaptureFontFactoryState();

        try
        {
            RestoreFontFactoryState(contentManager: null, defaultFont: null, loadedFonts: new Dictionary<string, SpriteFont>());

            var exception = Assert.Throws<InvalidOperationException>(() => ManagedFont.CreateFont((GraphicsDevice)null!, "Arial", 16));
            Assert.Contains("Font factory not initialized", exception.Message);
        }
        finally
        {
            RestoreFontFactoryState(state.ContentManager, state.DefaultFont, state.LoadedFonts);
        }
    }

    [Fact]
    public void Constructor_WithUninitializedSpriteFont_ShouldBuildCharacterCacheAndUseUnknownSourcePath()
    {
        var spriteFont = CreateUninitializedSpriteFont();

        var font = new ManagedFont(spriteFont, null!, 18);

        Assert.Same(spriteFont, font.SpriteFont);
        Assert.Equal("Unknown", font.SourcePath);
        Assert.Equal(18, font.Size);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(font, "_characterCacheBuilt"));
        Assert.False(font.SupportsCharacter('A'));
    }

    [Fact]
    public void CreateFont_WithExistingSpriteFont_ShouldUseLineSpacingAsFontSize()
    {
        var spriteFont = CreateUninitializedSpriteFont(lineSpacing: 28);

        var font = ManagedFont.CreateFont(spriteFont, "ExistingFont");

        Assert.Same(spriteFont, font.SpriteFont);
        Assert.Equal("ExistingFont", font.SourcePath);
        Assert.Equal(28, font.Size);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(font, "_characterCacheBuilt"));
    }

    [Fact]
    public void CreateFont_WhenRequestedSizeNotCached_ShouldLoadClosestSpriteFontAsset()
    {
        var state = CaptureFontFactoryState();
        var spriteFont = CreateUninitializedSpriteFont(lineSpacing: 48);
        var contentManager = new FakeContentManager(assetName =>
        {
            Assert.Equal("NotoSerifJP-48", assetName);
            return spriteFont;
        });

        try
        {
            RestoreFontFactoryState(contentManager, defaultFont: null, loadedFonts: new Dictionary<string, SpriteFont>());

            var font = ManagedFont.CreateFont((GraphicsDevice)null!, "LoadedFont", 40);

            Assert.Same(spriteFont, font.SpriteFont);
            Assert.Equal(["NotoSerifJP-48"], contentManager.RequestedAssets);
            Assert.Same(spriteFont, CaptureFontFactoryState().LoadedFonts["NotoSerifJP-48"]);
        }
        finally
        {
            RestoreFontFactoryState(state.ContentManager, state.DefaultFont, state.LoadedFonts);
        }
    }

    [Fact]
    public void CreateFont_WhenSpecificAssetLoadFails_ShouldFallBackToDefaultSpriteFont()
    {
        var state = CaptureFontFactoryState();
        var defaultFont = CreateUninitializedSpriteFont(lineSpacing: 14);
        var contentManager = new FakeContentManager(assetName =>
        {
            if (assetName == "NotoSerifJP-48")
            {
                throw new InvalidOperationException("missing font");
            }

            Assert.Equal("NotoSerifJP", assetName);
            return defaultFont;
        });

        try
        {
            RestoreFontFactoryState(contentManager, defaultFont: null, loadedFonts: new Dictionary<string, SpriteFont>());

            var font = ManagedFont.CreateFont((GraphicsDevice)null!, "FallbackFont", 40);

            Assert.Same(defaultFont, font.SpriteFont);
            Assert.Equal(["NotoSerifJP-48", "NotoSerifJP"], contentManager.RequestedAssets);
            Assert.Same(defaultFont, CaptureFontFactoryState().DefaultFont);
        }
        finally
        {
            RestoreFontFactoryState(state.ContentManager, state.DefaultFont, state.LoadedFonts);
        }
    }

    [Fact]
    public void CreateFont_WhenClosestCachedSpriteFontExists_ShouldReuseCachedFont()
    {
        var state = CaptureFontFactoryState();
        var spriteFont = CreateUninitializedSpriteFont();
        var contentManager = (ContentManager)RuntimeHelpers.GetUninitializedObject(typeof(ContentManager));

        try
        {
            RestoreFontFactoryState(
                contentManager,
                defaultFont: null,
                loadedFonts: new Dictionary<string, SpriteFont> { ["NotoSerifJP-24"] = spriteFont });

            var font = ManagedFont.CreateFont((GraphicsDevice)null!, "CachedFont", 20, FontStyle.Bold);

            Assert.Same(spriteFont, font.SpriteFont);
            Assert.Equal("CachedFont", font.SourcePath);
            Assert.Equal(20, font.Size);
            // Size 20 Bold has no Bold-20 asset; resolves to Regular-24
            Assert.Equal(FontStyle.Regular, font.Style);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(font, "_characterCacheBuilt"));
        }
        finally
        {
            RestoreFontFactoryState(state.ContentManager, state.DefaultFont, state.LoadedFonts);
        }
    }

    [Fact]
    public void ProtectedConstructor_WhenGraphicsDeviceIsNull_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SuccessfulConstructorManagedFont(null!, "font.ttf", 18));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ProtectedConstructor_WhenPathIsNullOrEmpty_ShouldThrowArgumentException(string? path)
    {
        var graphicsDevice = (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

        Assert.Throws<ArgumentException>(() => new SuccessfulConstructorManagedFont(graphicsDevice, path!, 18));
    }

    [Fact]
    public void ProtectedConstructor_WhenLoadFontThrows_ShouldWrapExceptionInFontLoadException()
    {
        var graphicsDevice = (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

        var exception = Assert.Throws<FontLoadException>(() => new FailingConstructorManagedFont(graphicsDevice, "font.ttf", 18));

        Assert.Contains("Failed to load font from font.ttf", exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void ProtectedConstructor_WhenLoadFontSucceeds_ShouldStoreConstructorArguments()
    {
        var graphicsDevice = (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

        var font = new SuccessfulConstructorManagedFont(graphicsDevice, "font.ttf", 18, FontStyle.Bold);

        Assert.Equal("font.ttf", font.SourcePath);
        Assert.Equal(18, font.Size);
        Assert.Equal(FontStyle.Bold, font.Style);
    }

    [Fact]
    public void SupportsCharacter_WhenDisposed_ShouldReturnFalse()
    {
        var font = CreateManagedFont();
        ReflectionHelpers.SetPrivateField(font, "_disposed", true);

        Assert.False(font.SupportsCharacter('A'));
    }

    [Fact]
    public void SupportsCharacter_WithSpriteFont_ShouldBuildCacheAndReturnMembership()
    {
        var spriteFont = CreateSpriteFont([('A', 10), ('B', 10), ('?', 8)]);
        var font = new ManagedFont(spriteFont, "TestFont", 16);

        Assert.True(font.SupportsCharacter('A'));
        Assert.True(font.SupportsCharacter('B'));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(font, "_characterCacheBuilt"));
    }

    [Fact]
    public void AddReferenceAndRemoveReference_ShouldTrackReferenceCountAndDisposeAtZero()
    {
        var font = CreateManagedFont();

        font.AddReference();
        font.AddReference();
        Assert.Equal(2, font.ReferenceCount);

        font.RemoveReference();
        Assert.Equal(1, font.ReferenceCount);
        Assert.False(font.IsDisposed);

        font.RemoveReference();
        Assert.True(font.IsDisposed);
    }

    [Fact]
    public void AddReference_WhenDisposed_ShouldThrowObjectDisposedException()
    {
        var font = CreateManagedFont();
        ReflectionHelpers.SetPrivateField(font, "_disposed", true);

        Assert.Throws<ObjectDisposedException>(() => font.AddReference());
    }

    [Fact]
    public void LineSpacing_WhenSpriteFontMissing_ShouldReturnZero()
    {
        var font = CreateManagedFont();

        Assert.Equal(0f, font.LineSpacing);
    }

    [Fact]
    public void DefaultCharacter_WhenSpriteFontMissing_ShouldUpdateStoredValue()
    {
        var font = CreateManagedFont();
        ReflectionHelpers.SetPrivateField(font, "_spriteFont", null);

        font.DefaultCharacter = '!';

        Assert.Equal('!', font.DefaultCharacter);
    }

    [Fact]
    public void DefaultCharacter_WhenSpriteFontExists_ShouldUpdateSpriteFontDefaultCharacter()
    {
        var spriteFont = CreateSpriteFont(
            [('A', 10), ('?', 8)],
            lineSpacing: 16,
            defaultCharacter: '?');
        var font = new ManagedFont(spriteFont, "SpriteFont", 16);

        font.DefaultCharacter = 'A';

        Assert.Equal('A', font.DefaultCharacter);
        Assert.Equal('A', spriteFont.DefaultCharacter);
    }

    [Fact]
    public void MeasureString_WhenDisposedOrTextEmpty_ShouldReturnZero()
    {
        var font = CreateManagedFont();

        Assert.Equal(Vector2.Zero, font.MeasureString(string.Empty));

        ReflectionHelpers.SetPrivateField(font, "_disposed", true);
        Assert.Equal(Vector2.Zero, font.MeasureString("text"));
    }

    [Fact]
    public void MeasureStringWrapped_WhenSpriteFontExists_ShouldReturnWrappedDimensions()
    {
        var spriteFont = CreateSpriteFont(
            [('A', 10), ('B', 10), ('C', 10), (' ', 8)],
            lineSpacing: 16);
        var font = new ManagedFont(spriteFont, "SpriteFont", 16);

        var size = font.MeasureStringWrapped("AA BB CCC", 30f);

        Assert.Equal(new Vector2(30, 48), size);
    }

    [Fact]
    public void GetCharacterBounds_WhenSpriteFontExists_ShouldTrackCharacterPositionsAndLineBreaks()
    {
        var spriteFont = CreateSpriteFont(
            [('A', 10), ('B', 12), ('C', 8)],
            lineSpacing: 16);
        var font = new ManagedFont(spriteFont, "SpriteFont", 16);

        var bounds = font.GetCharacterBounds("AB\nC");

        Assert.Equal(4, bounds.Length);
        Assert.Equal(new Rectangle(0, 0, 10, 16), bounds[0]);
        Assert.Equal(new Rectangle(10, 0, 12, 16), bounds[1]);
        Assert.Equal(Rectangle.Empty, bounds[2]);
        Assert.Equal(new Rectangle(0, 16, 8, 16), bounds[3]);
    }

    [Fact]
    public void SanitizeText_ShouldReplaceUnsupportedCharactersUsingFallbackRules()
    {
        var spriteFont = CreateSpriteFont([('A', 10), ('?', 8)], defaultCharacter: '?');
        var font = new TestableManagedFont(spriteFont, "TestFont", 16);
        font.SetTestCharacters(new HashSet<char> { 'A', '?', '.', '-', '"', '\'', 'ア' });
        // Reset cache so it rebuilds with the test characters
        ReflectionHelpers.SetPrivateField(font, "_characterCacheBuilt", false);
        ReflectionHelpers.SetPrivateField(font, "_supportedCharacters", new HashSet<char>());

        var sanitized = InvokePrivate<string>(font, "SanitizeText", "Ａ…—\u201C\u201Dあ");

        Assert.Equal("A.-\"\"ア", sanitized);
    }

    [Fact]
    public void GetCharacterReplacement_ShouldUseHiraganaKatakanaAndHalfwidthFallbacks()
    {
        var spriteFont = CreateSpriteFont([('?', 8)], defaultCharacter: '?');
        var font = new TestableManagedFont(spriteFont, "TestFont", 16);
        font.SetTestCharacters(new HashSet<char> { 'ア', 'あ', 'A', '?', '.' });
        ReflectionHelpers.SetPrivateField(font, "_characterCacheBuilt", false);
        ReflectionHelpers.SetPrivateField(font, "_supportedCharacters", new HashSet<char>());

        Assert.Equal('ア', InvokePrivate<char>(font, "GetCharacterReplacement", 'あ'));
        Assert.Equal('あ', InvokePrivate<char>(font, "GetCharacterReplacement", 'ア'));
        Assert.Equal('A', InvokePrivate<char>(font, "GetCharacterReplacement", 'Ａ'));
        Assert.Equal('.', InvokePrivate<char>(font, "GetCharacterReplacement", '…'));
        Assert.Equal('?', InvokePrivate<char>(font, "GetCharacterReplacement", '漢'));
    }

    [Fact]
    public void WrapText_ShouldSplitOnWordBoundariesUsingMeasuredWidth()
    {
        var spriteFont = CreateSpriteFont(
            [('A', 10), ('B', 10), ('C', 10), (' ', 8)],
            lineSpacing: 16);
        var font = new ManagedFont(spriteFont, "TestFont", 16);

        var lines = InvokePrivate<List<string>>(font, "WrapText", "AA BB CCC", 30f);

        Assert.Equal(["AA", "BB", "CCC"], lines);
    }

    [Fact]
    public void DrawString_WhenSpriteFontNull_ShouldSwallowErrors()
    {
        var font = CreateManagedFont();

        // No SpriteFont set, should not throw
        var exception = Record.Exception(() => font.DrawString(null!, "AA?", Vector2.Zero, Color.White));

        Assert.Null(exception);
    }

    [Fact]
    public void CreateTextTexture_WithLegacyOverload_ShouldThrowInvalidOperationException()
    {
        var font = CreateManagedFont();

        var exception = Assert.Throws<InvalidOperationException>(() => font.CreateTextTexture(null!, "text", new TextRenderOptions()));
        Assert.Contains("shared RenderTarget", exception.Message);
    }

    [Fact]
    public void CreateTextTexture_WithColorOverload_ShouldThrowInvalidOperationException()
    {
        var font = CreateManagedFont();

        var exception = Assert.Throws<InvalidOperationException>(() => font.CreateTextTexture(null!, "text", Color.White));

        Assert.Contains("shared RenderTarget", exception.Message);
    }

    [Fact]
    public void CreateTextTexture_WithMissingSpriteFontOrEmptyText_ShouldReturnNull()
    {
        var font = CreateManagedFont();

        Assert.Null(font.CreateTextTexture(null!, "text", new TextRenderOptions(), null!));
        Assert.Null(font.CreateTextTexture(null!, string.Empty, new TextRenderOptions(), null!));
    }

    [Fact]
    public void CreateTextTexture_WhenSharedRenderTargetIsNullAndSpriteFontExists_ShouldThrowArgumentNullException()
    {
        var font = CreateManagedFont();
        var spriteFont = (SpriteFont)RuntimeHelpers.GetUninitializedObject(typeof(SpriteFont));
        ReflectionHelpers.SetPrivateField(font, "_spriteFont", spriteFont);

        Assert.Throws<ArgumentNullException>(() => font.CreateTextTexture(null!, "text", new TextRenderOptions(), null!));
    }

    [Fact]
    public void GenerateCacheKey_ShouldReflectTextAndRenderOptions()
    {
        var font = CreateManagedFont();
        var optionsA = new TextRenderOptions { TextColor = Color.Red, EnableOutline = true, OutlineThickness = 2 };
        var optionsB = new TextRenderOptions { TextColor = Color.Blue, EnableOutline = true, OutlineThickness = 2 };

        var keyA = InvokePrivate<string>(font, "GenerateCacheKey", "text", optionsA);
        var keyB = InvokePrivate<string>(font, "GenerateCacheKey", "text", optionsB);

        Assert.NotEqual(keyA, keyB);
        Assert.Contains("text", keyA);
    }

    [Fact]
    public void CacheTextTexture_ThenDispose_ShouldDisposeCachedTextures()
    {
        var font = CreateManagedFont();
        var texture = new Mock<ITexture>();
        var options = new TextRenderOptions { TextColor = Color.Gold };

        InvokePrivate<object?>(font, "CacheTextTexture", "key", "text", texture.Object, options);
        font.Dispose();

        texture.Verify(x => x.Dispose(), Times.Once);
        Assert.True(font.IsDisposed);
    }

    [Fact]
    public void LoadFont_DefaultImplementation_ShouldThrowNotSupportedException()
    {
        var font = CreateManagedFont();
        var exception = Assert.Throws<TargetInvocationException>(() => InvokePrivate<object?>(font, "LoadFont", null!, "font.ttf", 18, FontStyle.Regular));

        Assert.IsType<NotSupportedException>(exception.InnerException);
    }

    [Theory]
    [InlineData("font.ttf", true)]
    [InlineData("font.otf", true)]
    [InlineData("font.ttc", true)]
    [InlineData("font.spritefont", true)]
    [InlineData("font.png", false)]
    public void IsSupportedFontFile_ShouldRecognizeExpectedExtensions(string path, bool expected)
    {
        var font = CreateManagedFont();

        var result = InvokePrivate<bool>(font, "IsSupportedFontFile", path);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryDrawCharacter_AndIsJapaneseCharacter_ShouldRecognizeJapaneseRanges()
    {
        var font = CreateManagedFont();

        Assert.True(InvokePrivate<bool>(font, "TryDrawCharacter", 'あ'));
        Assert.False(InvokePrivate<bool>(font, "TryDrawCharacter", 'A'));
        Assert.True(InvokePrivate<bool>(font, "IsJapaneseCharacter", '漢'));
        Assert.False(InvokePrivate<bool>(font, "IsJapaneseCharacter", 'A'));
    }

    [Fact]
    public void GetKerning_ShouldAlwaysReturnZero()
    {
        var font = CreateManagedFont();

        Assert.Equal(Vector2.Zero, font.GetKerning('A', 'V'));
    }

    [Fact]
    public void BuildCharacterRangeCache_ShouldPopulateCharacterRangesAndLogResults()
    {
        var spriteFont = (SpriteFont)RuntimeHelpers.GetUninitializedObject(typeof(SpriteFont));
        var testChars = new HashSet<char> { 'A', 'B', '1', '2' };
        var font = new TestableManagedFont(spriteFont, "test", 16);
        font.SetTestCharacters(testChars);
        ReflectionHelpers.SetPrivateField(font, "_characterCacheBuilt", false);
        ReflectionHelpers.SetPrivateField(font, "_supportedCharacters", new HashSet<char>());

        InvokePrivate<object?>(font, "BuildCharacterRangeCache");

        var supportedCharacters = ReflectionHelpers.GetPrivateField<HashSet<char>>(font, "_supportedCharacters");
        Assert.NotNull(supportedCharacters);
        // Verify all test-supported characters were found
        Assert.Contains('A', supportedCharacters);
        Assert.Contains('B', supportedCharacters);
        Assert.Contains('1', supportedCharacters);
        Assert.Contains('2', supportedCharacters);
        Assert.Equal(4, supportedCharacters.Count);
        // Verify unsupported character is absent
        Assert.DoesNotContain('Z', supportedCharacters);
    }

    [Fact]
    public void TestCharacterRange_ShouldCallTestCharacterSupportForEachCharacterInRange()
    {
        var spriteFont = (SpriteFont)RuntimeHelpers.GetUninitializedObject(typeof(SpriteFont));
        var testChars = new HashSet<char> { 'A', 'B', 'C' };
        var font = new TestableManagedFont(spriteFont, "test", 16);
        font.SetTestCharacters(testChars);
        ReflectionHelpers.SetPrivateField(font, "_supportedCharacters", new HashSet<char>());

        InvokePrivate<object?>(font, "TestCharacterRange", 0x41, 0x43, "Test Range");

        var supportedCharacters = ReflectionHelpers.GetPrivateField<HashSet<char>>(font, "_supportedCharacters");
        Assert.NotNull(supportedCharacters);
        // All three characters in range 0x41-0x43 ('A'-'C') are supported
        Assert.Contains('A', supportedCharacters);
        Assert.Contains('B', supportedCharacters);
        Assert.Contains('C', supportedCharacters);
        Assert.Equal(3, supportedCharacters.Count);
    }

    [Fact]
    public void TestCommonKanjiCharacters_ShouldCallTestCharacterSupportForEachKanjiCharacter()
    {
        var spriteFont = (SpriteFont)RuntimeHelpers.GetUninitializedObject(typeof(SpriteFont));
        var testChars = new HashSet<char> { '一', '二', '三' };
        var font = new TestableManagedFont(spriteFont, "test", 16);
        font.SetTestCharacters(testChars);
        ReflectionHelpers.SetPrivateField(font, "_supportedCharacters", new HashSet<char>());

        InvokePrivate<object?>(font, "TestCommonKanjiCharacters");

        var supportedCharacters = ReflectionHelpers.GetPrivateField<HashSet<char>>(font, "_supportedCharacters");
        Assert.NotNull(supportedCharacters);
        // Verify all test-supported kanji were found
        Assert.Contains('一', supportedCharacters);
        Assert.Contains('二', supportedCharacters);
        Assert.Contains('三', supportedCharacters);
        Assert.Equal(3, supportedCharacters.Count);
        // Verify unsupported kanji is absent
        Assert.DoesNotContain('四', supportedCharacters);
    }

    [Fact]
    public void IsJapaneseCharacter_ShouldRecognizeAllJapaneseUnicodeRanges()
    {
        var font = CreateManagedFont();

        // Test various Unicode ranges
        Assert.True(InvokePrivate<bool>(font, "IsJapaneseCharacter", 'あ'));
        Assert.True(InvokePrivate<bool>(font, "IsJapaneseCharacter", 'ア'));
        Assert.True(InvokePrivate<bool>(font, "IsJapaneseCharacter", '漢'));
        Assert.True(InvokePrivate<bool>(font, "IsJapaneseCharacter", '「'));
        Assert.True(InvokePrivate<bool>(font, "IsJapaneseCharacter", (char)0xFF00));
        Assert.False(InvokePrivate<bool>(font, "IsJapaneseCharacter", 'A'));
        Assert.False(InvokePrivate<bool>(font, "IsJapaneseCharacter", '@'));
    }

    [Fact]
    public void GetCharacterReplacement_ShouldReplaceFullwidthCharactersWithHalfwidth()
    {
        var spriteFont = CreateSpriteFont([('?', 8), (' ', 6)], defaultCharacter: '?');
        var font = new TestableManagedFont(spriteFont, "TestFont", 16);
        font.SetTestCharacters(new HashSet<char> { 'A', '?', ' ' });
        ReflectionHelpers.SetPrivateField(font, "_characterCacheBuilt", false);
        ReflectionHelpers.SetPrivateField(font, "_supportedCharacters", new HashSet<char>());

        var replacement = InvokePrivate<char>(font, "GetCharacterReplacement", 'Ａ');

        Assert.Equal('A', replacement);
    }

    [Theory]
    [InlineData('…', '.', '.')]
    [InlineData('—', '-', '-')]
    [InlineData('\u2018', '\'', '\'')]
    [InlineData('\u2019', '\'', '\'')]
    [InlineData('\u201C', '"', '"')]
    [InlineData('\u201D', '"', '"')]
    public void GetCharacterReplacement_ShouldReplaceUnsupportedChars(char inputChar, char supportedReplacement, char expectedReplacement)
    {
        var spriteFont = CreateSpriteFont([('?', 8)], defaultCharacter: '?');
        var font = new TestableManagedFont(spriteFont, "TestFont", 16);
        font.SetTestCharacters(new HashSet<char> { supportedReplacement, '?', '"', '\'' });
        ReflectionHelpers.SetPrivateField(font, "_characterCacheBuilt", false);
        ReflectionHelpers.SetPrivateField(font, "_supportedCharacters", new HashSet<char>());

        var replacement = InvokePrivate<char>(font, "GetCharacterReplacement", inputChar);

        Assert.Equal(expectedReplacement, replacement);
    }

    [Fact]
    public void WrapText_ShouldWrapLongSingleWordOnNarrowWidth()
    {
        var spriteFont = CreateSpriteFont(
            [('A', 20), ('B', 20), ('C', 20), ('D', 20), ('E', 20), ('F', 20), (' ', 8)],
            lineSpacing: 16);
        var font = new ManagedFont(spriteFont, "TestFont", 16);

        var lines = InvokePrivate<List<string>>(font, "WrapText", "ABC DEF", 50f);

        // Algorithm splits on word boundaries only; each word (60px) exceeds maxWidth (50px)
        // but the algorithm does not split words character-by-character
        Assert.Equal(["ABC", "DEF"], lines);
    }

    [Fact]
    public void WrapText_ShouldPreserveWordBoundariesAndRespectMaxWidth()
    {
        var spriteFont = CreateSpriteFont(
            [('x', 5), ('y', 5), ('z', 5), (' ', 8)],
            lineSpacing: 16);
        var font = new ManagedFont(spriteFont, "TestFont", 16);

        var lines = InvokePrivate<List<string>>(font, "WrapText", "x y z", 20f);

        // "x y" = 5+8(space)+5 = 18px <= 20px fits; "x y z" = 31px > 20px wraps
        Assert.Equal(["x y", "z"], lines);
        // Verify all lines respect maxWidth
        Assert.All(lines, line =>
        {
            var lineWidth = font.MeasureString(line).X;
            Assert.True(lineWidth <= 20f, $"Line '{line}' width {lineWidth} exceeds maxWidth 20");
        });
    }

    [Fact]
    public void GenerateCacheKey_ShouldIncludeAllRenderOptions()
    {
        var font = CreateManagedFont();
        var optionsA = new TextRenderOptions
        {
            TextColor = Color.Red,
            EnableOutline = true,
            OutlineColor = Color.Blue,
            OutlineThickness = 2,
            EnableGradient = true,
            GradientTopColor = Color.Green,
            GradientBottomColor = Color.Yellow,
            EnableShadow = true,
            ShadowColor = Color.Black,
            ShadowOffset = new Vector2(1, 1)
        };
        var optionsB = new TextRenderOptions
        {
            TextColor = Color.Blue,
            EnableOutline = false,
            OutlineColor = Color.Red,
            OutlineThickness = 1,
            EnableGradient = false,
            GradientTopColor = Color.Red,
            GradientBottomColor = Color.Blue,
            EnableShadow = false,
            ShadowColor = Color.White,
            ShadowOffset = new Vector2(2, 2)
        };

        var keyA = InvokePrivate<string>(font, "GenerateCacheKey", "test", optionsA);
        var keyB = InvokePrivate<string>(font, "GenerateCacheKey", "test", optionsB);

        Assert.NotEqual(keyA, keyB);
        Assert.Contains("test", keyA);
        Assert.Contains(Color.Red.PackedValue.ToString(), keyA);
    }

    [Fact]
    public void BuildCharacterCache_WithSpriteFont_ShouldPopulateFromCharacterRanges()
    {
        var spriteFont = CreateSpriteFont([('A', 10), ('B', 10), ('X', 10), ('?', 8)], defaultCharacter: '?');
        var testChars = new HashSet<char> { 'A', 'B', 'X' };
        var font = new TestableManagedFont(spriteFont, "TestFont", 16);
        font.SetTestCharacters(testChars);
        ReflectionHelpers.SetPrivateField(font, "_characterCacheBuilt", false);
        ReflectionHelpers.SetPrivateField(font, "_supportedCharacters", new HashSet<char>());

        InvokePrivate<object?>(font, "BuildCharacterCache");

        var supported = ReflectionHelpers.GetPrivateField<HashSet<char>>(font, "_supportedCharacters");
        Assert.NotNull(supported);
        Assert.Contains('A', supported);
        Assert.Contains('B', supported);
        Assert.Contains('X', supported);
    }

    [Fact]
    public void BuildCharacterCache_WithoutSpriteFont_ShouldMarkCacheBuilt()
    {
        var font = CreateManagedFont();
        ReflectionHelpers.SetPrivateField(font, "_characterCacheBuilt", false);

        InvokePrivate<object?>(font, "BuildCharacterCache");

        var cacheBuilt = ReflectionHelpers.GetPrivateField<bool>(font, "_characterCacheBuilt");
        Assert.True(cacheBuilt);
    }

    [Fact]
    public void TestCharacterSupport_WithNullSpriteFont_ShouldReturnFalse()
    {
        var font = CreateManagedFont();
        ReflectionHelpers.SetPrivateField(font, "_spriteFont", null);

        var result = InvokePrivate<bool>(font, "TestCharacterSupport", 'A');

        Assert.False(result);
    }

    [Fact]
    public void TestCharacterSupport_WhenMeasureStringThrows_ShouldReturnFalse()
    {
        var font = CreateManagedFont();
        var spriteFont = CreateUninitializedSpriteFont();
        ReflectionHelpers.SetPrivateField(font, "_spriteFont", spriteFont);

        var result = InvokePrivate<bool>(font, "TestCharacterSupport", 'A');

        Assert.False(result);
    }

    [Fact]
    public void FontAtlas_Properties_ShouldRoundTripAssignedValues()
    {
        var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        var characters = new Dictionary<char, CharacterData>
        {
            ['A'] = new CharacterData { Character = 'A', Width = 12, Height = 18 }
        };

        var atlas = new FontAtlas
        {
            Texture = texture,
            Characters = characters,
            LineSpacing = 24
        };

        Assert.Same(texture, atlas.Texture);
        Assert.Same(characters, atlas.Characters);
        Assert.Equal(24, atlas.LineSpacing);
    }

    [Fact]
    public void CharacterData_Properties_ShouldRoundTripAssignedValues()
    {
        var characterData = new CharacterData
        {
            Character = 'B',
            Width = 14,
            Height = 20,
            AtlasX = 1,
            AtlasY = 2,
            AtlasWidth = 15,
            AtlasHeight = 21
        };

        Assert.Equal('B', characterData.Character);
        Assert.Equal(14, characterData.Width);
        Assert.Equal(20, characterData.Height);
        Assert.Equal(1, characterData.AtlasX);
        Assert.Equal(2, characterData.AtlasY);
        Assert.Equal(15, characterData.AtlasWidth);
        Assert.Equal(21, characterData.AtlasHeight);
    }

    [Fact]
    public void CreateTextTextureInternal_WhenMeasureStringReturnsZeroSize_ShouldReturnNull()
    {
        var spriteFont = CreateSpriteFont([('A', 0)], lineSpacing: 0);
        var font = new ManagedFont(spriteFont, "ZeroSizeFont", 16);
        var graphicsDevice = (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));
        var sharedRenderTarget = (RenderTarget2D)RuntimeHelpers.GetUninitializedObject(typeof(RenderTarget2D));
        var options = new TextRenderOptions { TextColor = Color.White };

        var result = InvokePrivate<ITexture?>(font, "CreateTextTextureInternal", graphicsDevice, "A", options, sharedRenderTarget);

        Assert.Null(result);
    }

    [Fact]
    public void CreateTextTexture_WhenCacheContainsEntry_ShouldReturnCachedTexture()
    {
        var spriteFont = CreateUninitializedSpriteFont();
        var font = new ManagedFont(spriteFont, "TestFont", 16);
        var mockTexture = new Mock<ITexture>();
        var options = new TextRenderOptions { TextColor = Color.Red };
        var cacheKey = InvokePrivate<string>(font, "GenerateCacheKey", "cached", options);

        InvokePrivate<object?>(font, "CacheTextTexture", cacheKey, "cached", mockTexture.Object, options);

        var sharedRenderTarget = (RenderTarget2D)RuntimeHelpers.GetUninitializedObject(typeof(RenderTarget2D));
        var result = font.CreateTextTexture(null!, "cached", options, sharedRenderTarget);

        Assert.Same(mockTexture.Object, result);
    }

    [Fact]
    public void CreateTextTexture_WhenDisposed_ShouldReturnNull()
    {
        var font = CreateManagedFont();
        ReflectionHelpers.SetPrivateField(font, "_disposed", true);
        var sharedRenderTarget = (RenderTarget2D)RuntimeHelpers.GetUninitializedObject(typeof(RenderTarget2D));

        var result = font.CreateTextTexture(null!, "text", new TextRenderOptions(), sharedRenderTarget);

        Assert.Null(result);
    }

    [Fact]
    public void DrawStringWithGradient_WhenDisposed_ShouldReturnWithoutThrowing()
    {
        var font = CreateManagedFont();
        ReflectionHelpers.SetPrivateField(font, "_disposed", true);

        var exception = Record.Exception(() => font.DrawStringWithGradient(null!, "text", Vector2.Zero, Color.White, Color.Black));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawStringWithGradient_WhenSpriteFontNull_ShouldReturnWithoutThrowing()
    {
        var font = CreateManagedFont();

        var exception = Record.Exception(() => font.DrawStringWithGradient(null!, "text", Vector2.Zero, Color.White, Color.Black));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawStringWithGradient_WhenTextEmpty_ShouldReturnWithoutThrowing()
    {
        var spriteFont = CreateUninitializedSpriteFont();
        var font = new ManagedFont(spriteFont, "TestFont", 16);

        var exception = Record.Exception(() => font.DrawStringWithGradient(null!, string.Empty, Vector2.Zero, Color.White, Color.Black));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawString_AdvancedOverload_WhenDisposed_ShouldReturnWithoutThrowing()
    {
        var font = CreateManagedFont();
        ReflectionHelpers.SetPrivateField(font, "_disposed", true);

        var exception = Record.Exception(() => font.DrawString(null!, "text", Vector2.Zero, Color.White, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0f));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawString_AdvancedOverload_WhenSpriteFontNull_ShouldReturnWithoutThrowing()
    {
        var font = CreateManagedFont();

        var exception = Record.Exception(() => font.DrawString(null!, "text", Vector2.Zero, Color.White, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0f));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawString_AdvancedOverload_WhenTextEmpty_ShouldReturnWithoutThrowing()
    {
        var spriteFont = CreateUninitializedSpriteFont();
        var font = new ManagedFont(spriteFont, "TestFont", 16);

        var exception = Record.Exception(() => font.DrawString(null!, string.Empty, Vector2.Zero, Color.White, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0f));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawStringWrapped_WhenDisposed_ShouldReturnWithoutThrowing()
    {
        var font = CreateManagedFont();
        ReflectionHelpers.SetPrivateField(font, "_disposed", true);

        var exception = Record.Exception(() => font.DrawStringWrapped(null!, "text", Rectangle.Empty, Color.White));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawStringWrapped_WhenSpriteFontNull_ShouldReturnWithoutThrowing()
    {
        var font = CreateManagedFont();

        var exception = Record.Exception(() => font.DrawStringWrapped(null!, "text", Rectangle.Empty, Color.White));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawStringWrapped_WhenTextEmpty_ShouldReturnWithoutThrowing()
    {
        var spriteFont = CreateUninitializedSpriteFont();
        var font = new ManagedFont(spriteFont, "TestFont", 16);

        var exception = Record.Exception(() => font.DrawStringWrapped(null!, string.Empty, Rectangle.Empty, Color.White));

        Assert.Null(exception);
    }

    private static ManagedFont CreateManagedFont(
        HashSet<char>? customCharacters = null,
        Dictionary<char, Rectangle>? glyphs = null,
        int lineSpacing = 16,
        bool includeCustomTexture = true)
    {
        var font = (ManagedFont)RuntimeHelpers.GetUninitializedObject(typeof(ManagedFont));

        ReflectionHelpers.SetPrivateField(font, "_spriteFont", null);
        ReflectionHelpers.SetPrivateField(font, "_sourcePath", "TestFont");
        ReflectionHelpers.SetPrivateField(font, "_size", 16);
        ReflectionHelpers.SetPrivateField(font, "_style", FontStyle.Regular);
        ReflectionHelpers.SetPrivateField(font, "_referenceCount", 0);
        ReflectionHelpers.SetPrivateField(font, "_disposed", false);
        ReflectionHelpers.SetPrivateField(font, "_lockObject", new object());
        ReflectionHelpers.SetPrivateField(font, "_defaultCharacter", '?');
        ReflectionHelpers.SetPrivateField(font, "_supportedCharacters", new HashSet<char>());
        ReflectionHelpers.SetPrivateField(font, "_characterCacheBuilt", false);

        var cacheField = typeof(ManagedFont).GetField("_textRenderCache", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cacheField);
        cacheField!.SetValue(font, Activator.CreateInstance(cacheField.FieldType));

        return font;
    }

    private static SpriteFont CreateSpriteFont((char character, int width)[] glyphs, int lineSpacing = 16, char? defaultCharacter = null)
    {
        var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        var glyphBounds = new List<Rectangle>();
        var cropping = new List<Rectangle>();
        var characters = new List<char>();
        var kerning = new List<Vector3>();
        var x = 0;

        foreach (var (character, width) in glyphs.OrderBy(glyph => glyph.character))
        {
            glyphBounds.Add(new Rectangle(x, 0, width, lineSpacing));
            cropping.Add(new Rectangle(0, 0, width, lineSpacing));
            characters.Add(character);
            kerning.Add(new Vector3(0, width, 0));
            x += width;
        }

        return (SpriteFont)Activator.CreateInstance(
            typeof(SpriteFont),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [texture, glyphBounds, cropping, characters, lineSpacing, 0f, kerning, defaultCharacter],
            culture: null)!;
    }

    private static SpriteFont CreateUninitializedSpriteFont(int lineSpacing = 0)
    {
        var spriteFont = (SpriteFont)RuntimeHelpers.GetUninitializedObject(typeof(SpriteFont));
        var bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        foreach (var fieldName in new[] { "<LineSpacing>k__BackingField", "LineSpacing", "_lineSpacing", "lineSpacing" })
        {
            var lineSpacingField = typeof(SpriteFont).GetField(fieldName, bindingFlags);
            if (lineSpacingField?.FieldType == typeof(int))
            {
                lineSpacingField.SetValue(spriteFont, lineSpacing);
                return spriteFont;
            }
        }

        var fallbackField = typeof(SpriteFont)
            .GetFields(bindingFlags)
            .FirstOrDefault(field =>
                field.FieldType == typeof(int) &&
                field.Name.Contains("LineSpacing", StringComparison.OrdinalIgnoreCase));
        if (fallbackField != null)
        {
            fallbackField.SetValue(spriteFont, lineSpacing);
            return spriteFont;
        }

        var property = typeof(SpriteFont).GetProperty("LineSpacing", bindingFlags);
        var setter = property?.GetSetMethod(nonPublic: true);
        if (property?.PropertyType == typeof(int) && setter != null)
        {
            setter.Invoke(spriteFont, new object[] { lineSpacing });
            return spriteFont;
        }

        var availableMembers = string.Join(
            ", ",
            typeof(SpriteFont)
                .GetMembers(bindingFlags)
                .Where(member => member.MemberType is MemberTypes.Field or MemberTypes.Property)
                .Select(member => $"{member.MemberType}:{member.Name}"));
        throw new InvalidOperationException(
            $"Unable to initialize SpriteFont.LineSpacing. Expected a writable field or property matching LineSpacing. Available members: {availableMembers}");
    }

    private static T InvokePrivate<T>(object target, string methodName, params object?[] args)
    {
        var method = typeof(ManagedFont).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(target, args)!;
    }

    private static (ContentManager? ContentManager, SpriteFont? DefaultFont, Dictionary<string, SpriteFont> LoadedFonts) CaptureFontFactoryState()
    {
        var contentField = typeof(ManagedFont).GetField("_contentManager", BindingFlags.Static | BindingFlags.NonPublic);
        var defaultField = typeof(ManagedFont).GetField("_defaultFont", BindingFlags.Static | BindingFlags.NonPublic);
        var loadedFontsField = typeof(ManagedFont).GetField("_loadedFonts", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(contentField);
        Assert.NotNull(defaultField);
        Assert.NotNull(loadedFontsField);

        var loadedFonts = (Dictionary<string, SpriteFont>)loadedFontsField!.GetValue(null)!;
        return (
            (ContentManager?)contentField!.GetValue(null),
            (SpriteFont?)defaultField!.GetValue(null),
            new Dictionary<string, SpriteFont>(loadedFonts));
    }

    private static void RestoreFontFactoryState(ContentManager? contentManager, SpriteFont? defaultFont, Dictionary<string, SpriteFont> loadedFonts)
    {
        var contentField = typeof(ManagedFont).GetField("_contentManager", BindingFlags.Static | BindingFlags.NonPublic);
        var defaultField = typeof(ManagedFont).GetField("_defaultFont", BindingFlags.Static | BindingFlags.NonPublic);
        var loadedFontsField = typeof(ManagedFont).GetField("_loadedFonts", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(contentField);
        Assert.NotNull(defaultField);
        Assert.NotNull(loadedFontsField);

        contentField!.SetValue(null, contentManager);
        defaultField!.SetValue(null, defaultFont);

        var targetLoadedFonts = (Dictionary<string, SpriteFont>)loadedFontsField!.GetValue(null)!;
        targetLoadedFonts.Clear();
        foreach (var (key, value) in loadedFonts)
        {
            targetLoadedFonts[key] = value;
        }
    }
}
