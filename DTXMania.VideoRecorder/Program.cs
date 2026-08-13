using DTXMania.VideoRecorder.Sandbox;

namespace DTXMania.VideoRecorder;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = RecorderCommandLine.Parse(args);
            var environment = RecorderCommandLine.ReadEnvironment(
                command.Verb == RecorderVerb.Record);
            RecorderCommandLine.Validate(command, environment);

            if (command.Verb == RecorderVerb.Doctor)
            {
                PrintDoctorSummary(environment);
                return 0;
            }

            var sandbox = RecordingSandbox.Create(environment.SourceAppDataRoot);
            try
            {
                Console.WriteLine($"Recorder sandbox ready at '{sandbox.RunRoot}'.");
                Console.WriteLine("Recorder workflow is not yet configured.");
                await sandbox.DeleteOnSuccessAsync().ConfigureAwait(false);
                return 0;
            }
            catch
            {
                // Keep the sandbox for diagnostics when a record run fails.
                throw;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 2;
        }
    }

    private static void PrintDoctorSummary(
        DTXMania.VideoRecorder.Configuration.RecorderEnvironment environment)
    {
        Console.WriteLine("dtx-video doctor: configuration gates passed.");
        Console.WriteLine($"OBS URL: {environment.ObsUrl}");
        Console.WriteLine("Dedicated profile/collection/scene already selected");
        Console.WriteLine("CX window/program capture configured");
        Console.WriteLine("CX application audio configured");
        Console.WriteLine("Hybrid MP4 recording configured");
        Console.WriteLine("WebSocket enabled");
        Console.WriteLine("raw output directory matches DTXMANIA_VIDEO_OBS_OUTPUT_DIR");
        Console.WriteLine("A fresh sandbox database may require several minutes for first-run library enumeration.");
    }
}
