using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Test.Helpers
{
    /// <summary>
    /// Mutable test double implementing <see cref="ITexture"/> with explicit, overridable
    /// property values. Centralizes the underlying <see cref="Texture2D"/> stub creation so
    /// individual tests do not depend on <see cref="Texture2D"/> internals directly.
    /// </summary>
    public class MutableTexture : ITexture
    {
        public Texture2D Texture { get; set; } = CreateTexture2DStub();
        public string SourcePath => TexturePath.JudgeStringsXg;
        public int Width
        {
            get
            {
                if (ThrowOnWidthGet)
                    throw new InvalidOperationException("Width getter failed");
                return _width;
            }
            set => _width = value;
        }
        public int Height { get; set; } = 256;
        public Vector2 Size => new Vector2(Width, Height);
        public bool IsDisposed { get; set; }
        public int ReferenceCount => 1;
        public long MemoryUsage => Width * Height * 4L;
        public int Transparency { get; set; } = 255;
        public Vector3 ScaleRatio { get; set; } = Vector3.One;
        public float ZAxisRotation { get; set; }
        public bool AdditiveBlending { get; set; }
        public int RemoveReferenceCount { get; private set; }
        public bool ThrowOnRemoveReference { get; set; }
        public bool ThrowOnWidthGet { get; set; }
        private int _width = 448;

        public void AddReference()
        {
        }

        public void RemoveReference()
        {
            RemoveReferenceCount++;
            if (ThrowOnRemoveReference)
                throw new InvalidOperationException("RemoveReference failed");
        }

        public virtual void Draw(SpriteBatch spriteBatch, Vector2 position)
        {
        }

        public virtual void Draw(SpriteBatch spriteBatch, Vector2 position, Rectangle? sourceRectangle)
        {
        }

        public virtual void Draw(
            SpriteBatch spriteBatch,
            Rectangle destinationRectangle,
            Rectangle? sourceRectangle,
            Color color,
            float rotation,
            Vector2 origin,
            SpriteEffects effects,
            float layerDepth)
        {
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 position, Vector2 scale, float rotation, Vector2 origin)
        {
        }

        public ITexture Clone()
        {
            return this;
        }

        public Color[] GetColorData()
        {
            return [];
        }

        public void SetColorData(Color[] colorData)
        {
        }

        public void SaveToFile(string filePath)
        {
        }

        public void Dispose()
        {
            IsDisposed = true;
        }

        public void SetUnderlyingDisposed(bool isDisposed)
        {
            SetDisposedFlag(Texture, isDisposed);
        }

        private static Texture2D CreateTexture2DStub()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            SetDisposedFlag(texture, false);
            return texture;
        }

        private static void SetDisposedFlag(Texture2D texture, bool isDisposed)
        {
            for (var type = texture.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField("_isDisposed", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? type.GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? type.GetField("<IsDisposed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.FieldType == typeof(bool))
                {
                    field.SetValue(texture, isDisposed);
                    return;
                }
            }

            throw new InvalidOperationException("Texture2D disposed flag field not found.");
        }
    }
}
