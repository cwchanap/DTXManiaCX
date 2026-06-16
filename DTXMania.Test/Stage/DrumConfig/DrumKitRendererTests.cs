using System.Reflection;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.DrumConfig;
using Xunit;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    public class DrumKitRendererTests
    {
        [Fact]
        public void ZoneColor_ForCymbalShape_ReturnsCorrectColor()
        {
            // Act - Use reflection to test the private static method
            var method = typeof(DrumKitRenderer).GetMethod("ZoneColor", 
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            
            var color = (Microsoft.Xna.Framework.Color)method!.Invoke(null, new object[] { DrumZoneShape.Cymbal });

            // Assert
            Assert.Equal(214, color.R);
            Assert.Equal(177, color.G);
            Assert.Equal(60, color.B);
        }

        [Fact]
        public void ZoneColor_ForDrumShape_ReturnsCorrectColor()
        {
            // Act - Use reflection to test the private static method
            var method = typeof(DrumKitRenderer).GetMethod("ZoneColor", 
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            
            var color = (Microsoft.Xna.Framework.Color)method!.Invoke(null, new object[] { DrumZoneShape.Drum });

            // Assert
            Assert.Equal(60, color.R);
            Assert.Equal(110, color.G);
            Assert.Equal(170, color.B);
        }

        [Fact]
        public void ZoneColor_ForKickShape_ReturnsCorrectColor()
        {
            // Act - Use reflection to test the private static method
            var method = typeof(DrumKitRenderer).GetMethod("ZoneColor", 
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            
            var color = (Microsoft.Xna.Framework.Color)method!.Invoke(null, new object[] { DrumZoneShape.Kick });

            // Assert
            Assert.Equal(40, color.R);
            Assert.Equal(44, color.G);
            Assert.Equal(56, color.B);
        }

        [Fact]
        public void ZoneColor_ForPedalShape_ReturnsCorrectColor()
        {
            // Act - Use reflection to test the private static method
            var method = typeof(DrumKitRenderer).GetMethod("ZoneColor", 
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            
            var color = (Microsoft.Xna.Framework.Color)method!.Invoke(null, new object[] { DrumZoneShape.Pedal });

            // Assert
            Assert.Equal(74, color.R);
            Assert.Equal(79, color.G);
            Assert.Equal(90, color.B);
        }

        [Fact]
        public void ZoneColor_ForUnknownShape_ReturnsGray()
        {
            // Act - Use reflection to test the private static method
            var method = typeof(DrumKitRenderer).GetMethod("ZoneColor", 
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            
            var color = (Microsoft.Xna.Framework.Color)method!.Invoke(null, new object[] { (DrumZoneShape)999 });

            // Assert
            Assert.Equal(Microsoft.Xna.Framework.Color.Gray, color);
        }
    }
}
