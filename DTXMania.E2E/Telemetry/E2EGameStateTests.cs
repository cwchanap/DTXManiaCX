using System.Text.Json;
using DTXMania.E2E.Telemetry;

namespace DTXMania.E2E.Telemetry;

[Trait("Category", "E2E-Support")]
public sealed class E2EGameStateTests
{
    [Fact]
    public void Telemetry_WhenCustomDataContainsTelemetry_ReturnsValue()
    {
        var state = new E2EGameState
        {
            CustomData = new Dictionary<string, JsonElement>
            {
                ["telemetry"] = JsonDocument.Parse("{\"stageType\":\"Result\",\"score\":500}").RootElement
            }
        };

        Assert.Equal("Result", state.StageType);
        Assert.Equal(500, state.Score);
    }

    [Fact]
    public void Telemetry_WhenCustomDataIsEmpty_ReturnsDefaultInsteadOfThrowing()
    {
        var state = new E2EGameState
        {
            CustomData = new Dictionary<string, JsonElement>()
        };

        // Should not throw — returns empty JSON object
        var ex = Record.Exception(() => _ = state.Telemetry);
        Assert.Null(ex);

        // Downstream accessors degrade gracefully to defaults
        Assert.Equal(string.Empty, state.StageType);
        Assert.Null(state.SelectedSongTitle);
        Assert.Equal(0, state.Score);
        Assert.False(state.ClearFlag);
        Assert.False(state.StageCompleted);
    }

    [Fact]
    public void Telemetry_WhenCustomDataHasNoTelemetryKey_ReturnsDefaultInsteadOfThrowing()
    {
        var state = new E2EGameState
        {
            CustomData = new Dictionary<string, JsonElement>
            {
                ["other"] = JsonDocument.Parse("\"value\"").RootElement
            }
        };

        var ex = Record.Exception(() => _ = state.Telemetry);
        Assert.Null(ex);
        Assert.Equal(string.Empty, state.StageType);
    }
}
