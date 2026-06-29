#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Stage.Performance
{
    public sealed class NxAttackEffectSettings
    {
        public static NxAttackEffectSettings Default { get; } = new NxAttackEffectSettings();

        public double PrimaryFrameDurationSeconds { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.PrimarySparkFrameDurationSeconds;

        public int PrimaryFrameCount { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.CombinedSparkFrameCount;

        public int StarParticleCount { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.StarParticleCount;

        public int ChipFragmentCount { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.ChipFragmentCount;

        public int WaveParticleCount { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.WaveParticleCount;

        public double StarLifetimeSeconds { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.StarLifetimeSeconds;

        public double ChipFragmentLifetimeSeconds { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.ChipFragmentLifetimeSeconds;

        public double WaveLifetimeSeconds { get; init; } =
            PerformanceUILayout.NxAttackEffectAssets.WaveLifetimeSeconds;
    }

    public class NxAttackEffectManager : IDisposable
    {
        private const int ChipFragmentSourceY = 640;
        private const int ChipFragmentSourceHeight = 64;
        private static readonly int[] DrumChipColumnWidths =
        {
            70, 58, 64, 56, 56, 56, 74, 48, 58, 74, 48, 58
        };

        private static readonly int[] LaneToDrumChipColumn =
        {
            9, 10, 11, 2, 3, 0, 4, 5, 1, 6
        };

        private readonly NxAttackEffectSettings _settings;
        private readonly Random _random;
        private readonly Dictionary<int, PrimarySparkInstance> _primarySparks = new Dictionary<int, PrimarySparkInstance>();
        private readonly List<ParticleInstance> _particles = new List<ParticleInstance>();
        private readonly List<int> _expiredSparkLanes = new List<int>();
        private readonly ITexture?[] _laneSparkTextures = new ITexture?[PerformanceUILayout.LaneCount];
        private readonly ITexture?[] _laneStarTextures = new ITexture?[PerformanceUILayout.LaneCount];
        private ITexture? _combinedSparkTexture;
        private ITexture? _chipTexture;
        private ITexture? _waveTexture;
        private bool _disposed;

        public NxAttackEffectManager(
            IResourceManager resourceManager,
            NxAttackEffectSettings? settings = null,
            Random? random = null)
        {
            ArgumentNullException.ThrowIfNull(resourceManager);

            _settings = settings ?? NxAttackEffectSettings.Default;
            _random = random ?? new Random(0);

            _combinedSparkTexture = LoadOptionalTexture(resourceManager, TexturePath.ChipFireCombined);
            if (!CanUseCombinedSparkSheet(_combinedSparkTexture))
            {
                _combinedSparkTexture?.RemoveReference();
                _combinedSparkTexture = null;
            }

            for (var lane = 0; lane < PerformanceUILayout.LaneCount; lane++)
            {
                _laneSparkTextures[lane] = LoadOptionalTexture(
                    resourceManager,
                    TexturePath.GetDrumChipFireLanePath(lane));

                _laneStarTextures[lane] = LoadOptionalTexture(
                    resourceManager,
                    TexturePath.GetDrumChipStarLanePath(lane));
            }

            _chipTexture = LoadOptionalTexture(resourceManager, TexturePath.DrumChips);
            _waveTexture = LoadOptionalTexture(resourceManager, TexturePath.ChipWave);
        }

        internal IReadOnlyDictionary<int, PrimarySparkInstance> ActivePrimarySparksForTesting => _primarySparks;

        internal int ActivePrimarySparkCountForTesting => _primarySparks.Count;

        internal int ActiveParticleCountForTesting => _particles.Count;

        public static Rectangle GetCombinedSparkSource(int laneIndex, int frameIndex)
        {
            return PerformanceUILayout.NxAttackEffectAssets.GetCombinedSparkSource(laneIndex, frameIndex);
        }

        public virtual void Spawn(int lane, JudgementType judgementType)
        {
            if (_disposed
                || lane < 0
                || lane >= PerformanceUILayout.LaneCount
                || judgementType == JudgementType.Miss)
            {
                return;
            }

            var origin = PerformanceUILayout.NxAttackEffectAssets.GetEffectOrigin(lane);
            if (_combinedSparkTexture != null || _laneSparkTextures[lane] != null)
            {
                _primarySparks[lane] = new PrimarySparkInstance(
                    lane,
                    judgementType,
                    origin,
                    usesCombinedSheet: _combinedSparkTexture != null);
            }

            SpawnStars(lane, origin);
            SpawnChipFragments(lane, origin);
            SpawnWaves(lane, origin);
        }

        public void Update(double deltaTime)
        {
            if (_disposed)
                return;

            var safeDelta = Math.Max(0.0, deltaTime);
            _expiredSparkLanes.Clear();
            foreach (var spark in _primarySparks)
            {
                spark.Value.Update(
                    safeDelta,
                    _settings.PrimaryFrameDurationSeconds,
                    _settings.PrimaryFrameCount);

                if (spark.Value.IsExpired)
                    _expiredSparkLanes.Add(spark.Key);
            }

            foreach (var lane in _expiredSparkLanes)
            {
                _primarySparks.Remove(lane);
            }
            _expiredSparkLanes.Clear();

            for (var i = _particles.Count - 1; i >= 0; i--)
            {
                _particles[i].Update(safeDelta);
                if (_particles[i].IsExpired)
                    _particles.RemoveAt(i);
            }
        }

        public void Draw(SpriteBatch? spriteBatch)
        {
            if (_disposed || spriteBatch == null)
                return;

            foreach (var spark in _primarySparks.Values)
            {
                DrawPrimarySpark(spriteBatch, spark);
            }

            foreach (var particle in _particles)
            {
                DrawParticle(spriteBatch, particle);
            }
        }

        public void ClearAll()
        {
            _primarySparks.Clear();
            _particles.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            ClearAll();

            _combinedSparkTexture?.RemoveReference();
            _combinedSparkTexture = null;

            _chipTexture?.RemoveReference();
            _chipTexture = null;

            _waveTexture?.RemoveReference();
            _waveTexture = null;

            for (var lane = 0; lane < PerformanceUILayout.LaneCount; lane++)
            {
                _laneSparkTextures[lane]?.RemoveReference();
                _laneSparkTextures[lane] = null;

                _laneStarTextures[lane]?.RemoveReference();
                _laneStarTextures[lane] = null;
            }

            _disposed = true;
        }

        private void SpawnStars(int lane, Vector2 origin)
        {
            if (_laneStarTextures[lane] == null)
                return;

            for (var i = 0; i < _settings.StarParticleCount; i++)
            {
                var angle = NextFloat(0f, MathHelper.TwoPi);
                var speed = NextFloat(70f, 145f);
                var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                _particles.Add(ParticleInstance.CreateStar(
                    lane,
                    origin,
                    velocity,
                    _settings.StarLifetimeSeconds));
            }
        }

        private void SpawnChipFragments(int lane, Vector2 origin)
        {
            if (_chipTexture == null)
                return;

            for (var i = 0; i < _settings.ChipFragmentCount; i++)
            {
                var direction = i % 2 == 0 ? -1f : 1f;
                var velocity = new Vector2(
                    direction * NextFloat(90f, 130f),
                    NextFloat(-150f, -95f));

                _particles.Add(ParticleInstance.CreateChip(
                    lane,
                    origin,
                    velocity,
                    GetChipFragmentSource(lane, i),
                    _settings.ChipFragmentLifetimeSeconds));
            }
        }

        private void SpawnWaves(int lane, Vector2 origin)
        {
            if (_waveTexture == null)
                return;

            for (var i = 0; i < _settings.WaveParticleCount; i++)
            {
                _particles.Add(ParticleInstance.CreateWave(
                    lane,
                    origin,
                    delaySeconds: i * 0.05,
                    _settings.WaveLifetimeSeconds));
            }
        }

        private void DrawPrimarySpark(SpriteBatch spriteBatch, PrimarySparkInstance spark)
        {
            var destination = CenteredDestination(
                spark.Position,
                PerformanceUILayout.NxAttackEffectAssets.PrimarySparkDrawSize,
                1f);

            if (spark.UsesCombinedSheet && _combinedSparkTexture != null)
            {
                _combinedSparkTexture.Draw(
                    spriteBatch,
                    destination,
                    GetCombinedSparkSource(spark.Lane, spark.FrameIndex),
                    Color.White,
                    0f,
                    Vector2.Zero,
                    SpriteEffects.None,
                    0f);
                return;
            }

            _laneSparkTextures[spark.Lane]?.Draw(
                spriteBatch,
                destination,
                null,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0f);
        }

        private void DrawParticle(SpriteBatch spriteBatch, ParticleInstance particle)
        {
            if (particle.DelaySeconds > 0)
                return;

            var color = Color.White * particle.Alpha;
            if (particle.Kind == ParticleKind.Star)
            {
                _laneStarTextures[particle.Lane]?.Draw(
                    spriteBatch,
                    CenteredDestination(
                        particle.Position,
                        PerformanceUILayout.NxAttackEffectAssets.StarDrawSize,
                        particle.Scale),
                    null,
                    color,
                    particle.Rotation,
                    PerformanceUILayout.NxAttackEffectAssets.StarDrawSize / 2f,
                    SpriteEffects.None,
                    0f);
            }
            else if (particle.Kind == ParticleKind.Chip)
            {
                var source = particle.SourceRectangle;
                _chipTexture?.Draw(
                    spriteBatch,
                    CenteredDestination(
                        particle.Position,
                        new Vector2(source.Width, source.Height),
                        particle.Scale),
                    source,
                    color,
                    particle.Rotation,
                    new Vector2(source.Width / 2f, source.Height / 2f),
                    SpriteEffects.None,
                    0f);
            }
            else if (particle.Kind == ParticleKind.Wave)
            {
                _waveTexture?.Draw(
                    spriteBatch,
                    CenteredDestination(
                        particle.Position,
                        PerformanceUILayout.NxAttackEffectAssets.WaveDrawSize,
                        particle.Scale),
                    null,
                    color,
                    particle.Rotation,
                    PerformanceUILayout.NxAttackEffectAssets.WaveDrawSize / 2f,
                    SpriteEffects.None,
                    0f);
            }
        }

        private static Rectangle CenteredDestination(Vector2 center, Vector2 size, float scale)
        {
            var width = Math.Max(1, (int)MathF.Round(size.X * scale));
            var height = Math.Max(1, (int)MathF.Round(size.Y * scale));

            return new Rectangle(
                (int)MathF.Round(center.X - width / 2f),
                (int)MathF.Round(center.Y - height / 2f),
                width,
                height);
        }

        private static Rectangle GetChipFragmentSource(int lane, int side)
        {
            if (lane < 0 || lane >= LaneToDrumChipColumn.Length)
                return Rectangle.Empty;

            var column = LaneToDrumChipColumn[lane];
            var columnX = GetDrumChipColumnX(column);
            var columnWidth = DrumChipColumnWidths[column];
            var fragmentWidth = Math.Max(8, columnWidth / 2);
            var fragmentX = side % 2 == 0
                ? columnX
                : columnX + columnWidth - fragmentWidth;

            return new Rectangle(
                fragmentX,
                ChipFragmentSourceY,
                fragmentWidth,
                ChipFragmentSourceHeight);
        }

        private static int GetDrumChipColumnX(int column)
        {
            var x = 0;
            for (var i = 0; i < column; i++)
            {
                x += DrumChipColumnWidths[i];
            }

            return x;
        }

        private static ITexture? LoadOptionalTexture(IResourceManager resourceManager, string path)
        {
            try
            {
                if (!resourceManager.ResourceExists(path))
                    return null;

                return resourceManager.LoadTexture(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"NxAttackEffectManager: {ex.GetType().Name} loading {path}: {ex.Message}");
                return null;
            }
        }

        private static bool CanUseCombinedSparkSheet(ITexture? texture)
        {
            return texture != null
                && texture.Width >= PerformanceUILayout.NxAttackEffectAssets.CombinedSparkFrameWidth
                    * PerformanceUILayout.NxAttackEffectAssets.CombinedSparkFrameCount
                && texture.Height >= PerformanceUILayout.NxAttackEffectAssets.CombinedSparkFrameHeight
                    * PerformanceUILayout.NxAttackEffectAssets.CombinedSparkLaneRows;
        }

        private float NextFloat(float min, float max)
        {
            return min + (float)_random.NextDouble() * (max - min);
        }

        internal sealed class PrimarySparkInstance
        {
            private double _elapsedSeconds;

            public PrimarySparkInstance(int lane, JudgementType judgementType, Vector2 position, bool usesCombinedSheet)
            {
                Lane = lane;
                JudgementType = judgementType;
                Position = position;
                UsesCombinedSheet = usesCombinedSheet;
            }

            public int Lane { get; }

            public JudgementType JudgementType { get; }

            public Vector2 Position { get; }

            public bool UsesCombinedSheet { get; }

            public int FrameIndex { get; private set; }

            public bool IsExpired { get; private set; }

            public void Update(double deltaTime, double frameDurationSeconds, int frameCount)
            {
                _elapsedSeconds += deltaTime;
                FrameIndex = (int)(_elapsedSeconds / frameDurationSeconds);

                if (FrameIndex >= frameCount)
                {
                    FrameIndex = frameCount - 1;
                    IsExpired = true;
                }
            }
        }

        internal sealed class ParticleInstance
        {
            private readonly double _durationSeconds;
            private double _elapsedSeconds;

            private ParticleInstance(
                ParticleKind kind,
                int lane,
                Vector2 position,
                Vector2 velocity,
                Rectangle sourceRectangle,
                double delaySeconds,
                double durationSeconds)
            {
                Kind = kind;
                Lane = lane;
                Position = position;
                Velocity = velocity;
                SourceRectangle = sourceRectangle;
                DelaySeconds = delaySeconds;
                _durationSeconds = durationSeconds;
                Alpha = 1f;
                Scale = kind == ParticleKind.Wave ? 0.6f : 1f;
            }

            public ParticleKind Kind { get; }

            public int Lane { get; }

            public Vector2 Position { get; private set; }

            public Vector2 Velocity { get; }

            public Rectangle SourceRectangle { get; }

            public double DelaySeconds { get; private set; }

            public float Alpha { get; private set; }

            public float Scale { get; private set; }

            public float Rotation { get; private set; }

            public bool IsExpired { get; private set; }

            public static ParticleInstance CreateStar(
                int lane,
                Vector2 position,
                Vector2 velocity,
                double durationSeconds)
            {
                return new ParticleInstance(
                    ParticleKind.Star,
                    lane,
                    position,
                    velocity,
                    Rectangle.Empty,
                    0.0,
                    durationSeconds);
            }

            public static ParticleInstance CreateChip(
                int lane,
                Vector2 position,
                Vector2 velocity,
                Rectangle sourceRectangle,
                double durationSeconds)
            {
                return new ParticleInstance(
                    ParticleKind.Chip,
                    lane,
                    position,
                    velocity,
                    sourceRectangle,
                    0.0,
                    durationSeconds);
            }

            public static ParticleInstance CreateWave(
                int lane,
                Vector2 position,
                double delaySeconds,
                double durationSeconds)
            {
                return new ParticleInstance(
                    ParticleKind.Wave,
                    lane,
                    position,
                    Vector2.Zero,
                    Rectangle.Empty,
                    delaySeconds,
                    durationSeconds);
            }

            public void Update(double deltaTime)
            {
                if (DelaySeconds > 0)
                {
                    DelaySeconds = Math.Max(0.0, DelaySeconds - deltaTime);
                    return;
                }

                _elapsedSeconds += deltaTime;
                if (_elapsedSeconds >= _durationSeconds)
                {
                    Alpha = 0f;
                    IsExpired = true;
                    return;
                }

                var progress = (float)(_elapsedSeconds / _durationSeconds);
                Position += Velocity * (float)deltaTime;
                Rotation += (Kind == ParticleKind.Chip ? 6f : 2f) * (float)deltaTime;
                Scale = Kind == ParticleKind.Wave
                    ? MathHelper.Lerp(0.6f, 1.7f, progress)
                    : MathHelper.Lerp(1f, 0.75f, progress);
                Alpha = MathHelper.Clamp(1f - progress, 0f, 1f);
            }
        }

        internal enum ParticleKind
        {
            Star,
            Chip,
            Wave
        }
    }
}
