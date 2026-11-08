using System;
using DTXMania.Game;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;

var crashRuntime = CrashReportRuntime.CreateBestEffort(Console.Error);

return GameEntryPoint.Run(
    () => new Game1(crashRuntime.GameDiagnostics),
    crashRuntime,
    Console.Error);
