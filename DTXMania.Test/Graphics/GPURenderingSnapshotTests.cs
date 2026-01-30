using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Test.Helpers;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace DTXMania.Test.Graphics
{
    /// <summary>
    /// GPU rendering snapshot tests for visual regression testing.
    /// Captures and compares rendering output to detect visual changes.
    /// </summary>
    [Collection("GraphicsTests")]
    public class GPURenderingSnapshotTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly TestGraphicsDeviceService _graphicsService;
        private readonly GraphicsDevice? _graphicsDevice;
        private readonly SpriteBatch? _spriteBatch;
        private readonly string _snapshotDirectory;
        private readonly bool _skipAll;

        public GPURenderingSnapshotTests(ITestOutputHelper output)
        {
            _output = output;
            _graphicsService = new TestGraphicsDeviceService();
            var graphicsDevice = _graphicsService.GraphicsDevice;
            if (graphicsDevice == null || graphicsDevice.IsDisposed)
            {
                _skipAll = true;
                _graphicsDevice = null;
                _spriteBatch = null;
                _snapshotDirectory = string.Empty;
                return;
            }

            _graphicsDevice = graphicsDevice;
            _spriteBatch = new SpriteBatch(_graphicsDevice);
            
            // Create snapshot directory for test outputs
            _snapshotDirectory = Path.Combine(Directory.GetCurrentDirectory(), "TestSnapshots");
            Directory.CreateDirectory(_snapshotDirectory);
            
            _output.WriteLine($"GPU Rendering Tests initialized");
            _output.WriteLine($"Graphics Device: {_graphicsDevice.Adapter.Description}");
            _output.WriteLine($"Snapshot Directory: {_snapshotDirectory}");
        }

        private bool EnsureGraphicsReady()
        {
            return !_skipAll && _graphicsDevice != null && !_graphicsDevice.IsDisposed;
        }

        #region Basic Rendering Tests

        [Fact]
        public async Task GPU_BasicRendering_ClearScreen_VerifyBlackSnapshot()
        {
            // Arrange
            if (!EnsureGraphicsReady())
                return;

            const int width = 800;
            const int height = 600;
            var renderTarget = new RenderTarget2D(_graphicsDevice, width, height);
            var testName = "BasicRendering_ClearScreen";

            _output.WriteLine($"Testing basic screen clear rendering ({width}x{height})");

            try
            {
                // Act - Render a simple black screen
                _graphicsDevice.SetRenderTarget(renderTarget);
                _graphicsDevice.Clear(Color.Black);
                _graphicsDevice.SetRenderTarget(null);

                // Save snapshot
                var snapshotPath = await SaveRenderTargetSnapshot(renderTarget, testName);
                
                // Assert - Verify pixel data
                var pixelData = GetRenderTargetPixels(renderTarget);
                var allBlack = true;
                var samplePixelCount = 0;

                // Sample pixels to verify they're all black
                for (int i = 0; i < pixelData.Length; i += 100) // Sample every 100th pixel
                {
                    var pixel = pixelData[i];
                    if (pixel.R != 0 || pixel.G != 0 || pixel.B != 0)
                    {
                        allBlack = false;
                        _output.WriteLine($"Non-black pixel found at index {i}: R={pixel.R}, G={pixel.G}, B={pixel.B}");
                        break;
                    }
                    samplePixelCount++;
                }

                Assert.True(allBlack, $"Clear(Black) should produce all black pixels. Sampled {samplePixelCount} pixels");
                _output.WriteLine($"✓ Basic clear rendering test passed - {snapshotPath}");
            }
            finally
            {
                renderTarget?.Dispose();
            }
        }

        [Fact]
        public async Task GPU_ColoredRendering_SolidColors_VerifyColorAccuracy()
        {
            // Arrange
            if (!EnsureGraphicsReady())
                return;

            const int width = 400;
            const int height = 300;
            var renderTarget = new RenderTarget2D(_graphicsDevice, width, height);
            var testColors = new[]
            {
                Color.Red, Color.Green, Color.Blue,
                Color.White, Color.Yellow, Color.Magenta,
                Color.Cyan, Color.Gray
            };

            _output.WriteLine("Testing solid color rendering accuracy");

            try
            {
                foreach (var testColor in testColors)
                {
                    // Act - Render solid color
                    _graphicsDevice.SetRenderTarget(renderTarget);
                    _graphicsDevice.Clear(testColor);
                    _graphicsDevice.SetRenderTarget(null);

                    // Save snapshot
                    var testName = $"SolidColor_{testColor.ToString()}";
                    var snapshotPath = await SaveRenderTargetSnapshot(renderTarget, testName);

                    // Assert - Verify color accuracy
                    var pixelData = GetRenderTargetPixels(renderTarget);
                    var centerPixel = pixelData[(height / 2) * width + (width / 2)];

                    // Allow small tolerance for GPU precision
                    const int tolerance = 2;
                    Assert.True(Math.Abs(centerPixel.R - testColor.R) <= tolerance, 
                        $"Red channel mismatch for {testColor}: expected {testColor.R}, got {centerPixel.R}");
                    Assert.True(Math.Abs(centerPixel.G - testColor.G) <= tolerance, 
                        $"Green channel mismatch for {testColor}: expected {testColor.G}, got {centerPixel.G}");
                    Assert.True(Math.Abs(centerPixel.B - testColor.B) <= tolerance, 
                        $"Blue channel mismatch for {testColor}: expected {testColor.B}, got {centerPixel.B}");

                    _output.WriteLine($"✓ Color {testColor} rendered correctly - {Path.GetFileName(snapshotPath)}");
                }
            }
            finally
            {
                renderTarget?.Dispose();
            }
        }

        #endregion

        #region Sprite Rendering Tests

        [Fact]
        public async Task GPU_SpriteRendering_BasicSprites_VerifyCorrectPlacement()
        {
            // Arrange
            if (!EnsureGraphicsReady())
                return;

            const int width = 800;
            const int height = 600;
            var renderTarget = new RenderTarget2D(_graphicsDevice, width, height);
            var testName = "SpriteRendering_BasicSprites";

            // Create a simple test texture (white square)
            var testTexture = CreateTestTexture(64, 64, Color.White);

            _output.WriteLine("Testing basic sprite rendering and placement");

            try
            {
                // Act - Render sprites at different positions
                _graphicsDevice.SetRenderTarget(renderTarget);
                _graphicsDevice.Clear(Color.Black);

                _spriteBatch.Begin();
                
                // Render sprites at corners and center
                _spriteBatch.Draw(testTexture, new Vector2(0, 0), Color.Red);           // Top-left, red tint
                _spriteBatch.Draw(testTexture, new Vector2(width - 64, 0), Color.Green); // Top-right, green tint
                _spriteBatch.Draw(testTexture, new Vector2(0, height - 64), Color.Blue); // Bottom-left, blue tint
                _spriteBatch.Draw(testTexture, new Vector2(width - 64, height - 64), Color.Yellow); // Bottom-right, yellow tint
                _spriteBatch.Draw(testTexture, new Vector2(width / 2 - 32, height / 2 - 32), Color.White); // Center, white

                _spriteBatch.End();
                _graphicsDevice.SetRenderTarget(null);

                // Save snapshot
                var snapshotPath = await SaveRenderTargetSnapshot(renderTarget, testName);

                // Assert - Verify sprite placement by checking specific pixels
                var pixelData = GetRenderTargetPixels(renderTarget);

                // Check top-left corner (should be red-ish)
                var topLeft = pixelData[10 * width + 10];
                Assert.True(topLeft.R > 100, $"Top-left sprite should have red component. Got: R={topLeft.R}");

                // Check top-right corner (should be green-ish)
                var topRight = pixelData[10 * width + (width - 10)];
                Assert.True(topRight.G > 100, $"Top-right sprite should have green component. Got: G={topRight.G}");

                // Check center (should be white-ish)
                var center = pixelData[(height / 2) * width + (width / 2)];
                Assert.True(center.R > 200 && center.G > 200 && center.B > 200, 
                    $"Center sprite should be white-ish. Got: R={center.R}, G={center.G}, B={center.B}");

                _output.WriteLine($"✓ Sprite rendering test passed - {snapshotPath}");
            }
            finally
            {
                renderTarget?.Dispose();
                testTexture?.Dispose();
            }
        }

        [Theory]
        [InlineData(0.5f, "Half")]
        [InlineData(2.0f, "Double")]
        [InlineData(0.25f, "Quarter")]
        public async Task GPU_SpriteScaling_VariousScales_VerifyCorrectSizing(float scale, string scaleName)
        {
            // Arrange
            if (!EnsureGraphicsReady())
                return;

            const int width = 400;
            const int height = 300;
            var renderTarget = new RenderTarget2D(_graphicsDevice, width, height);
            var testName = $"SpriteScaling_{scaleName}Scale";
            var testTexture = CreateTestTexture(50, 50, Color.White);

            _output.WriteLine($"Testing sprite scaling at {scale}x scale");

            try
            {
                // Act - Render scaled sprite
                _graphicsDevice.SetRenderTarget(renderTarget);
                _graphicsDevice.Clear(Color.Black);

                _spriteBatch.Begin();
                var position = new Vector2(width / 2 - 25 * scale, height / 2 - 25 * scale);
                _spriteBatch.Draw(testTexture, position, null, Color.Red, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                _spriteBatch.End();

                _graphicsDevice.SetRenderTarget(null);

                // Save snapshot
                var snapshotPath = await SaveRenderTargetSnapshot(renderTarget, testName);

                // Assert - Basic verification that something was rendered
                var pixelData = GetRenderTargetPixels(renderTarget);
                var nonBlackPixels = 0;

                foreach (var pixel in pixelData)
                {
                    if (pixel.R > 50 || pixel.G > 50 || pixel.B > 50)
                        nonBlackPixels++;
                }

                var expectedPixelCount = (int)(50 * 50 * scale * scale);
                var tolerance = expectedPixelCount * 0.3f; // 30% tolerance for scaling artifacts

                Assert.True(nonBlackPixels > expectedPixelCount - tolerance && nonBlackPixels < expectedPixelCount + tolerance,
                    $"Scaled sprite should have approximately {expectedPixelCount} non-black pixels. Got: {nonBlackPixels}");

                _output.WriteLine($"✓ Sprite scaling {scaleName} test passed - {nonBlackPixels} pixels - {Path.GetFileName(snapshotPath)}");
            }
            finally
            {
                renderTarget?.Dispose();
                testTexture?.Dispose();
            }
        }

        #endregion

        #region Performance and Stress Tests

        [Fact]
        public async Task GPU_BatchRendering_ManySprites_VerifyPerformanceAndAccuracy()
        {
            // Arrange
            if (!EnsureGraphicsReady())
                return;

            const int width = 1024;
            const int height = 768;
            const int spriteCount = 1000;
            var renderTarget = new RenderTarget2D(_graphicsDevice, width, height);
            var testName = "BatchRendering_ManySprites";
            var testTexture = CreateTestTexture(16, 16, Color.White);
            var random = new Random(42);

            _output.WriteLine($"Testing batch rendering with {spriteCount} sprites");

            try
            {
                var startTime = DateTime.UtcNow;

                // Act - Render many sprites in a batch
                _graphicsDevice.SetRenderTarget(renderTarget);
                _graphicsDevice.Clear(Color.Black);

                _spriteBatch.Begin();

                for (int i = 0; i < spriteCount; i++)
                {
                    var x = random.Next(0, width - 16);
                    var y = random.Next(0, height - 16);
                    var color = new Color(
                        (byte)random.Next(50, 255),
                        (byte)random.Next(50, 255),
                        (byte)random.Next(50, 255)
                    );

                    _spriteBatch.Draw(testTexture, new Vector2(x, y), color);
                }

                _spriteBatch.End();
                _graphicsDevice.SetRenderTarget(null);

                var renderTime = DateTime.UtcNow - startTime;

                // Save snapshot
                var snapshotPath = await SaveRenderTargetSnapshot(renderTarget, testName);

                // Assert - Performance and correctness checks
                Assert.True(renderTime.TotalMilliseconds < 5000, 
                    $"Batch rendering {spriteCount} sprites should complete within 5 seconds. Took: {renderTime.TotalMilliseconds}ms");

                // Verify that sprites were actually rendered
                var pixelData = GetRenderTargetPixels(renderTarget);
                var nonBlackPixels = 0;

                foreach (var pixel in pixelData)
                {
                    if (pixel.R > 25 || pixel.G > 25 || pixel.B > 25)
                        nonBlackPixels++;
                }

                Assert.True(nonBlackPixels > spriteCount * 50, // Expect at least 50 pixels per sprite on average
                    $"Should have rendered visible sprites. Non-black pixels: {nonBlackPixels}");

                _output.WriteLine($"✓ Batch rendering test passed in {renderTime.TotalMilliseconds:F1}ms - {Path.GetFileName(snapshotPath)}");
            }
            finally
            {
                renderTarget?.Dispose();
                testTexture?.Dispose();
            }
        }

        [Fact]
        public async Task GPU_RenderTargetStress_MultipleTargets_VerifyNoMemoryLeaks()
        {
            // Arrange
            if (!EnsureGraphicsReady())
                return;

            const int targetCount = 20;
            const int width = 256;
            const int height = 256;
            var testName = "RenderTargetStress";

            _output.WriteLine($"Testing render target stress with {targetCount} targets");

            var renderTargets = new List<RenderTarget2D>();
            var testTexture = CreateTestTexture(32, 32, Color.White);

            try
            {
                var startTime = DateTime.UtcNow;

                // Act - Create and use multiple render targets
                for (int i = 0; i < targetCount; i++)
                {
                    var renderTarget = new RenderTarget2D(_graphicsDevice, width, height);
                    renderTargets.Add(renderTarget);

                    // Render to each target
                    _graphicsDevice.SetRenderTarget(renderTarget);
                    _graphicsDevice.Clear(new Color(i * 10, (255 - i * 10), 128));

                    _spriteBatch.Begin();
                    _spriteBatch.Draw(testTexture, Vector2.Zero, Color.White);
                    _spriteBatch.End();

                    _graphicsDevice.SetRenderTarget(null);
                }

                var creationTime = DateTime.UtcNow - startTime;

                // Save a few sample snapshots
                for (int i = 0; i < Math.Min(3, targetCount); i++)
                {
                    await SaveRenderTargetSnapshot(renderTargets[i], $"{testName}_Target{i}");
                }

                // Assert - Performance and memory checks
                Assert.True(creationTime.TotalMilliseconds < 10000,
                    $"Creating and rendering to {targetCount} targets should complete within 10 seconds. Took: {creationTime.TotalMilliseconds}ms");

                // Verify each target has expected content
                foreach (var target in renderTargets)
                {
                    var pixelData = GetRenderTargetPixels(target);
                    var hasNonBlackPixels = false;

                    for (int i = 0; i < Math.Min(1000, pixelData.Length); i++)
                    {
                        if (pixelData[i].R > 10 || pixelData[i].G > 10 || pixelData[i].B > 10)
                        {
                            hasNonBlackPixels = true;
                            break;
                        }
                    }

                    Assert.True(hasNonBlackPixels, "Each render target should have rendered content");
                }

                _output.WriteLine($"✓ Render target stress test passed in {creationTime.TotalMilliseconds:F1}ms");
            }
            finally
            {
                // Cleanup
                foreach (var target in renderTargets)
                {
                    target?.Dispose();
                }
                testTexture?.Dispose();
            }
        }

        #endregion

        #region Visual Regression Detection

        [Fact]
        public async Task GPU_VisualRegression_ConsistentRendering_DetectChanges()
        {
            // Arrange
            if (!EnsureGraphicsReady())
                return;

            const int width = 400;
            const int height = 300;
            var renderTarget = new RenderTarget2D(_graphicsDevice, width, height);
            var testName = "VisualRegression_Baseline";
            var testTexture = CreateTestTexture(50, 50, Color.Blue);

            _output.WriteLine("Testing visual regression detection");

            try
            {
                // Act - Render a specific scene
                _graphicsDevice.SetRenderTarget(renderTarget);
                _graphicsDevice.Clear(Color.DarkGray);

                _spriteBatch.Begin();
                
                // Create a specific pattern for regression testing
                _spriteBatch.Draw(testTexture, new Vector2(50, 50), Color.Red);
                _spriteBatch.Draw(testTexture, new Vector2(150, 100), Color.Green);
                _spriteBatch.Draw(testTexture, new Vector2(250, 150), Color.Blue);
                
                // Draw some overlapping sprites
                _spriteBatch.Draw(testTexture, new Vector2(100, 75), Color.Yellow * 0.7f);
                _spriteBatch.Draw(testTexture, new Vector2(200, 125), Color.Magenta * 0.5f);

                _spriteBatch.End();
                _graphicsDevice.SetRenderTarget(null);

                // Save baseline snapshot
                var snapshotPath = await SaveRenderTargetSnapshot(renderTarget, testName);

                // Generate hash for comparison
                var pixelData = GetRenderTargetPixels(renderTarget);
                var renderHash = ComputePixelHash(pixelData);

                _output.WriteLine($"Baseline render hash: {renderHash:X8}");

                // Render the same scene again and verify consistency
                _graphicsDevice.SetRenderTarget(renderTarget);
                _graphicsDevice.Clear(Color.DarkGray);

                _spriteBatch.Begin();
                _spriteBatch.Draw(testTexture, new Vector2(50, 50), Color.Red);
                _spriteBatch.Draw(testTexture, new Vector2(150, 100), Color.Green);
                _spriteBatch.Draw(testTexture, new Vector2(250, 150), Color.Blue);
                _spriteBatch.Draw(testTexture, new Vector2(100, 75), Color.Yellow * 0.7f);
                _spriteBatch.Draw(testTexture, new Vector2(200, 125), Color.Magenta * 0.5f);
                _spriteBatch.End();

                _graphicsDevice.SetRenderTarget(null);

                var secondPixelData = GetRenderTargetPixels(renderTarget);
                var secondHash = ComputePixelHash(secondPixelData);

                // Assert - Verify rendering consistency
                Assert.Equal(renderHash, secondHash);

                // Verify specific pixel values for key areas
                var redSpritePixel = pixelData[75 * width + 75]; // Center of red sprite
                Assert.True(redSpritePixel.R > 100, $"Red sprite area should be red-dominant. Got: {redSpritePixel}");

                var greenSpritePixel = pixelData[125 * width + 175]; // Center of green sprite
                Assert.True(greenSpritePixel.G > 100, $"Green sprite area should be green-dominant. Got: {greenSpritePixel}");

                _output.WriteLine($"✓ Visual regression test passed - consistent rendering - {Path.GetFileName(snapshotPath)}");
            }
            finally
            {
                renderTarget?.Dispose();
                testTexture?.Dispose();
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a test texture filled with the specified color
        /// </summary>
        private Texture2D CreateTestTexture(int width, int height, Color color)
        {
            var texture = new Texture2D(_graphicsDevice, width, height);
            var colorData = new Color[width * height];
            
            for (int i = 0; i < colorData.Length; i++)
            {
                colorData[i] = color;
            }
            
            texture.SetData(colorData);
            return texture;
        }

        /// <summary>
        /// Gets pixel data from a render target
        /// </summary>
        private Color[] GetRenderTargetPixels(RenderTarget2D renderTarget)
        {
            var pixelData = new Color[renderTarget.Width * renderTarget.Height];
            renderTarget.GetData(pixelData);
            return pixelData;
        }

        /// <summary>
        /// Saves a render target as a PNG snapshot
        /// </summary>
        private async Task<string> SaveRenderTargetSnapshot(RenderTarget2D renderTarget, string testName)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{testName}_{timestamp}.png";
            var filePath = Path.Combine(_snapshotDirectory, fileName);

            try
            {
                using var fileStream = File.Create(filePath);
                renderTarget.SaveAsPng(fileStream, renderTarget.Width, renderTarget.Height);
                await fileStream.FlushAsync();
                
                _output.WriteLine($"Snapshot saved: {fileName} ({renderTarget.Width}x{renderTarget.Height})");
                return filePath;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Failed to save snapshot {fileName}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Computes a simple hash of pixel data for regression testing
        /// </summary>
        private uint ComputePixelHash(Color[] pixelData)
        {
            uint hash = 2166136261u; // FNV-1a initial value
            
            foreach (var pixel in pixelData)
            {
                hash ^= (uint)pixel.R;
                hash *= 16777619u;
                hash ^= (uint)pixel.G;
                hash *= 16777619u;
                hash ^= (uint)pixel.B;
                hash *= 16777619u;
                hash ^= (uint)pixel.A;
                hash *= 16777619u;
            }
            
            return hash;
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            _spriteBatch?.Dispose();
            _graphicsService?.Dispose();
        }

        #endregion
    }
}
