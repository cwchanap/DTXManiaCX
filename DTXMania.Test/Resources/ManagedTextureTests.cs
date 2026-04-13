using DTXMania.Game.Lib.Resources;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Basic unit tests for ManagedTexture interfaces and data structures
    /// Tests core functionality without MonoGame dependencies
    /// </summary>
    [Trait("Category", "Unit")]
    public class ManagedTextureTests : IDisposable
    {
        private sealed record VectorDrawCall(Vector2 Position, Color Color, Rectangle? SourceRectangle);
        private sealed record RectangleDrawCall(Rectangle Destination, Rectangle? SourceRectangle, Color Color, float Rotation, Vector2 Origin, SpriteEffects Effects, float LayerDepth);
        private sealed record TransformDrawCall(Vector2 Position, Color Color, float Rotation, Vector2 Origin, Vector2 Scale, SpriteEffects Effects, float LayerDepth);

        private sealed class ConstructorProbeManagedTexture : ManagedTexture
        {
            public static int WidthOverride { get; set; } = 2;
            public static int HeightOverride { get; set; } = 2;
            public static Color[] CurrentColorData { get; set; } = Array.Empty<Color>();
            public static List<Color[]> SetColorDataCalls { get; } = new();
            public static Exception? LoadException { get; set; }

            public ConstructorProbeManagedTexture(GraphicsDevice graphicsDevice, string filePath, string sourcePath, TextureCreationParams? creationParams = null)
                : base(graphicsDevice, filePath, sourcePath, creationParams)
            {
            }

            public static void Reset(Color[]? initialColorData = null, Exception? loadException = null)
            {
                WidthOverride = 2;
                HeightOverride = 2;
                CurrentColorData = initialColorData?.ToArray() ?? Array.Empty<Color>();
                SetColorDataCalls.Clear();
                LoadException = loadException;
            }

            protected override int GetTextureWidthCore()
            {
                return WidthOverride;
            }

            protected override int GetTextureHeightCore()
            {
                return HeightOverride;
            }

            protected override Stream OpenReadCore(string filePath)
            {
                return new MemoryStream(new byte[] { 1, 2, 3, 4 });
            }

            protected override Texture2D LoadTextureFromStreamCore(GraphicsDevice graphicsDevice, Stream stream)
            {
                if (LoadException != null)
                {
                    throw LoadException;
                }

                return (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            }

            protected override void GetTextureDataCore(Texture2D texture, Color[] data)
            {
                Array.Copy(CurrentColorData, data, Math.Min(CurrentColorData.Length, data.Length));
            }

            protected override void SetTextureDataCore(Texture2D texture, Color[] data)
            {
                CurrentColorData = data.ToArray();
                SetColorDataCalls.Add(data.ToArray());
            }
        }

        private sealed class TestableManagedTexture : ManagedTexture
        {
            public List<VectorDrawCall> VectorDrawCalls { get; } = new();
            public List<RectangleDrawCall> RectangleDrawCalls { get; } = new();
            public List<TransformDrawCall> TransformDrawCalls { get; } = new();
            public List<Color[]> SetColorDataCalls { get; } = new();
            public Color[] ColorDataToReturn { get; set; } = Array.Empty<Color>();
            public int WidthOverride { get; set; } = 2;
            public int HeightOverride { get; set; } = 2;
            public GraphicsDevice GraphicsDeviceOverride { get; set; } = CreateGraphicsDeviceStub();
            public Texture2D CreatedTexture { get; set; } = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            public string? CreatedFilePath { get; private set; }
            public (int Width, int Height)? SavedSize { get; private set; }
            public bool ThrowOnSave { get; set; }
            public bool ThrowOnCreateFile { get; set; }

            public TestableManagedTexture(Texture2D texture, string sourcePath)
                : base(null!, texture, sourcePath)
            {
            }

            protected override int GetTextureWidthCore()
            {
                return WidthOverride;
            }

            protected override int GetTextureHeightCore()
            {
                return HeightOverride;
            }

            protected override GraphicsDevice GetGraphicsDeviceCore(Texture2D texture)
            {
                return GraphicsDeviceOverride;
            }

            protected override void DrawTextureCore(SpriteBatch spriteBatch, Vector2 position, Color color)
            {
                VectorDrawCalls.Add(new VectorDrawCall(position, color, null));
            }

            protected override void DrawTextureCore(SpriteBatch spriteBatch, Vector2 position, Rectangle? sourceRectangle, Color color)
            {
                VectorDrawCalls.Add(new VectorDrawCall(position, color, sourceRectangle));
            }

            protected override void DrawTextureCore(SpriteBatch spriteBatch, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, SpriteEffects effects, float layerDepth)
            {
                RectangleDrawCalls.Add(new RectangleDrawCall(destinationRectangle, sourceRectangle, color, rotation, origin, effects, layerDepth));
            }

            protected override void DrawTextureCore(SpriteBatch spriteBatch, Vector2 position, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth)
            {
                TransformDrawCalls.Add(new TransformDrawCall(position, color, rotation, origin, scale, effects, layerDepth));
            }

            protected override Texture2D CreateTextureCore(GraphicsDevice graphicsDevice, int width, int height)
            {
                return CreatedTexture;
            }

            protected override void GetTextureDataCore(Texture2D texture, Color[] data)
            {
                Array.Copy(ColorDataToReturn, data, Math.Min(ColorDataToReturn.Length, data.Length));
            }

            protected override void SetTextureDataCore(Texture2D texture, Color[] data)
            {
                SetColorDataCalls.Add(data.ToArray());
            }

            protected override Stream CreateFileCore(string filePath)
            {
                if (ThrowOnCreateFile)
                {
                    throw new IOException("create failed");
                }

                CreatedFilePath = filePath;
                return new MemoryStream();
            }

            protected override void SaveTextureAsPngCore(Texture2D texture, Stream stream, int width, int height)
            {
                if (ThrowOnSave)
                {
                    throw new IOException("save failed");
                }

                SavedSize = (width, height);
            }
        }

        private sealed class BaseHookManagedTexture : ManagedTexture
        {
            public BaseHookManagedTexture(Texture2D texture, string sourcePath)
                : base(null!, texture, sourcePath)
            {
            }

            public GraphicsDevice CallBaseGetGraphicsDevice(Texture2D texture)
            {
                return base.GetGraphicsDeviceCore(texture);
            }

            public void CallBaseDraw(SpriteBatch spriteBatch, Vector2 position, Color color)
            {
                base.DrawTextureCore(spriteBatch, position, color);
            }

            public void CallBaseDraw(SpriteBatch spriteBatch, Vector2 position, Rectangle? sourceRectangle, Color color)
            {
                base.DrawTextureCore(spriteBatch, position, sourceRectangle, color);
            }

            public void CallBaseDraw(SpriteBatch spriteBatch, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, SpriteEffects effects, float layerDepth)
            {
                base.DrawTextureCore(spriteBatch, destinationRectangle, sourceRectangle, color, rotation, origin, effects, layerDepth);
            }

            public void CallBaseDraw(SpriteBatch spriteBatch, Vector2 position, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth)
            {
                base.DrawTextureCore(spriteBatch, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
            }

            public Texture2D CallBaseCreateTexture(GraphicsDevice graphicsDevice, int width, int height)
            {
                return base.CreateTextureCore(graphicsDevice, width, height);
            }

            public void CallBaseGetTextureData(Texture2D texture, Color[] data)
            {
                base.GetTextureDataCore(texture, data);
            }

            public void CallBaseSetTextureData(Texture2D texture, Color[] data)
            {
                base.SetTextureDataCore(texture, data);
            }

            public Stream CallBaseOpenRead(string filePath)
            {
                return base.OpenReadCore(filePath);
            }

            public Stream CallBaseCreateFile(string filePath)
            {
                return base.CreateFileCore(filePath);
            }

            public Texture2D CallBaseLoadTextureFromStream(GraphicsDevice graphicsDevice, Stream stream)
            {
                return base.LoadTextureFromStreamCore(graphicsDevice, stream);
            }

            public void CallBaseSaveTextureAsPng(Texture2D texture, Stream stream, int width, int height)
            {
                base.SaveTextureAsPngCore(texture, stream, width, height);
            }
        }

        private readonly string _testImagePath;

        public ManagedTextureTests()
        {
            // Create a temporary test image file
            _testImagePath = Path.GetTempFileName();
            File.WriteAllBytes(_testImagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header
        }

        [Fact]
        public void TextureCreationParams_ShouldHaveCorrectDefaults()
        {
            // Arrange & Act
            var params1 = new TextureCreationParams();

            // Assert
            Assert.False(params1.EnableTransparency);
            Assert.Equal(Color.Black, params1.TransparencyColor);
            Assert.False(params1.GenerateMipmaps);
            Assert.True(params1.PremultiplyAlpha);
        }

        [Fact]
        public void TextureCreationParams_ShouldAllowCustomization()
        {
            // Arrange & Act
            var params1 = new TextureCreationParams
            {
                EnableTransparency = true,
                TransparencyColor = Color.Magenta,
                GenerateMipmaps = true,
                PremultiplyAlpha = false
            };

            // Assert
            Assert.True(params1.EnableTransparency);
            Assert.Equal(Color.Magenta, params1.TransparencyColor);
            Assert.True(params1.GenerateMipmaps);
            Assert.False(params1.PremultiplyAlpha);
        }

        [Fact]
        public void TextureLoadException_ShouldInitializeCorrectly()
        {
            // Arrange
            var texturePath = "Graphics/test.png";
            var message = "Failed to load texture";
            var innerException = new FileNotFoundException("File not found");

            // Act
            var exception1 = new TextureLoadException(texturePath, message);
            var exception2 = new TextureLoadException(texturePath, message, innerException);

            // Assert
            Assert.Equal(texturePath, exception1.TexturePath);
            Assert.Equal(message, exception1.Message);
            Assert.Null(exception1.InnerException);

            Assert.Equal(texturePath, exception2.TexturePath);
            Assert.Equal(message, exception2.Message);
            Assert.Equal(innerException, exception2.InnerException);
        }

        [Fact]
        public void FilePathConstructor_WhenGraphicsDeviceIsNull_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ManagedTexture(null!, "texture.png", "source"));
        }

        [Fact]
        public void FilePathConstructor_WhenFilePathIsNullOrEmpty_ShouldThrowArgumentException()
        {
            var graphicsDevice = CreateGraphicsDeviceStub();

            Assert.Throws<ArgumentException>(() => new ManagedTexture(graphicsDevice, (string)null!, "source"));
            Assert.Throws<ArgumentException>(() => new ManagedTexture(graphicsDevice, string.Empty, "source"));
        }

        [Fact]
        public void ExistingTextureConstructor_WhenSourcePathIsNull_ShouldFallbackToUnknown()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));

            var managedTexture = new ManagedTexture(null!, texture, null!);

            Assert.Same(texture, managedTexture.Texture);
            Assert.Equal("Unknown", managedTexture.SourcePath);
        }

        [Fact]
        public void ExistingTextureConstructor_WhenTextureIsNull_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ManagedTexture(null!, (Texture2D)null!, "source"));
        }

        [Fact]
        public void AddReferenceAndRemoveReference_ShouldTrackReferenceCountWithoutDisposing()
        {
            var managedTexture = CreateManagedTexture();

            managedTexture.AddReference();
            managedTexture.AddReference();
            managedTexture.RemoveReference();

            Assert.Equal(1, managedTexture.ReferenceCount);
            Assert.False(managedTexture.IsDisposed);
        }

        [Fact]
        public void AddReference_WhenDisposed_ShouldThrowObjectDisposedException()
        {
            var managedTexture = CreateManagedTexture(disposed: true);

            Assert.Throws<ObjectDisposedException>(() => managedTexture.AddReference());
        }

        [Fact]
        public void Properties_WhenTextureIsMissing_ShouldUseSafeDefaults()
        {
            var managedTexture = CreateManagedTexture();

            managedTexture.ZAxisRotation = 1.25f;
            managedTexture.ScaleRatio = new Vector3(2f, 3f, 1f);
            managedTexture.AdditiveBlending = true;

            Assert.Equal(0, managedTexture.Width);
            Assert.Equal(0, managedTexture.Height);
            Assert.Equal(Vector2.Zero, managedTexture.Size);
            Assert.Equal(0L, managedTexture.MemoryUsage);
            Assert.Equal(1.25f, managedTexture.ZAxisRotation);
            Assert.Equal(new Vector3(2f, 3f, 1f), managedTexture.ScaleRatio);
            Assert.True(managedTexture.AdditiveBlending);
        }

        [Fact]
        public void Properties_WhenTextureDimensionsAreAvailable_ShouldExposeSizeAndMemoryUsage()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var managedTexture = new TestableManagedTexture(texture, "SizedTexture")
            {
                WidthOverride = 3,
                HeightOverride = 4
            };

            Assert.Equal(3, managedTexture.Width);
            Assert.Equal(4, managedTexture.Height);
            Assert.Equal(new Vector2(3, 4), managedTexture.Size);
            Assert.Equal(48L, managedTexture.MemoryUsage);
        }

        [Theory]
        [InlineData(999, 255)]
        [InlineData(-5, 0)]
        [InlineData(0, 0)]
        [InlineData(255, 255)]
        [InlineData(128, 128)]
        public void Transparency_ShouldClampToByteRange(int input, int expected)
        {
            var managedTexture = CreateManagedTexture();

            managedTexture.Transparency = input;

            Assert.Equal(expected, managedTexture.Transparency);
        }

        [Fact]
        public void DrawOverloads_WhenTextureIsMissing_ShouldReturnWithoutThrowing()
        {
            var managedTexture = CreateManagedTexture();

            var exception = Record.Exception(() =>
            {
                managedTexture.Draw(null!, Vector2.Zero);
                managedTexture.Draw(null!, Vector2.Zero, null);
                managedTexture.Draw(null!, new Rectangle(0, 0, 10, 10), null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
                managedTexture.Draw(null!, Vector2.One, Vector2.One, 0f, Vector2.Zero);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void DrawOverloads_WhenTextureExists_ShouldUseConfiguredColorsAndTransforms()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var managedTexture = new TestableManagedTexture(texture, "DrawableTexture")
            {
                Transparency = 128,
                ScaleRatio = new Vector3(2f, 3f, 1f),
                ZAxisRotation = 0.5f
            };
            var spriteBatch = (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));
            var sourceRectangle = new Rectangle(1, 2, 3, 4);

            managedTexture.Draw(spriteBatch, new Vector2(10f, 20f));
            managedTexture.Draw(spriteBatch, new Vector2(30f, 40f), sourceRectangle);
            managedTexture.Draw(spriteBatch, new Rectangle(5, 6, 7, 8), sourceRectangle, Color.Blue, 0.25f, new Vector2(1f, 2f), SpriteEffects.FlipHorizontally, 0.75f);
            managedTexture.Draw(spriteBatch, new Vector2(50f, 60f), new Vector2(4f, 5f), 0.75f, new Vector2(2f, 3f));

            Assert.Equal(2, managedTexture.VectorDrawCalls.Count);
            Assert.Equal(Color.White * (128 / 255f), managedTexture.VectorDrawCalls[0].Color);
            Assert.Equal(sourceRectangle, managedTexture.VectorDrawCalls[1].SourceRectangle);

            var rectangleDraw = Assert.Single(managedTexture.RectangleDrawCalls);
            Assert.Equal(Color.Blue * (128 / 255f), rectangleDraw.Color);
            Assert.Equal(0.25f, rectangleDraw.Rotation);
            Assert.Equal(SpriteEffects.FlipHorizontally, rectangleDraw.Effects);
            Assert.Equal(0.75f, rectangleDraw.LayerDepth);

            var transformDraw = Assert.Single(managedTexture.TransformDrawCalls);
            Assert.Equal(Color.White * (128 / 255f), transformDraw.Color);
            Assert.Equal(1.25f, transformDraw.Rotation);
            Assert.Equal(new Vector2(8f, 15f), transformDraw.Scale);
            Assert.Equal(SpriteEffects.None, transformDraw.Effects);
            Assert.Equal(0f, transformDraw.LayerDepth);
        }

        [Fact]
        public void CloneAndColorOperations_WhenTextureIsMissing_ShouldThrowObjectDisposedException()
        {
            var managedTexture = CreateManagedTexture();
            var tempPath = Path.Combine(Path.GetTempPath(), "texture.png");

            Assert.Throws<ObjectDisposedException>(() => managedTexture.Clone());
            Assert.Throws<ObjectDisposedException>(() => managedTexture.GetColorData());
            Assert.Throws<ObjectDisposedException>(() => managedTexture.SetColorData(Array.Empty<Color>()));
            try
            {
                Assert.Throws<ObjectDisposedException>(() => managedTexture.SaveToFile(tempPath));
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        [Fact]
        public void Clone_ShouldCopyColorDataAndVisualProperties()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var createdTexture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var managedTexture = new TestableManagedTexture(texture, "CloneSource")
            {
                WidthOverride = 2,
                HeightOverride = 2,
                ColorDataToReturn = new[] { Color.Red, Color.Green, Color.Blue, Color.White },
                CreatedTexture = createdTexture,
                Transparency = 200,
                ScaleRatio = new Vector3(1.5f, 2.5f, 1f),
                ZAxisRotation = 0.75f,
                AdditiveBlending = true
            };

            var clone = (ManagedTexture)managedTexture.Clone();

            Assert.Single(managedTexture.SetColorDataCalls);
            Assert.Equal(managedTexture.ColorDataToReturn, managedTexture.SetColorDataCalls[0]);
            Assert.Same(createdTexture, clone.Texture);
            Assert.Equal("CloneSource_clone", clone.SourcePath);
            Assert.Equal(200, clone.Transparency);
            Assert.Equal(new Vector3(1.5f, 2.5f, 1f), clone.ScaleRatio);
            Assert.Equal(0.75f, clone.ZAxisRotation);
            Assert.True(clone.AdditiveBlending);
        }

        [Fact]
        public void GetAndSetColorData_ShouldUseTextureAccessors()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var managedTexture = new TestableManagedTexture(texture, "ColorTexture")
            {
                WidthOverride = 2,
                HeightOverride = 2,
                ColorDataToReturn = new[] { Color.Red, Color.Green, Color.Blue, Color.White }
            };
            var newData = new[] { Color.Black, Color.Yellow, Color.Magenta, Color.Cyan };

            var colorData = managedTexture.GetColorData();
            managedTexture.SetColorData(newData);

            Assert.Equal(managedTexture.ColorDataToReturn, colorData);
            Assert.Single(managedTexture.SetColorDataCalls);
            Assert.Equal(newData, managedTexture.SetColorDataCalls[0]);
        }

        [Fact]
        public void SetColorData_WhenArrayLengthDoesNotMatchTextureDimensions_ShouldThrowArgumentException()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var managedTexture = new TestableManagedTexture(texture, "ColorTexture")
            {
                WidthOverride = 2,
                HeightOverride = 2
            };

            Assert.Throws<ArgumentException>(() => managedTexture.SetColorData(new[] { Color.Red }));
        }

        [Fact]
        public void SaveToFile_WhenSuccessful_ShouldCreateFileAndSaveTexture()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var managedTexture = new TestableManagedTexture(texture, "SaveTexture")
            {
                WidthOverride = 3,
                HeightOverride = 4
            };

            managedTexture.SaveToFile("save-target.png");

            Assert.Equal("save-target.png", managedTexture.CreatedFilePath);
            Assert.Equal((3, 4), managedTexture.SavedSize);
        }

        [Fact]
        public void SaveToFile_WhenSavingFails_ShouldWrapInvalidOperationException()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var managedTexture = new TestableManagedTexture(texture, "SaveTexture")
            {
                ThrowOnSave = true
            };

            var exception = Assert.Throws<InvalidOperationException>(() => managedTexture.SaveToFile("save-target.png"));

            Assert.Equal("Failed to save texture to save-target.png", exception.Message);
            Assert.IsType<IOException>(exception.InnerException);
        }

        [Fact]
        public void SaveToFile_WhenFilePathIsNullOrEmpty_ShouldThrowArgumentException()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var managedTexture = new TestableManagedTexture(texture, "SaveTexture");

            Assert.Throws<ArgumentException>(() => managedTexture.SaveToFile(null!));
            Assert.Throws<ArgumentException>(() => managedTexture.SaveToFile(string.Empty));
        }

        [Fact]
        public void FilePathConstructor_WhenCreationParamsRequestTransparencyAndPremultiplication_ShouldProcessColorData()
        {
            ConstructorProbeManagedTexture.Reset(new[]
            {
                Color.Magenta,
                new Color(100, 50, 200, 128),
                new Color(10, 20, 30, 255),
                new Color(5, 5, 5, 0)
            });
            var graphicsDevice = CreateGraphicsDeviceStub();

            var managedTexture = new ConstructorProbeManagedTexture(
                graphicsDevice,
                _testImagePath,
                "ConstructorSource",
                new TextureCreationParams
                {
                    EnableTransparency = true,
                    TransparencyColor = Color.Magenta,
                    PremultiplyAlpha = true
                });

            Assert.Equal("ConstructorSource", managedTexture.SourcePath);
            Assert.Equal(2, ConstructorProbeManagedTexture.SetColorDataCalls.Count);
            Assert.Equal(Color.Transparent, ConstructorProbeManagedTexture.SetColorDataCalls[0][0]);
            Assert.Equal(new Color(50, 25, 100, 128), ConstructorProbeManagedTexture.SetColorDataCalls[1][1]);
            Assert.Equal(new Color(0, 0, 0, 0), ConstructorProbeManagedTexture.SetColorDataCalls[1][3]);
        }

        [Fact]
        public void FilePathConstructor_WhenTextureLoadFails_ShouldWrapTextureLoadException()
        {
            ConstructorProbeManagedTexture.Reset(loadException: new InvalidOperationException("load failed"));
            var graphicsDevice = CreateGraphicsDeviceStub();

            var exception = Assert.Throws<TextureLoadException>(() =>
                new ConstructorProbeManagedTexture(graphicsDevice, _testImagePath, "BadTexture"));

            Assert.Equal("BadTexture", exception.TexturePath);
            Assert.Equal("Failed to load texture from " + _testImagePath, exception.Message);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        [Fact]
        public void BaseHooks_ShouldReachUnderlyingMonoGameDelegates()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            var managedTexture = new BaseHookManagedTexture(texture, "BaseHooks");
            var tempOutputPath = Path.Combine(Path.GetTempPath(), $"managed-texture-base-{Guid.NewGuid():N}.tmp");

            try
            {
                Assert.NotNull(Record.Exception(() => managedTexture.CallBaseGetGraphicsDevice(null!)));
                Assert.NotNull(Record.Exception(() => managedTexture.CallBaseDraw(null!, Vector2.Zero, Color.White)));
                Assert.NotNull(Record.Exception(() => managedTexture.CallBaseDraw(null!, Vector2.Zero, null, Color.White)));
                Assert.NotNull(Record.Exception(() => managedTexture.CallBaseDraw(null!, new Rectangle(0, 0, 1, 1), null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f)));
                Assert.NotNull(Record.Exception(() => managedTexture.CallBaseDraw(null!, Vector2.Zero, null, Color.White, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0f)));
                Assert.NotNull(Record.Exception(() => managedTexture.CallBaseCreateTexture(null!, 1, 1)));
                Assert.NotNull(Record.Exception(() => managedTexture.CallBaseGetTextureData(null!, Array.Empty<Color>())));
                Assert.NotNull(Record.Exception(() => managedTexture.CallBaseSetTextureData(null!, Array.Empty<Color>())));

                using var input = managedTexture.CallBaseOpenRead(_testImagePath);
                Assert.NotNull(input);

                using var output = managedTexture.CallBaseCreateFile(tempOutputPath);
                Assert.True(File.Exists(tempOutputPath));

                Assert.NotNull(Record.Exception(() => managedTexture.CallBaseLoadTextureFromStream(null!, Stream.Null)));
                Assert.NotNull(Record.Exception(() => managedTexture.CallBaseSaveTextureAsPng(null!, Stream.Null, 1, 1)));
            }
            finally
            {
                if (File.Exists(tempOutputPath))
                {
                    File.Delete(tempOutputPath);
                }
            }
        }

        public void Dispose()
        {
            // Cleanup test file
            try
            {
                if (File.Exists(_testImagePath))
                {
                    File.Delete(_testImagePath);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }

        private static ManagedTexture CreateManagedTexture(Texture2D? texture = null, bool disposed = false)
        {
            var managedTexture = (ManagedTexture)RuntimeHelpers.GetUninitializedObject(typeof(ManagedTexture));

            ReflectionHelpers.SetPrivateField(managedTexture, "_texture", texture);
            ReflectionHelpers.SetPrivateField(managedTexture, "_sourcePath", "TestTexture");
            ReflectionHelpers.SetPrivateField(managedTexture, "_referenceCount", 0);
            ReflectionHelpers.SetPrivateField(managedTexture, "_disposed", disposed);
            ReflectionHelpers.SetPrivateField(managedTexture, "_lockObject", new object());
            ReflectionHelpers.SetPrivateField(managedTexture, "_transparency", 255);
            ReflectionHelpers.SetPrivateField(managedTexture, "_scaleRatio", Vector3.One);
            ReflectionHelpers.SetPrivateField(managedTexture, "_zAxisRotation", 0f);
            ReflectionHelpers.SetPrivateField(managedTexture, "_additiveBlending", false);

            return managedTexture;
        }

        private static GraphicsDevice CreateGraphicsDeviceStub()
        {
            return (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));
        }
    }
}
