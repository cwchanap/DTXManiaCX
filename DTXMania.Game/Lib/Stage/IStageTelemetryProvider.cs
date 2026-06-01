#nullable enable

using DTXMania.Game.Lib;

namespace DTXMania.Game.Lib.Stage;

public interface IStageTelemetryProvider
{
    void PopulateTelemetry(GameTelemetrySnapshot telemetry);
}
