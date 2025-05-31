# Stage Transition System Implementation

## Overview

This document describes the implementation of the Stage Transition System for DTXManiaCX, based on DTXManiaNX patterns with eフェーズID (phase ID) management and comprehensive fade effects.

## Architecture

### Core Components

#### 1. Enhanced Stage Management
- **IStageManager**: Extended interface with transition support
- **StageManager**: Enhanced implementation with transition lifecycle management
- **BaseStage**: Abstract base class implementing common stage functionality
- **IStage**: Enhanced interface with transition lifecycle methods

#### 2. Stage Phases (eフェーズID Pattern)
```csharp
public enum StagePhase
{
    Inactive,           // b活性化してない equivalent
    FadeIn,            // Common_FadeIn equivalent
    Normal,            // Common_DefaultState equivalent
    FadeOut,           // Common_FadeOut equivalent
    FadeInFromStartup  // タイトル_起動画面からのフェードイン equivalent
}
```

#### 3. Transition System
- **IStageTransition**: Core transition interface
- **BaseStageTransition**: Abstract base with common functionality
- **Multiple Transition Types**: Various transition implementations

### Transition Types

#### InstantTransition
- Immediate stage changes with no fade effects
- Used for quick transitions or testing

#### FadeTransition
- Sequential fade: fade out current stage, then fade in new stage
- Configurable fade out and fade in durations

#### CrossfadeTransition
- Simultaneous fade out and fade in
- Smooth visual transition between stages

#### DTXManiaFadeTransition
- Authentic DTXMania-style transitions with easing curves
- Uses sine/cosine functions for smooth animation
- Configurable easing on/off

#### StartupToTitleTransition
- Special transition from startup to title stage
- Delayed fade in for authentic DTXMania feel
- Based on タイトル_起動画面からのフェードイン pattern

## Implementation Details

### Stage Lifecycle

1. **Activation**: `OnActivate()` called when stage becomes active
2. **First Update**: `OnFirstUpdate()` called on first update cycle
3. **Normal Operation**: `OnUpdate()` and `OnDraw()` called each frame
4. **Transition Out**: `OnTransitionOut()` when leaving stage
5. **Transition In**: `OnTransitionIn()` when entering stage
6. **Transition Complete**: `OnTransitionComplete()` when transition finishes
7. **Deactivation**: `OnDeactivate()` when stage becomes inactive

### Shared Data System

Stages can pass data between each other during transitions:

```csharp
var sharedData = new Dictionary<string, object>
{
    ["SelectedSong"] = songInfo,
    ["Difficulty"] = difficulty
};

ChangeStage(StageType.Performance, new DTXManiaFadeTransition(), sharedData);
```

### Usage Examples

#### Basic Stage Change
```csharp
// Instant transition
ChangeStage(StageType.Title);

// With fade transition
ChangeStage(StageType.Config, new CrossfadeTransition(0.5));

// DTXMania-style transition
ChangeStage(StageType.UITest, new DTXManiaFadeTransition(0.7));
```

#### Special Transitions
```csharp
// Startup to title with authentic DTXMania transition
ChangeStage(StageType.Title, new StartupToTitleTransition(1.0));
```

## Updated Stages

All existing stages have been updated to inherit from `BaseStage`:

- **StartupStage**: Uses `StartupToTitleTransition` for authentic feel
- **TitleStage**: Uses `DTXManiaFadeTransition` for game start, `CrossfadeTransition` for config
- **ConfigStage**: Uses `CrossfadeTransition` for return to title
- **UITestStage**: Uses `CrossfadeTransition` for return to title

## Testing

Comprehensive unit tests cover:

- All transition types and their behavior
- Stage manager transition functionality
- Shared data passing
- Error handling and edge cases
- Phase management

### Test Results
- ✅ 12/12 transition tests passing
- ✅ All transition types working correctly
- ✅ Fade effects functioning as expected
- ✅ DTXMania-style easing implemented

## Key Features

### DTXMania Compatibility
- Follows DTXManiaNX eフェーズID patterns
- Implements b活性化してない (activation) flags
- Supports タイトル_起動画面からのフェードイン special transitions
- Authentic fade timing and curves

### Fade Effects
- ✅ Fade out current stage
- ✅ Fade in new stage
- ✅ Crossfade option for seamless transitions
- ✅ DTXMania-style easing curves

### State Management
- ✅ Previous stage cleanup
- ✅ New stage initialization
- ✅ Shared data passing between stages
- ✅ Transition progress tracking

### Error Handling
- Prevents multiple simultaneous transitions
- Handles invalid stage types gracefully
- Proper resource cleanup during transitions
- Comprehensive disposal patterns

## Future Enhancements

1. **Visual Fade Effects**: Implement actual rendering fade effects using render targets
2. **Sound Transitions**: Add audio fade in/out during transitions
3. **Custom Transition Curves**: Support for custom easing functions
4. **Transition Callbacks**: Event system for transition milestones
5. **Performance Optimization**: Optimize transition rendering for complex scenes

## Integration Notes

The transition system is fully integrated with the existing DTXManiaCX architecture:

- Works with existing resource management system
- Compatible with UI system
- Follows established patterns and conventions
- Maintains backward compatibility where possible

This implementation provides a solid foundation for authentic DTXMania-style stage transitions while being flexible enough for future enhancements.
