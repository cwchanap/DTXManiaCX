using DTXMania.Automation.Process;

namespace DTXMania.Automation.Tests.Process;

public sealed class GameLaunchTargetTests
{
    [Fact]
    public void ProjectPaths_ShouldExposeExactRepositoryPaths()
    {
        Assert.Equal("DTXMania.Game/DTXMania.Game.Windows.csproj", GameProjectPaths.Windows);
        Assert.Equal("DTXMania.Game/DTXMania.Game.Mac.csproj", GameProjectPaths.Mac);
    }

    [Fact]
    public void Project_WithOverride_ShouldKeepExactOverride()
    {
        var target = GameLaunchTarget.Project("custom/Game.csproj");

        Assert.Equal(GameLaunchKind.Project, target.Kind);
        Assert.Equal("custom/Game.csproj", target.Path);
    }

    [Fact]
    public void Project_WithBlankOverride_ShouldUseCurrentPlatformDefault()
    {
        var target = GameLaunchTarget.Project(" ");

        Assert.Equal(GameProjectPaths.Current, target.Path);
    }

    [Fact]
    public void Executable_ShouldKeepExactCallerPath()
    {
        var target = GameLaunchTarget.Executable("/tmp/DTXMania.Game");

        Assert.Equal(GameLaunchKind.Executable, target.Kind);
        Assert.Equal("/tmp/DTXMania.Game", target.Path);
    }

    [Fact]
    public void Executable_WithBlankPath_ShouldReject()
    {
        Assert.Throws<ArgumentException>(() => GameLaunchTarget.Executable(" "));
    }
}
