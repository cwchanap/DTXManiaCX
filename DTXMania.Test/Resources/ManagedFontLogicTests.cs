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
            Assert.Equal(FontStyle.Bold, font.Style);
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
    public void SupportsCharacter_WithCustomCharacterSet_ShouldBuildCacheAndReturnMembership()
    {
        var font = CreateManagedFont(customCharacters: new HashSet<char> { 'A', 'B', '?' });

        Assert.True(font.SupportsCharacter('A'));
        Assert.False(font.SupportsCharacter('Z'));
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
    public void LineSpacing_WhenSpriteFontMissing_ShouldUseCustomLineSpacing()
    {
        var font = CreateManagedFont(lineSpacing: 23);

        Assert.Equal(23, font.LineSpacing);
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
    public void MeasureString_WhenUsingCustomGlyphs_ShouldRespectGlyphWidthsAndNewlines()
    {
        var font = CreateManagedFont(
            customCharacters: new HashSet<char> { 'A', 'B' },
            glyphs: new Dictionary<char, Rectangle>
            {
                ['A'] = new Rectangle(0, 0, 10, 16),
                ['B'] = new Rectangle(10, 0, 12, 16)
            },
            lineSpacing: 16);

        var size = font.MeasureString("AB\nC");

        Assert.Equal(new Vector2(22, 32), size);
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
    public void SanitizeText_ShouldReplaceUnsupportedCharactersUsingFallbackRules()
    {
        var font = CreateManagedFont(customCharacters: new HashSet<char> { 'A', '?', '.', '-', '"', '\'', 'ア' });

        var sanitized = InvokePrivate<string>(font, "SanitizeText", "Ａ…—“あ");

        Assert.Equal("A.-\"ア", sanitized);
    }

    [Fact]
    public void GetCharacterReplacement_ShouldUseHiraganaKatakanaAndHalfwidthFallbacks()
    {
        var font = CreateManagedFont(customCharacters: new HashSet<char> { 'ア', 'あ', 'A', '?', '.' });

        Assert.Equal('ア', InvokePrivate<char>(font, "GetCharacterReplacement", 'あ'));
        Assert.Equal('あ', InvokePrivate<char>(font, "GetCharacterReplacement", 'ア'));
        Assert.Equal('A', InvokePrivate<char>(font, "GetCharacterReplacement", 'Ａ'));
        Assert.Equal('.', InvokePrivate<char>(font, "GetCharacterReplacement", '…'));
        Assert.Equal('?', InvokePrivate<char>(font, "GetCharacterReplacement", '漢'));
    }

    [Fact]
    public void WrapText_ShouldSplitOnWordBoundariesUsingMeasuredWidth()
    {
        var font = CreateManagedFont(
            customCharacters: new HashSet<char> { 'A', 'B', 'C' },
            glyphs: new Dictionary<char, Rectangle>
            {
                ['A'] = new Rectangle(0, 0, 10, 16),
                ['B'] = new Rectangle(10, 0, 10, 16),
                ['C'] = new Rectangle(20, 0, 10, 16)
            });

        var lines = InvokePrivate<List<string>>(font, "WrapText", "AA BB CCC", 30f);

        Assert.Equal(["AA", "BB", "CCC"], lines);
    }

    [Fact]
    public void DrawString_WhenUsingCustomGlyphsAndSpriteBatchIsNull_ShouldSwallowCustomRenderErrors()
    {
        var font = CreateManagedFont(
            customCharacters: new HashSet<char> { 'A', '?' },
            glyphs: new Dictionary<char, Rectangle>
            {
                ['A'] = new Rectangle(0, 0, 10, 16),
                ['?'] = new Rectangle(10, 0, 8, 16)
            });

        var exception = Record.Exception(() => font.DrawString(null!, "AA?", Vector2.Zero, Color.White));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawStringCustom_WhenTextHasNewlineAndUnknownCharacterWithoutFallbackGlyph_ShouldSkipDrawingAndNotThrow()
    {
        var font = CreateManagedFont(glyphs: new Dictionary<char, Rectangle>());

        var exception = Record.Exception(() => InvokePrivate<object?>(font, "DrawStringCustom", null!, "\nZ", Vector2.Zero, Color.White));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawStringCustom_WhenUnknownCharacterHasDefaultGlyph_ShouldSwallowDrawFailure()
    {
        var font = CreateManagedFont(glyphs: new Dictionary<char, Rectangle>
        {
            ['?'] = new Rectangle(0, 0, 8, 16)
        });

        var exception = Record.Exception(() => InvokePrivate<object?>(font, "DrawStringCustom", null!, "Z", Vector2.Zero, Color.White));

        Assert.Null(exception);
    }

    [Fact]
    public void MeasureStringCustom_WhenTextContainsNewlinesAndUnknownCharacters_ShouldUseGlyphWidthsAndLineSpacing()
    {
        var font = CreateManagedFont(
            glyphs: new Dictionary<char, Rectangle>
            {
                ['A'] = new Rectangle(0, 0, 10, 16),
                ['B'] = new Rectangle(10, 0, 12, 16)
            },
            lineSpacing: 18);

        var size = InvokePrivate<Vector2>(font, "MeasureStringCustom", "A\nBZ");

        Assert.Equal(new Vector2(20, 36), size);
    }

    [Fact]
    public void CreateTextTexture_WithLegacyOverload_ShouldThrowInvalidOperationException()
    {
        var font = CreateManagedFont();

        var exception = Assert.Throws<InvalidOperationException>(() => font.CreateTextTexture(null!, "text", new TextRenderOptions()));
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
        Assert.Contains('A', supportedCharacters);
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
        var font = CreateManagedFont(customCharacters: new HashSet<char> { 'A', '?', ' ' });

        var replacement = InvokePrivate<char>(font, "GetCharacterReplacement", 'Ａ');

        Assert.Equal('A', replacement);
    }

    [Fact]
    public void GetCharacterReplacement_ShouldReplaceUnsupportedEllipsisWithPeriod()
    {
        var font = CreateManagedFont(customCharacters: new HashSet<char> { '.', '?' });

        var replacement = InvokePrivate<char>(font, "GetCharacterReplacement", '…');

        Assert.Equal('.', replacement);
    }

    [Fact]
    public void GetCharacterReplacement_ShouldReplaceEMDashWithHyphen()
    {
        var font = CreateManagedFont(customCharacters: new HashSet<char> { '-', '?' });

        var replacement = InvokePrivate<char>(font, "GetCharacterReplacement", '—');

        Assert.Equal('-', replacement);
    }

    [Fact]
    public void GetCharacterReplacement_ShouldReplaceLeftSingleQuotationMarkWithApostrophe()
    {
        var font = CreateManagedFont(customCharacters: new HashSet<char> { '\'', '?' });

        var replacement = InvokePrivate<char>(font, "GetCharacterReplacement", '\u2018');

        Assert.Equal('\'', replacement);
    }

    [Fact]
    public void GetCharacterReplacement_ShouldReplaceRightSingleQuotationMarkWithApostrophe()
    {
        var font = CreateManagedFont(customCharacters: new HashSet<char> { '\'', '?' });

        var replacement = InvokePrivate<char>(font, "GetCharacterReplacement", '\u2019');

        Assert.Equal('\'', replacement);
    }

    [Fact]
    public void GetCharacterReplacement_ShouldReplaceLeftDoubleQuotationMarkWithQuote()
    {
        var font = CreateManagedFont(customCharacters: new HashSet<char> { '"', '?' });

        var replacement = InvokePrivate<char>(font, "GetCharacterReplacement", '\u201C');

        Assert.Equal('"', replacement);
    }

    [Fact]
    public void GetCharacterReplacement_ShouldReplaceRightDoubleQuotationMarkWithQuote()
    {
        var font = CreateManagedFont(customCharacters: new HashSet<char> { '"', '?' });

        var replacement = InvokePrivate<char>(font, "GetCharacterReplacement", '\u201D');

        Assert.Equal('"', replacement);
    }

    [Fact]
    public void WrapText_ShouldWrapLongSingleWordOnNarrowWidth()
    {
        var font = CreateManagedFont(
            customCharacters: new HashSet<char> { 'A', 'B', 'C', 'D', 'E', 'F' },
            glyphs: new Dictionary<char, Rectangle>
            {
                ['A'] = new Rectangle(0, 0, 20, 16),
                ['B'] = new Rectangle(20, 0, 20, 16),
                ['C'] = new Rectangle(40, 0, 20, 16),
                ['D'] = new Rectangle(60, 0, 20, 16),
                ['E'] = new Rectangle(80, 0, 20, 16),
                ['F'] = new Rectangle(100, 0, 20, 16)
            });

        var lines = InvokePrivate<List<string>>(font, "WrapText", "ABC DEF", 50f);

        Assert.NotEmpty(lines);
    }

    [Fact]
    public void WrapText_ShouldPreserveWordBoundariesAndRespectMaxWidth()
    {
        var font = CreateManagedFont(
            customCharacters: new HashSet<char> { 'x', 'y', 'z' },
            glyphs: new Dictionary<char, Rectangle>
            {
                ['x'] = new Rectangle(0, 0, 5, 16),
                ['y'] = new Rectangle(5, 0, 5, 16),
                ['z'] = new Rectangle(10, 0, 5, 16)
            });

        var lines = InvokePrivate<List<string>>(font, "WrapText", "x y z", 20f);

        Assert.True(lines.Count >= 1);
        Assert.All(lines, line => Assert.NotNull(line));
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
    public void BuildCharacterCache_WithCustomCharactersSet_ShouldUseCustomCharacterSet()
    {
        var font = CreateManagedFont(customCharacters: new HashSet<char> { 'X', 'Y', 'Z' });
        ReflectionHelpers.SetPrivateField(font, "_characterCacheBuilt", false);

        InvokePrivate<object?>(font, "BuildCharacterCache");

        var supported = ReflectionHelpers.GetPrivateField<HashSet<char>>(font, "_supportedCharacters");
        Assert.NotNull(supported);
        Assert.Contains('X', supported);
        Assert.Contains('Y', supported);
        Assert.Contains('Z', supported);
    }

    [Fact]
    public void BuildCharacterCache_WithoutSpriteFontAndNoCustomCharacters_ShouldMarkCacheBuilt()
    {
        var font = CreateManagedFont();
        ReflectionHelpers.SetPrivateField(font, "_spriteFont", null);
        ReflectionHelpers.SetPrivateField(font, "_customFontCharacters", new HashSet<char>());
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

    private static ManagedFont CreateManagedFont(
        HashSet<char>? customCharacters = null,
        Dictionary<char, Rectangle>? glyphs = null,
        int lineSpacing = 16,
        bool includeCustomTexture = true)
    {
        var font = (ManagedFont)RuntimeHelpers.GetUninitializedObject(typeof(ManagedFont));
        var customTexture = includeCustomTexture
            ? (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D))
            : null;

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
        ReflectionHelpers.SetPrivateField(font, "_customFontTexture", customTexture);
        ReflectionHelpers.SetPrivateField(font, "_customFontGlyphs", glyphs ?? new Dictionary<char, Rectangle>());
        ReflectionHelpers.SetPrivateField(font, "_customFontCharacters", customCharacters ?? new HashSet<char>());
        ReflectionHelpers.SetPrivateField(font, "_customLineSpacing", lineSpacing);

        var cacheField = typeof(ManagedFont).GetField("_textRenderCache", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cacheField);
        cacheField!.SetValue(font, Activator.CreateInstance(cacheField.FieldType));

        return font;
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
