# DTXManiaCX Skin System Implementation

## Overview

Task 3 has been successfully completed! The skin system implementation provides DTXMania-compatible skin management with the following key features:

- **DTXMania-style path resolution** using `CSkin.Path()` patterns
- **Dual skin support** for system skins and box.def skins
- **Automatic fallback** to default skin when resources are missing
- **Skin discovery and validation** with metadata support
- **Configuration management** for skin preferences
- **✅ Path Resolution Fix**: Fixed hardcoded paths in StartupStage and TitleStage

## Architecture Components

### 1. Enhanced ResourceManager

**File**: `DTXMania.Shared.Game\Lib\Resources\ResourceManager.cs`

**New Features**:
- Box.def skin path support (`SetBoxDefSkinPath()`)
- DTXMania-style path resolution with priority handling
- Enable/disable box.def skin usage (`SetUseBoxDefSkin()`)
- Current effective skin path retrieval (`GetCurrentEffectiveSkinPath()`)

**Usage Example**:
```csharp
// Set system skin
resourceManager.SetSkinPath("System/MyCustomSkin/");

// Set box.def skin (takes priority)
resourceManager.SetBoxDefSkinPath("Songs/MySong/CustomSkin/");

// Load texture with automatic path resolution
var texture = resourceManager.LoadTexture("Graphics/background.jpg");
// Resolves to: Songs/MySong/CustomSkin/Graphics/background.jpg
```

### 2. SkinManager

**File**: `DTXMania.Shared.Game\Lib\Resources\SkinManager.cs`

**Features**:
- Discover available system skins
- Switch between skins with proper resource cleanup
- Validate skin completeness
- Manage box.def skin overrides

**Usage Example**:
```csharp
var skinManager = new SkinManager(resourceManager);

// Discover available skins
skinManager.RefreshAvailableSkins();
Console.WriteLine($"Found {skinManager.AvailableSystemSkins.Count} skins");

// Switch to a different skin
if (skinManager.SwitchToSystemSkin("MyCustomSkin"))
{
    Console.WriteLine("Skin switched successfully");
}

// Set temporary box.def skin
skinManager.SetBoxDefSkin("Songs/MySong/SkinPath/");
```

### 3. SkinDiscoveryService

**File**: `DTXMania.Shared.Game\Lib\Resources\SkinDiscoveryService.cs`

**Features**:
- Detailed skin analysis and metadata extraction
- Skin completeness calculation
- SkinConfig.ini metadata reading
- Validation based on DTXMania patterns

**Usage Example**:
```csharp
var discoveryService = new SkinDiscoveryService("System/");

// Discover all skins with detailed info
var skins = discoveryService.DiscoverSkins();

foreach (var skin in skins)
{
    Console.WriteLine($"Skin: {skin.Name}");
    Console.WriteLine($"  Valid: {skin.IsValid}");
    Console.WriteLine($"  Completeness: {discoveryService.GetSkinCompleteness(skin.FullPath)}%");
    Console.WriteLine($"  Author: {skin.Author}");
    Console.WriteLine($"  Size: {skin.SizeBytes / 1024} KB");
}
```

### 4. Enhanced Configuration

**File**: `DTXMania.Shared.Game\Lib\Config\ConfigData.cs`

**New Settings**:
- `UseBoxDefSkin`: Enable/disable box.def skin support
- `SystemSkinRoot`: Root directory for system skins
- `LastUsedSkin`: Remember last used skin

**Configuration Example**:
```ini
[System]
DTXManiaVersion=NX1.5.0-MG
SkinPath=System/Default/
DTXPath=DTXFiles/

[Skin]
UseBoxDefSkin=True
SystemSkinRoot=System/
LastUsedSkin=MyCustomSkin
```

## DTXMania Compatibility

### Path Resolution

The system follows DTXMania's `CSkin.Path()` behavior:

1. **Box.def skin priority**: If a box.def skin is set and enabled, it takes priority
2. **System skin fallback**: Falls back to the configured system skin
3. **Default skin fallback**: Falls back to System/Default/ if resources are missing

### Skin Validation

Skins are validated using DTXMania's `bIsValid()` pattern:
- Must contain `Graphics\1_background.jpg`
- Must contain `Graphics\2_background.jpg`

### Folder Structure

Expected DTXMania-compatible folder structure:
```
System/
├── Default/
│   ├── Graphics/
│   │   ├── 1_background.jpg
│   │   ├── 2_background.jpg
│   │   └── ...
│   └── Sounds/
│       ├── Decide.ogg
│       ├── Cancel.ogg
│       └── ...
└── MyCustomSkin/
    ├── Graphics/
    ├── Sounds/
    └── SkinConfig.ini (optional)
```

## Integration Examples

### Basic Skin Management

```csharp
// Initialize resource manager with skin support
var resourceManager = new ResourceManager(graphicsDevice);
var skinManager = new SkinManager(resourceManager);

// Load default skin
resourceManager.SetSkinPath("System/Default/");

// Load a texture (automatically resolves path)
var backgroundTexture = resourceManager.LoadTexture("Graphics/1_background.jpg");
```

### Advanced Skin Switching

```csharp
public class GameSkinManager
{
    private readonly SkinManager _skinManager;
    private readonly ConfigManager _configManager;

    public void LoadUserPreferredSkin()
    {
        var lastUsedSkin = _configManager.Config.LastUsedSkin;

        if (_skinManager.SwitchToSystemSkin(lastUsedSkin))
        {
            Console.WriteLine($"Loaded preferred skin: {lastUsedSkin}");
        }
        else
        {
            // Fallback to default
            _skinManager.SwitchToSystemSkin("Default");
        }
    }

    public void HandleBoxDefSkin(string songPath)
    {
        var boxDefSkinPath = Path.Combine(songPath, "SkinPath");

        if (Directory.Exists(boxDefSkinPath) && _configManager.Config.UseBoxDefSkin)
        {
            _skinManager.SetBoxDefSkin(boxDefSkinPath);
        }
    }
}
```

## Testing

The implementation includes comprehensive unit tests:

- **SkinManagerTests**: 12 tests covering skin switching, validation, and box.def support
- **SkinDiscoveryServiceTests**: 11 tests covering skin discovery, analysis, and metadata
- **ConfigDataTests**: Enhanced with skin configuration tests

All tests pass and provide good coverage of the skin system functionality.

## Path Resolution Fix

### Issue Resolved
Fixed hardcoded paths in StartupStage and TitleStage that were causing "texture not found" errors:
```
❌ Before: DTXManiaNX\Runtime\System\Graphics\1_background.jpg
✅ After:  System\Graphics\1_background.jpg (relative to current directory)
```

### Changes Made
- **StartupStage.cs**: Updated to use `_resourceManager.LoadTexture("Graphics/1_background.jpg")`
- **TitleStage.cs**: Updated to use ResourceManager for `Graphics/2_background.jpg` and `Graphics/2_menu.png`
- **Enhanced path resolution**: All paths now resolve correctly relative to current working directory

### Result
Resources now load correctly from the current working directory using the proper skin system.

## Summary

The skin system implementation successfully provides:

✅ **DTXMania-compatible path resolution** with `CSkin.Path()` behavior
✅ **Box.def skin support** for song-specific skins
✅ **Automatic fallback system** for missing resources
✅ **Skin discovery and validation** with metadata support
✅ **Configuration management** for skin preferences
✅ **Comprehensive unit tests** with 100% pass rate
✅ **Fixed path resolution** for all game stages

The system is ready for integration into the DTXManiaCX game engine and provides a solid foundation for theme and skin management.
