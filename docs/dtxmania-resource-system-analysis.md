# DTXMania Resource System Analysis

## Overview

This document analyzes DTXMania's resource management system, focusing on texture loading, skin system, font management, and resource lifecycle patterns found in the DTXManiaNX codebase.

## Core Resource System Components

### 1. CTexture.cs - Core Texture Management

**Location**: `DTXManiaNX/FDK/Code/04.Graphics/CTexture.cs`

**Key Features**:
- **SharpDX-based**: Uses SharpDX.Direct3D9 for texture management
- **Multiple constructors**: Supports loading from files, bitmaps, byte arrays, and creating empty textures
- **Format support**: BMP, JPG, PNG, TGA, DDS, PPM, DIB, HDR, PFM
- **Automatic sizing**: Adjusts texture size to device capabilities (power-of-2, max dimensions)
- **Memory pools**: Supports both Managed and Default pools
- **Color key transparency**: Configurable black transparency (0xFF000000)

**Loading Patterns**:
```csharp
// File loading
this.texture = Texture.FromMemory(device, txData, width, height, 1, Usage.None, format, pool, Filter.Point, Filter.None, colorKey);

// Stream loading
this.texture = Texture.FromStream(device, stream, width, height, 1, Usage.None, format, pool, Filter.Point, Filter.None, colorKey);
```

**Resource Disposal**:
- Implements IDisposable with proper disposal pattern
- Finalizer detects disposal leaks and logs warnings
- Tracks disposal state with `bSharpDXTextureDispose完了済み` flag
- Automatic cleanup in Dispose(bool) method

### 2. CSkin.cs - Skin System and Resource Paths

**Location**: `DTXManiaNX/DTXMania/Code/App/CSkin.cs`

**Key Features**:
- **Path resolution**: Central `CSkin.Path()` method for resource path resolution
- **Skin switching**: Support for box.def skins and system skins
- **Sound management**: System sounds with dual-buffer playback
- **Validation**: Skin validity checking via title background existence

**Path Resolution Pattern**:
```csharp
public static string Path(string strファイルの相対パス)
{
    if (string.IsNullOrEmpty(strBoxDefSkinSubfolderFullName) || !bUseBoxDefSkin)
    {
        return System.IO.Path.Combine(strSystemSkinSubfolderFullName, strファイルの相対パス);
    }
    else
    {
        return System.IO.Path.Combine(strBoxDefSkinSubfolderFullName, strファイルの相対パス);
    }
}
```

**Resource Structure**:
- System skins: `System/[SkinName]/`
- Graphics: `Graphics/` subfolder
- Sounds: `Sounds/` subfolder
- Validation file: `Graphics/1_background.jpg`

**Sound System**:
- Dual-buffer system for seamless playback
- Exclusive playback support for BGM
- Lazy loading with `bReadNotTried` flag
- Automatic sound generation via `CDTXMania.SoundManager.tGenerateSound()`

### 3. Font Management System

**CPrivateFont.cs** - `DTXManiaNX/DTXMania/Code/App/CPrivateFont.cs`

**Features**:
- **Private font loading**: TTF/OTF font file support
- **System font fallback**: MS PGothic as default fallback
- **Style adaptation**: Automatic style selection when requested style unavailable
- **Rendering modes**: Normal, Edge (outline), Gradation
- **High DPI support**: Pixel-based font sizing

**Font Loading Pattern**:
```csharp
this._pfc = new System.Drawing.Text.PrivateFontCollection();
this._pfc.AddFontFile(fontpath);
this._fontfamily = _pfc.Families[0];
float emSize = pt * 96.0f / 72.0f;
this._font = new Font(this._fontfamily, emSize, style, GraphicsUnit.Pixel);
```

**CPrivateFastFont.cs** - Optimized font rendering for performance-critical scenarios

## Resource Lifecycle Management

### Loading Patterns

1. **Lazy Loading**: Resources loaded on first access
2. **Validation**: File existence checks before loading
3. **Fallback**: Graceful degradation when resources missing
4. **Caching**: Loaded resources retained for reuse

### Disposal Patterns

**Consistent IDisposable Implementation**:
```csharp
public void Dispose()
{
    if (!this.bDispose完了済み)
    {
        if (this.resource != null)
        {
            this.resource.Dispose();
            this.resource = null;
        }
        this.bDispose完了済み = true;
    }
}
```

**Finalizer-based Leak Detection**:
```csharp
~CTexture()
{
    if (!this.bSharpDXTextureDispose完了済み)
    {
        Trace.TraceWarning("CTexture: Dispose漏れを検出しました。(Size=({0}, {1}), filename={2})",
                          szImageSize.Width, szImageSize.Height, filename);
    }
    this.Dispose(false);
}
```

### Memory Management

- **Pool Management**: Managed vs Default pools based on usage
- **Texture Sizing**: Automatic power-of-2 adjustment for compatibility
- **Resource Tracking**: Filename and size tracking for debugging
- **Leak Detection**: Finalizer warnings for undisposed resources

## Usage Examples from Codebase

### Texture Loading in Song Selection
```csharp
// From CActSelectSongList.cs
string backgroundPath = Path.Combine("DTXManiaNX", "Runtime", "System", "Graphics", "2_background.jpg");
if (File.Exists(backgroundPath))
{
    using (var fileStream = File.OpenRead(backgroundPath))
    {
        _backgroundTexture = Texture2D.FromStream(_game.GraphicsDevice, fileStream);
    }
}
```

### Sound Resource Management
```csharp
// From CSkin.cs - System sound initialization
this.soundDecide = new CSystemSound(@"Sounds\Decide.ogg", false, false, false);
this.soundCancel = new CSystemSound(@"Sounds\Cancel.ogg", false, false, true);

// Usage with path resolution
string strnov = CSkin.Path(@"Sounds\Novice.ogg");
if (!File.Exists(strnov))
    CDTXMania.Skin.soundChange.tPlay();
```

## Key Architectural Patterns

### 1. Centralized Path Management
- All resource paths go through `CSkin.Path()`
- Supports skin switching without code changes
- Hierarchical fallback system

### 2. Resource Validation
- File existence checks before loading
- Graceful fallback to defaults
- Error logging for missing resources

### 3. Memory Safety
- Consistent disposal patterns
- Leak detection via finalizers
- Resource state tracking

### 4. Performance Optimization
- Lazy loading for non-critical resources
- Dual-buffer sound system
- Texture size optimization

## Recommendations for DTXManiaCX

1. **Adopt Path Resolution Pattern**: Implement centralized resource path management
2. **Implement Disposal Tracking**: Add finalizer-based leak detection
3. **Use Validation Patterns**: Check resource existence before loading
4. **Support Fallback Resources**: Graceful degradation when resources missing
5. **Optimize Texture Loading**: Implement size adjustment for compatibility
6. **Add Resource Caching**: Cache frequently used resources
7. **Implement Skin System**: Support for theme/skin switching

## Technical Notes

- **SharpDX Dependency**: DTXMania uses SharpDX for Direct3D9 integration
- **Japanese Comments**: Original codebase uses Japanese variable names and comments
- **Legacy Compatibility**: Supports older graphics hardware via texture size adjustment
- **Thread Safety**: Some resource loading uses locking mechanisms
- **Error Handling**: Extensive try-catch blocks with logging

---

## Implementation Status

✅ **Analysis Complete**: DTXMania resource system patterns documented
🚧 **In Progress**: Resource Management Architecture implementation
⏳ **Next**: IResourceManager, ITexture, and IFont abstractions
