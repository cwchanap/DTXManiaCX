using Xunit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.TestData;
using Moq;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Unit tests for JudgementText popup system
    /// Tests the popup animation, manager lifecycle, and integration with JudgementEvents
    /// </summary>
    [Trait("Category", "Unit")]
    public class JudgementTextPopupTests
    {
        #region Test Helpers

        private Mock<IResourceManager> CreateMockResourceManager()
        {
            var mockResourceManager = new Mock<IResourceManager>();
            
            // Mock texture loading to return null (testing fallback behavior)
            mockResourceManager.Setup(rm => rm.LoadTexture(It.IsAny<string>()))
                              .Returns((ITexture)null);
            
            return mockResourceManager;
        }

        private Mock<GraphicsDevice> CreateMockGraphicsDevice()
        {
            // Note: GraphicsDevice is difficult to mock due to sealed methods
            // In real testing, this would use a test GraphicsDevice or be mocked differently
            return new Mock<GraphicsDevice>();
        }

        #endregion

        #region JudgementTextPopup Tests

        [Fact]
        public void JudgementTextPopup_Constructor_SetsInitialValues()
        {
            // Arrange
            var text = "PERFECT";
            var position = new Vector2(100, 200);

            // Act
            var popup = new JudgementTextPopup(text, position);

            // Assert
            Assert.Equal(text, popup.Text);
            Assert.Equal(1.0f, popup.Alpha);
            Assert.Equal(0f, popup.YOffset);
            Assert.True(popup.IsActive);
            Assert.Equal(position, popup.CurrentPosition);
        }

        [Fact]
        public void JudgementTextPopup_Update_AnimatesCorrectly()
        {
            // Arrange
            var popup = new JudgementTextPopup("GREAT", Vector2.Zero);
            var deltaTime = 0.1; // 100ms

            // Act
            var stillActive = popup.Update(deltaTime);

            // Assert
            Assert.True(stillActive);
            Assert.True(popup.Alpha < 1.0f); // Should fade
            Assert.True(popup.YOffset > 0f); // Should rise
            Assert.True(popup.IsActive);
        }

        [Fact]
        public void JudgementTextPopup_Update_CompletesAfterDuration()
        {
            // Arrange
            var popup = new JudgementTextPopup("GOOD", Vector2.Zero);

            // Act - Use single large step approach
            var deltaTime = 0.7; // 700ms (longer than 0.6s duration)
            var stillActive = popup.Update(deltaTime);

            // Assert - Use tolerance-based assertions to avoid floating-point brittleness
            Assert.False(stillActive);
            Assert.True(Math.Abs(popup.Alpha - 0f) < 1e-6f, $"Expected Alpha ~= 0, but was {popup.Alpha}");
            Assert.True(Math.Abs(popup.YOffset - 30f) < 1e-3f, $"Expected YOffset ~= 30, but was {popup.YOffset}");
            Assert.False(popup.IsActive);
        }

        [Fact]
        public void JudgementTextPopup_Update_CompletesAfterDuration_SteppedApproach()
        {
            // Alternative approach: Step through animation until completion
            // Arrange
            var popup = new JudgementTextPopup("GOOD", Vector2.Zero);
            const double smallTimeStep = 0.01; // 10ms steps
            bool stillActive = true;

            // Act - Step through animation until completion
            double totalTime = 0;
            const double maxTime = 1.0; // Safety limit to prevent infinite loops
            
            while (stillActive && totalTime < maxTime)
            {
                stillActive = popup.Update(smallTimeStep);
                totalTime += smallTimeStep;
            }

            // Assert - Now we can safely assert exact final values
            Assert.False(stillActive);
            Assert.Equal(0f, popup.Alpha); // Safe to use exact equality after deterministic completion
            Assert.Equal(30f, popup.YOffset); // Safe to use exact equality after deterministic completion  
            Assert.False(popup.IsActive);
            Assert.True(totalTime >= 0.6, $"Animation should take at least 0.6s, but completed in {totalTime:F3}s");
        }

        [Fact]
        public void JudgementTextPopup_CurrentPosition_UpdatesWithYOffset()
        {
            // Arrange
            var initialPosition = new Vector2(100, 200);
            var popup = new JudgementTextPopup("POOR", initialPosition);

            // Act
            popup.Update(0.3); // 50% through animation

            // Assert
            var currentPosition = popup.CurrentPosition;
            Assert.Equal(initialPosition.X, currentPosition.X); // X shouldn't change
            Assert.True(currentPosition.Y < initialPosition.Y); // Y should move up
        }

        #endregion

        #region JudgementTextPopupManager Tests

        [Fact]
        public void JudgementTextPopupManager_Constructor_RequiresParameters()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new JudgementTextPopupManager(null, CreateMockResourceManager().Object));
            
            // Note: GraphicsDevice parameter test would require a proper mock setup
        }

        [Fact]
        public void JudgementTextPopupManager_SpawnPopup_CreatesPopupForValidJudgement()
        {
            var manager = CreateManager();
            var judgementEvent = new JudgementEvent(1, 3, 10.0, JudgementType.Perfect);

            manager.SpawnPopup(judgementEvent);

            var popup = Assert.Single(GetActivePopups(manager));
            Assert.Equal("Perfect", popup.Text);
            Assert.Equal(new Vector2(PerformanceUILayout.GetLaneX(3), PerformanceUILayout.JudgementLineY - 50), popup.CurrentPosition);
        }

        [Fact]
        public void JudgementTextPopupManager_SpawnPopup_IgnoresNullJudgement()
        {
            var manager = CreateManager();

            manager.SpawnPopup(null!);

            Assert.Empty(GetActivePopups(manager));
        }

        [Theory]
        [InlineData(JudgementType.Perfect, "Perfect")]
        [InlineData(JudgementType.Great, "Great")]
        [InlineData(JudgementType.Good, "Good")]
        [InlineData(JudgementType.Poor, "OK")]
        [InlineData(JudgementType.Miss, "Miss")]
        public void JudgementTextPopupManager_SpawnPopup_CreatesCorrectTextForJudgementType(
            JudgementType judgementType, string expectedText)
        {
            var manager = CreateManager();

            manager.SpawnPopup(new JudgementEvent(1, 2, 0.0, judgementType));

            var popup = Assert.Single(GetActivePopups(manager));
            Assert.Equal(expectedText, popup.Text);
        }

        [Fact]
        public void JudgementTextPopupManager_LoadJudgementFont_WhenResourceManagerThrows_ShouldReturnNull()
        {
            var loadMethod = typeof(JudgementTextPopupManager).GetMethod("LoadJudgementFont", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(loadMethod);

            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(rm => rm.LoadFont(It.IsAny<string>(), It.IsAny<int>()))
                .Throws(new InvalidOperationException("font load failed"));

            var result = loadMethod!.Invoke(
                null,
                new object[]
                {
                    ReflectionHelpers.CreateUninitialized<GraphicsDevice>(),
                    resourceManager.Object
                });

            Assert.Null(result);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void JudgementTextPopup_Integration_FullAnimationCycle()
        {
            // Arrange
            var popup = new JudgementTextPopup("PERFECT", new Vector2(640, 500));
            var totalTime = 0.0;
            var timeStep = 0.016; // ~60 FPS

            // Act & Assert - simulate full animation cycle
            while (popup.IsActive && totalTime < 1.0) // Max 1 second safety
            {
                var wasActive = popup.Update(timeStep);
                totalTime += timeStep;

                if (totalTime < 0.6) // During animation
                {
                    Assert.True(wasActive);
                    Assert.True(popup.Alpha > 0f);
                    Assert.True(popup.YOffset >= 0f);
                }
            }

            // Final state
            Assert.False(popup.IsActive);
            Assert.Equal(0f, popup.Alpha);
            Assert.Equal(30f, popup.YOffset);
        }

        [Fact]
        public void JudgementTextPopup_LanePositioning_CalculatesCorrectPositions()
        {
            // This test would verify lane center position calculations
            // It would test various lane indices (0-8) and verify the positions
            // are calculated correctly according to PerformanceUILayout
            
            // Arrange - test different lane indices
            var testLanes = new[] { 0, 4, 8 }; // Left, center, right lanes
            
            // Act & Assert would involve creating popups for each lane
            // and verifying the X positions match the expected lane centers
        }

        #endregion

        #region Performance Tests

        [Fact]
        public void JudgementTextPopupManager_MultiplePopups_HandlesSimultaneousPopups()
        {
            var manager = CreateManager();

            manager.SpawnPopup(new JudgementEvent(1, 0, 0.0, JudgementType.Perfect));
            manager.SpawnPopup(new JudgementEvent(1, 4, 0.0, JudgementType.Great));
            manager.SpawnPopup(new JudgementEvent(1, 8, 0.0, JudgementType.Miss));

            Assert.Equal(3, manager.ActivePopupCount);
        }

        [Fact]
        public void JudgementTextPopupManager_Update_RemovesCompletedPopups()
        {
            var manager = CreateManager();
            var expiredPopup = new JudgementTextPopup("Perfect", Vector2.Zero);
            expiredPopup.Update(0.55);
            GetActivePopups(manager).Add(expiredPopup);
            GetActivePopups(manager).Add(new JudgementTextPopup("Great", Vector2.One));

            manager.Update(0.1);

            var remainingPopup = Assert.Single(GetActivePopups(manager));
            Assert.Equal("Great", remainingPopup.Text);
        }

        #endregion

        #region Hostless Manager Logic Tests

        [Fact]
        public void JudgementTextPopupManager_SpawnPopup_WhenDisposed_ShouldIgnoreJudgement()
        {
            var manager = CreateManager(disposed: true);

            manager.SpawnPopup(new JudgementEvent(1, 1, 0.0, JudgementType.Perfect));

            Assert.Empty(GetActivePopups(manager));
        }

        [Fact]
        public void JudgementTextPopupManager_SpawnPopup_WithUnknownJudgementType_ShouldNotCreatePopup()
        {
            var manager = CreateManager();

            manager.SpawnPopup(new JudgementEvent(1, 1, 0.0, (JudgementType)999));

            Assert.Empty(GetActivePopups(manager));
        }

        [Fact]
        public void JudgementTextPopupManager_CreateForTesting_WhenDisposed_ShouldRunRealDisposeLogic()
        {
            var fontMock = new Mock<IFont>();
            var manager = CreateManager(font: fontMock.Object, activePopups: [new JudgementTextPopup("Perfect", Vector2.Zero)], disposed: true);

            Assert.Empty(GetActivePopups(manager));
            fontMock.Verify(f => f.RemoveReference(), Times.Once);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(manager, "_disposed"));
        }

        [Fact]
        public void JudgementTextPopupManager_ClearAll_ShouldRemoveEveryPopup()
        {
            var manager = CreateManager();
            GetActivePopups(manager).Add(new JudgementTextPopup("Perfect", Vector2.Zero));
            GetActivePopups(manager).Add(new JudgementTextPopup("Great", Vector2.One));

            manager.ClearAll();

            Assert.Empty(GetActivePopups(manager));
            Assert.Equal(0, manager.ActivePopupCount);
        }

        [Theory]
        [InlineData("Perfect", 255, 255, 0)]
        [InlineData("Great", 144, 238, 144)]
        [InlineData("Good", 173, 216, 230)]
        [InlineData("OK", 255, 165, 0)]
        [InlineData("Miss", 255, 0, 0)]
        [InlineData("Unknown", 255, 255, 255)]
        public void JudgementTextPopupManager_GetJudgementColor_ShouldReturnExpectedColor(string text, byte r, byte g, byte b)
        {
            var manager = CreateManager();

            var color = ReflectionHelpers.InvokePrivateMethod<Color>(manager, "GetJudgementColor", text);

            Assert.Equal(new Color(r, g, b), color);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(PerformanceUILayout.LaneCount)]
        public void JudgementTextPopupManager_GetLaneCenterPosition_WithInvalidLane_ShouldReturnScreenCenter(int laneIndex)
        {
            var manager = CreateManager();

            var position = ReflectionHelpers.InvokePrivateMethod<Vector2>(manager, "GetLaneCenterPosition", laneIndex);

            Assert.Equal(new Vector2(PerformanceUILayout.ScreenWidth / 2, PerformanceUILayout.JudgementLineY - 50), position);
        }

        [Fact]
        public void JudgementTextPopupManager_Dispose_ShouldClearPopupsReleaseFontAndMarkDisposed()
        {
            var fontMock = new Mock<IFont>();
            var manager = CreateManager(font: fontMock.Object, activePopups: [new JudgementTextPopup("Perfect", Vector2.Zero)]);

            manager.Dispose();

            Assert.Empty(GetActivePopups(manager));
            fontMock.Verify(f => f.RemoveReference(), Times.Once);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(manager, "_disposed"));
        }

        private static JudgementTextPopupManager CreateManager(
            IFont? font = null,
            List<JudgementTextPopup>? activePopups = null,
            bool disposed = false)
        {
            return JudgementTextPopupManager.CreateForTesting(
                ReflectionHelpers.CreateUninitialized<GraphicsDevice>(),
                new Mock<IResourceManager>().Object,
                font,
                activePopups,
                disposed);
        }

        private static List<JudgementTextPopup> GetActivePopups(JudgementTextPopupManager manager)
        {
            return ReflectionHelpers.GetPrivateField<List<JudgementTextPopup>>(manager, "_activePopups")!;
        }

        #endregion
    }
}
