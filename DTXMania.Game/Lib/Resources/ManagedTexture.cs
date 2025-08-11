using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Managed texture implementation with reference counting
    /// Based on DTXMania's CTexture patterns
    /// </summary>
    public class ManagedTexture : ITexture
    {
        #region Private Fields

        private Texture2D _texture;
        private readonly string _sourcePath;
        private int _referenceCount;
        private bool _disposed;
        private readonly object _lockObject = new object();

        // DTXMania-style properties
        private int _transparency = 255;
        private Vector3 _scaleRatio = Vector3.One;
        private float _zAxisRotation = 0f;
        private bool _additiveBlending = false;

        #endregion

        #region Constructors

        /// <summary>
        /// Create texture from file path
        /// </summary>
        public ManagedTexture(GraphicsDevice graphicsDevice, string filePath, string sourcePath, 
                             TextureCreationParams creationParams = null)
        {
            if (graphicsDevice == null)
                throw new ArgumentNullException(nameof(graphicsDevice));
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            _sourcePath = sourcePath ?? filePath;
            creationParams = creationParams ?? new TextureCreationParams();

            try
            {
                LoadTextureFromFile(graphicsDevice, filePath, creationParams);
            }
            catch (Exception ex)
            {
                throw new TextureLoadException(_sourcePath, $"Failed to load texture from {filePath}", ex);
            }
        }

        /// <summary>
        /// Create texture from existing Texture2D
        /// </summary>
        public ManagedTexture(GraphicsDevice graphicsDevice, Texture2D texture, string sourcePath)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));

            _texture = texture;
            _sourcePath = sourcePath ?? "Unknown";
        }

        #endregion

        #region ITexture Properties

        public Texture2D Texture => _texture;
        public string SourcePath => _sourcePath;
        public int Width => _texture?.Width ?? 0;
        public int Height => _texture?.Height ?? 0;
        public Vector2 Size => new Vector2(Width, Height);
        public bool IsDisposed => _disposed;
        public int ReferenceCount => _referenceCount;

        public long MemoryUsage
        {
            get
            {
                if (_texture == null) return 0;
                
                // Estimate memory usage: width * height * bytes per pixel
                // Assuming 4 bytes per pixel for Color format
                return Width * Height * 4;
            }
        }

        public int Transparency
        {
            get => _transparency;
            set => _transparency = Math.Clamp(value, 0, 255);
        }

        public Vector3 ScaleRatio
        {
            get => _scaleRatio;
            set => _scaleRatio = value;
        }

        public float ZAxisRotation
        {
            get => _zAxisRotation;
            set => _zAxisRotation = value;
        }

        public bool AdditiveBlending
        {
            get => _additiveBlending;
            set => _additiveBlending = value;
        }

        #endregion

        #region Reference Counting

        public void AddReference()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ManagedTexture));

            Interlocked.Increment(ref _referenceCount);
        }

        public void RemoveReference()
        {
            var newCount = Interlocked.Decrement(ref _referenceCount);
            if (newCount <= 0)
            {
                Dispose();
            }
        }

        #endregion

        #region Drawing Methods

        public void Draw(SpriteBatch spriteBatch, Vector2 position)
        {
            if (_disposed || _texture == null)
                return;

            var color = Color.White * (_transparency / 255f);
            spriteBatch.Draw(_texture, position, color);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 position, Rectangle? sourceRectangle)
        {
            if (_disposed || _texture == null)
                return;

            var color = Color.White * (_transparency / 255f);
            spriteBatch.Draw(_texture, position, sourceRectangle, color);
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle destinationRectangle, Rectangle? sourceRectangle,
                        Color color, float rotation, Vector2 origin, SpriteEffects effects, float layerDepth)
        {
            if (_disposed || _texture == null)
                return;

            var finalColor = color * (_transparency / 255f);
            spriteBatch.Draw(_texture, destinationRectangle, sourceRectangle, finalColor, 
                           rotation, origin, effects, layerDepth);
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 position, Vector2 scale, float rotation, Vector2 origin)
        {
            if (_disposed || _texture == null)
                return;

            var color = Color.White * (_transparency / 255f);
            var finalScale = scale * new Vector2(_scaleRatio.X, _scaleRatio.Y);
            var finalRotation = rotation + _zAxisRotation;

            spriteBatch.Draw(_texture, position, null, color, finalRotation, origin, finalScale, 
                           SpriteEffects.None, 0f);
        }

        #endregion

        #region Utility Methods

        public ITexture Clone()
        {
            if (_disposed || _texture == null)
                throw new ObjectDisposedException(nameof(ManagedTexture));

            // Create a new texture with the same data
            var newTexture = new Texture2D(_texture.GraphicsDevice, Width, Height);
            var colorData = new Color[Width * Height];
            _texture.GetData(colorData);
            newTexture.SetData(colorData);

            var clone = new ManagedTexture(_texture.GraphicsDevice, newTexture, _sourcePath + "_clone")
            {
                _transparency = _transparency,
                _scaleRatio = _scaleRatio,
                _zAxisRotation = _zAxisRotation,
                _additiveBlending = _additiveBlending
            };

            return clone;
        }

        public Color[] GetColorData()
        {
            if (_disposed || _texture == null)
                throw new ObjectDisposedException(nameof(ManagedTexture));

            var colorData = new Color[Width * Height];
            _texture.GetData(colorData);
            return colorData;
        }

        public void SetColorData(Color[] colorData)
        {
            if (_disposed || _texture == null)
                throw new ObjectDisposedException(nameof(ManagedTexture));

            if (colorData.Length != Width * Height)
                throw new ArgumentException("Color data array size doesn't match texture size");

            _texture.SetData(colorData);
        }

        public void SaveToFile(string filePath)
        {
            if (_disposed || _texture == null)
                throw new ObjectDisposedException(nameof(ManagedTexture));

            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            try
            {
                using (var stream = File.Create(filePath))
                {
                    _texture.SaveAsPng(stream, Width, Height);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save texture to {filePath}", ex);
            }
        }

        #endregion

        #region Private Methods

        private void LoadTextureFromFile(GraphicsDevice graphicsDevice, string filePath, TextureCreationParams creationParams)
        {
            using (var stream = File.OpenRead(filePath))
            {
                _texture = Texture2D.FromStream(graphicsDevice, stream);
            }

            // Apply transparency if requested
            if (creationParams.EnableTransparency)
            {
                ApplyTransparency(creationParams.TransparencyColor);
            }

            // Apply premultiplied alpha if requested
            if (creationParams.PremultiplyAlpha)
            {
                ApplyPremultipliedAlpha();
            }
        }

        private void ApplyTransparency(Color transparencyColor)
        {
            var colorData = GetColorData();
            
            for (int i = 0; i < colorData.Length; i++)
            {
                if (colorData[i].R == transparencyColor.R &&
                    colorData[i].G == transparencyColor.G &&
                    colorData[i].B == transparencyColor.B)
                {
                    colorData[i] = Color.Transparent;
                }
            }

            SetColorData(colorData);
        }

        private void ApplyPremultipliedAlpha()
        {
            var colorData = GetColorData();
            
            for (int i = 0; i < colorData.Length; i++)
            {
                var color = colorData[i];
                var alpha = color.A / 255f;
                
                colorData[i] = new Color(
                    (byte)(color.R * alpha),
                    (byte)(color.G * alpha),
                    (byte)(color.B * alpha),
                    color.A
                );
            }

            SetColorData(colorData);
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                lock (_lockObject)
                {
                    if (!_disposed)
                    {
                        _texture?.Dispose();
                        _texture = null;
                        _disposed = true;
                    }
                }
            }
        }

        ~ManagedTexture()
        {
            if (!_disposed)
            {
                Debug.WriteLine($"ManagedTexture: Dispose leak detected for texture: {_sourcePath}");
            }
            Dispose(false);
        }

        #endregion
    }
}
