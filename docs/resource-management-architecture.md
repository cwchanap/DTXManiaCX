# DTXManiaCX Resource Management Architecture

## Overview

This document describes the resource management architecture implemented for DTXManiaCX, based on patterns discovered in the original DTXMania codebase. The system provides centralized loading, caching, and disposal of game resources with reference counting and skin support.

## Architecture Components

### Core Interfaces

#### IResourceManager
Central resource management interface providing:
- **Texture Loading**: `LoadTexture(path)` with transparency support
- **Font Loading**: `LoadFont(path, size, style)` with Japanese character support
- **Path Resolution**: `ResolvePath(relativePath)` using skin system
- **Resource Management**: `UnloadAll()`, `CollectUnusedResources()`
- **Skin Support**: `SetSkinPath(skinPath)` for theme switching

#### ITexture
Texture abstraction wrapping MonoGame Texture2D:
- **Reference Counting**: `AddReference()`, `RemoveReference()`
- **DTXMania Properties**: `Transparency`, `ScaleRatio`, `ZAxisRotation`, `AdditiveBlending`
- **Drawing Methods**: Multiple `Draw()` overloads with transformation support
- **Utility Methods**: `Clone()`, `GetColorData()`, `SaveToFile()`

#### IFont
Font abstraction wrapping MonoGame SpriteFont:
- **Character Support**: `SupportsCharacter(char)` for Japanese text
- **Text Measurement**: `MeasureString()`, `MeasureStringWrapped()`
- **Advanced Rendering**: Outline, gradient, shadow effects
- **Text Textures**: `CreateTextTexture()` for cached text rendering

### Implementation Classes

#### ResourceManager
Main implementation of `IResourceManager`:
- **Caching**: Thread-safe concurrent dictionaries for textures and fonts
- **Path Resolution**: DTXMania-style skin path resolution
- **Validation**: File existence checks with fallback support
- **Statistics**: Cache hit/miss tracking and memory usage monitoring
- **Events**: `ResourceLoadFailed`, `SkinChanged` notifications

#### ManagedTexture
Implementation of `ITexture`:
- **Reference Counting**: Automatic disposal when references reach zero
- **Leak Detection**: Finalizer-based disposal leak warnings
- **Transparency**: Color key transparency support
- **Memory Tracking**: Estimated memory usage calculation
- **DTXMania Compatibility**: Properties matching original CTexture patterns

#### ManagedFont
Implementation of `IFont`:
- **Character Cache**: Performance-optimized character support checking with Japanese character ranges
- **Text Sanitization**: Automatic replacement of unsupported characters with intelligent fallbacks
- **Advanced Effects**: Outline, gradient, shadow rendering with DTXMania-style effects
- **Word Wrapping**: Intelligent text wrapping with measurement
- **Text Textures**: Render-to-texture for complex text effects with LRU caching
- **Font Loading**: Support for TTF/OTF files, SpriteFont content pipeline, and system fonts
- **Japanese Support**: Full Unicode support for Hiragana, Katakana, and common Kanji characters
- **Performance Caching**: Text render caching similar to CPrivateFastFont for optimal performance

## Key Features

### 1. Reference Counting
```csharp
// Automatic resource management
var texture = resourceManager.LoadTexture("background.jpg");
texture.AddReference(); // Increment reference count
// ... use texture ...
texture.RemoveReference(); // Decrements and disposes when count reaches 0
```

### 2. Skin System Integration
```csharp
// DTXMania-style skin switching
// Default skin uses System/ directly
resourceManager.SetSkinPath("System/");
var texture = resourceManager.LoadTexture("Graphics/background.jpg");
// Resolves to: System/Graphics/background.jpg

// Custom skin uses System/{SkinName}/
resourceManager.SetSkinPath("System/MyCustomSkin/");
var texture2 = resourceManager.LoadTexture("Graphics/background.jpg");
// Resolves to: System/MyCustomSkin/Graphics/background.jpg
```

### 3. Fallback Resource Loading
```csharp
// Automatic fallback to default skin if resource not found
var texture = resourceManager.LoadTexture("Graphics/missing.jpg");
// Falls back to System/Graphics/missing.jpg if not found in current skin
```

### 4. Advanced Text Rendering
```csharp
// DTXMania-style text effects
font.DrawStringWithOutline(spriteBatch, "Score: 1000", position, Color.White, Color.Black, 2);
font.DrawStringWithGradient(spriteBatch, "PERFECT!", position, Color.Yellow, Color.Orange);
```

### 5. Memory Management
```csharp
// Resource usage monitoring
var usage = resourceManager.GetUsageInfo();
Console.WriteLine($"Loaded: {usage.LoadedTextures} textures, {usage.LoadedFonts} fonts");
Console.WriteLine($"Memory: {usage.TotalMemoryUsage / 1024 / 1024} MB");

// Cleanup unused resources
resourceManager.CollectUnusedResources();
```

## Usage Examples

### Basic Resource Loading
```csharp
// Initialize resource manager
var resourceManager = new ResourceManager(graphicsDevice);
resourceManager.SetSkinPath("System/"); // Default skin

// Load resources
var backgroundTexture = resourceManager.LoadTexture("Graphics/background.jpg");
var titleFont = resourceManager.LoadFont("Fonts/Title.spritefont", 48, FontStyle.Bold);

// Use resources
backgroundTexture.Draw(spriteBatch, Vector2.Zero);
titleFont.DrawString(spriteBatch, "DTXManiaCX", new Vector2(100, 50), Color.White);

// Resources are automatically disposed when references reach zero
```

### Advanced Texture Usage
```csharp
var texture = resourceManager.LoadTexture("Graphics/note.png", enableTransparency: true);

// DTXMania-style properties
texture.Transparency = 200; // Semi-transparent
texture.ScaleRatio = new Vector3(1.5f, 1.5f, 1.0f); // 150% scale
texture.ZAxisRotation = MathHelper.ToRadians(45); // 45-degree rotation

// Draw with transformations
texture.Draw(spriteBatch, position, Vector2.One, 0f, Vector2.Zero);
```

### Font Effects
```csharp
var font = resourceManager.LoadFont("Arial", 24, FontStyle.Bold);

// Check Japanese character support
if (font.SupportsCharacter('あ'))
{
    font.DrawString(spriteBatch, "こんにちは", position, Color.White);
}

// Create text texture for complex effects
var textOptions = new TextRenderOptions
{
    TextColor = Color.White,
    EnableOutline = true,
    OutlineColor = Color.Black,
    OutlineThickness = 2
};
var textTexture = font.CreateTextTexture(graphicsDevice, "High Score!", textOptions);
```

### Japanese Character Support
```csharp
// Load a font with Japanese character support
var japaneseFont = resourceManager.LoadFont("NotoSansCJK", 16, FontStyle.Regular);

// Test various Japanese character ranges
var hiragana = "ひらがな";
var katakana = "カタカナ";
var kanji = "漢字";
var mixed = "こんにちは世界！Hello World!";

// Check character support before rendering
foreach (char c in mixed)
{
    if (!japaneseFont.SupportsCharacter(c))
    {
        Debug.WriteLine($"Character '{c}' (U+{(int)c:X4}) not supported");
    }
}

// Render Japanese text with effects
var japaneseOptions = new TextRenderOptions
{
    TextColor = Color.White,
    EnableOutline = true,
    OutlineColor = Color.DarkBlue,
    OutlineThickness = 1
};

japaneseFont.DrawStringWithOutline(spriteBatch, mixed, position,
    Color.White, Color.DarkBlue, 1);

// Create cached text texture for performance
var cachedTexture = japaneseFont.CreateTextTexture(graphicsDevice, mixed, japaneseOptions);
```

### Character Replacement and Fallbacks
```csharp
// The font system automatically handles character replacements
var textWithSpecialChars = "Hello "World" — こんにちは…";

// Unsupported characters are automatically replaced:
// - Smart quotes (""") become regular quotes (")
// - Em dash (—) becomes hyphen (-)
// - Ellipsis (…) becomes period (.)
// - Hiragana characters may be replaced with Katakana equivalents if available
// - Fullwidth characters may be replaced with halfwidth equivalents

font.DrawString(spriteBatch, textWithSpecialChars, position, Color.White);
```

## Integration with DTXManiaCX

### Stage Integration
```csharp
public class GameStage : IStage
{
    private IResourceManager _resourceManager;

    public void Initialize(IResourceManager resourceManager)
    {
        _resourceManager = resourceManager;
        LoadStageResources();
    }

    private void LoadStageResources()
    {
        // Load stage-specific resources
        _backgroundTexture = _resourceManager.LoadTexture("Stages/Game/background.jpg");
        _noteTextures = new Dictionary<NoteType, ITexture>
        {
            [NoteType.Don] = _resourceManager.LoadTexture("Notes/don.png"),
            [NoteType.Ka] = _resourceManager.LoadTexture("Notes/ka.png")
        };
    }

    public void Dispose()
    {
        // Resources automatically disposed via reference counting
        _backgroundTexture?.RemoveReference();
        foreach (var texture in _noteTextures.Values)
        {
            texture?.RemoveReference();
        }
    }
}
```

### Skin System Usage
```csharp
public class SkinManager
{
    private readonly IResourceManager _resourceManager;

    public void ChangeSkin(string skinName)
    {
        // Unload current skin resources
        _resourceManager.UnloadByPattern("Graphics/");
        _resourceManager.UnloadByPattern("Sounds/");

        // Set new skin path
        _resourceManager.SetSkinPath($"System/{skinName}/");

        // Resources will be reloaded automatically on next access
    }
}
```

## Performance Considerations

### 1. Caching Strategy
- **Path-based Cache**: Same path returns same texture instance with case-insensitive comparison
- **Texture Caching**: Textures cached by normalized path + parameters
- **Font Caching**: Fonts cached by normalized path + size + style
- **Reference Counting**: Prevents duplicate loading of same resource and auto-disposal

### 2. Memory Management
- **Automatic Disposal**: Resources disposed when reference count reaches zero
- **Leak Detection**: Finalizers detect and log disposal leaks
- **Memory Monitoring**: Track total memory usage and cache statistics

### 3. Loading Optimization
- **Lazy Loading**: Resources loaded on first access
- **Fallback Loading**: Graceful degradation when resources missing
- **Validation**: File existence checks before loading attempts

## Error Handling

### Resource Load Failures
```csharp
resourceManager.ResourceLoadFailed += (sender, e) =>
{
    Logger.Warning($"Failed to load resource: {e.Path} - {e.ErrorMessage}");
    // Fallback resources are automatically provided
};
```

### Disposal Leak Detection
```csharp
// Finalizers automatically detect and log disposal leaks
// Output: "ManagedTexture: Dispose leak detected for texture: background.jpg"
```

## Testing

The resource management system includes comprehensive unit tests covering:

### Core Functionality Tests
- **ResourceManagerTests**: Basic resource loading, path resolution, and skin system
- **ManagedTextureTests**: Texture creation parameters and validation
- **ManagedFontTests**: Font loading and character support
- **ResourceInterfaceTests**: Interface contracts and data structures

### Caching System Tests
- **CachingSystemTests**: Path-based cache, case-insensitive comparison, reference counting
- **Path Normalization**: Windows/Unix path compatibility and case handling
- **Cache Statistics**: Hit/miss tracking and memory usage calculation
- **Resource Collection**: Unused resource cleanup and pattern-based unloading

### Integration Tests
- **SkinManagerTests**: Skin discovery, validation, and switching
- **SkinDiscoveryServiceTests**: Skin analysis and completeness checking
- **ConfigDataTests**: Configuration persistence and validation

All tests use xUnit framework and follow DTXMania testing patterns with proper setup/teardown.

## Future Enhancements

1. **Async Loading**: Support for asynchronous resource loading
2. **Streaming**: Large resource streaming for memory optimization
3. **Compression**: Built-in texture compression support
4. **Hot Reloading**: Development-time resource hot reloading
5. **Custom Formats**: Support for DTXMania-specific resource formats

## DTXMania Skin Path Structure

The system follows the correct DTXMania skin path convention:

### Default Skin
- **Path**: `System/`
- **Graphics**: `System/Graphics/`
- **Example**: `System/Graphics/1_background.jpg`

### Custom Skins
- **Path**: `System/{SkinName}/`
- **Graphics**: `System/{SkinName}/Graphics/`
- **Example**: `System/MyCustomSkin/Graphics/1_background.jpg`

This structure ensures compatibility with original DTXMania while providing proper fallback behavior.

## Conclusion

The resource management architecture provides a robust, DTXMania-compatible system for handling game resources with proper memory management, skin support, and performance optimization. The reference counting system ensures resources are properly disposed, while the skin system enables easy theme switching without code changes.
