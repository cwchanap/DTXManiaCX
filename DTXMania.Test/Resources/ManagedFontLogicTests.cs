using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace DTXMania.Test.Resources;

[Trait("Category", "Unit")]
public class ManagedFontLogicTests
{
    [Fact]
    public void InitializeFontFactory_WhenContentManagerIsNull_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ManagedFont.InitializeFontFactory(null!));
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
    public void CreateFont_WhenClosestCachedSpriteFontExists_ShouldReuseCachedFont()
    {
        var state = CaptureFontFactoryState();
        var spriteFont = CreateUninitializedSpriteFont();
#pragma warning disable SYSLIB0050
        var contentManager = (ContentManager)FormatterServices.GetUninitializedObject(typeof(ContentManager));
#pragma warning restore SYSLIB0050

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
#pragma warning disable SYSLIB0050
        var spriteFont = (SpriteFont)FormatterServices.GetUninitializedObject(typeof(SpriteFont));
#pragma warning restore SYSLIB0050
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

    private static ManagedFont CreateManagedFont(
        HashSet<char>? customCharacters = null,
        Dictionary<char, Rectangle>? glyphs = null,
        int lineSpacing = 16,
        bool includeCustomTexture = true)
    {
#pragma warning disable SYSLIB0050
        var font = (ManagedFont)FormatterServices.GetUninitializedObject(typeof(ManagedFont));
        var customTexture = includeCustomTexture
            ? (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D))
            : null;
#pragma warning restore SYSLIB0050

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
#pragma warning disable SYSLIB0050
        var spriteFont = (SpriteFont)FormatterServices.GetUninitializedObject(typeof(SpriteFont));
#pragma warning restore SYSLIB0050

        var lineSpacingField = typeof(SpriteFont).GetField("<LineSpacing>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(lineSpacingField);
        lineSpacingField!.SetValue(spriteFont, lineSpacing);

        return spriteFont;
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
