using System;
using DTXMania.Game;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;

var startupTrace = StartupTimingTrace.StartProcess();
var crashRuntime = CrashReportRuntime.CreateBestEffort(Console.Error);

return GameEntryPoint.Run(
    () => new Game1(startupTrace, crashRuntime.GameDiagnostics),
    crashRuntime,
    Console.Error);
