using Xunit;

namespace DTXMania.Test.Helpers;

/// <summary>
/// Serializes tests that construct a real <see cref="TestGraphicsDeviceService"/>
/// (<c>TestGame</c>/<c>GraphicsDevice</c>) and keeps them from running concurrently
/// with other <c>DisableParallelization = true</c> collections (such as
/// <c>AppPaths</c> and <c>SongManager</c>) that also host device-backed tests.
/// </summary>
[CollectionDefinition("GraphicsTests", DisableParallelization = true)]
public sealed class GraphicsTestsCollectionDefinition
{
}
