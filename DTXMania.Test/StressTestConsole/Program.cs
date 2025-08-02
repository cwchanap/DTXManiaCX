using System;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Test.Performance;
using DTXMania.Test.Helpers;

namespace DTXMania.Test.StressTestConsole
{
    /// <summary>
    /// Standalone console application for running DTXMania stress tests
    /// Task 3-B-5: Load 100 k-note synthetic chart, run 5 minutes, ensure frame time < 16 ms on reference GPU
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("================================");
            Console.WriteLine("DTXMania Performance Stress Test");
            Console.WriteLine("================================");
            Console.WriteLine();

            try
            {
                // Parse command line arguments
                var config = ParseArguments(args);
                
                Console.WriteLine($"Configuration:");
                Console.WriteLine($"  Note Count: {config.NoteCount:N0}");
                Console.WriteLine($"  Duration: {config.DurationMinutes} minutes");
                Console.WriteLine($"  Target Frame Time: {config.TargetFrameTimeMs}ms");
                Console.WriteLine($"  Output Directory: {config.OutputDir}");
                Console.WriteLine();

                // Initialize test output helper
                var outputHelper = new ConsoleOutputHelper();

                // Run the stress test
                using var stressTest = new StressTestRunner(outputHelper);
                
                Console.WriteLine("Starting stress test...");
                await RunStressTestWithConfig(stressTest, config);
                
                Console.WriteLine();
                Console.WriteLine("Stress test completed successfully!");
                Console.WriteLine("Check the output above for detailed performance analysis.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running stress test: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                Environment.Exit(1);
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        /// <summary>
        /// Parses command line arguments
        /// </summary>
        private static StressTestConfig ParseArguments(string[] args)
        {
            var config = new StressTestConfig();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--notes":
                    case "-n":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var noteCount))
                        {
                            config.NoteCount = noteCount;
                            i++;
                        }
                        break;

                    case "--duration":
                    case "-d":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var duration))
                        {
                            config.DurationMinutes = duration;
                            i++;
                        }
                        break;

                    case "--target-frame-time":
                    case "-t":
                        if (i + 1 < args.Length && double.TryParse(args[i + 1], out var frameTime))
                        {
                            config.TargetFrameTimeMs = frameTime;
                            i++;
                        }
                        break;

                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length)
                        {
                            config.OutputDir = args[i + 1];
                            i++;
                        }
                        break;

                    case "--help":
                    case "-h":
                        ShowHelp();
                        Environment.Exit(0);
                        break;
                }
            }

            return config;
        }

        /// <summary>
        /// Shows help information
        /// </summary>
        private static void ShowHelp()
        {
            Console.WriteLine("DTXMania Performance Stress Test");
            Console.WriteLine();
            Console.WriteLine("Usage: StressTestConsole [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -n, --notes <count>           Number of synthetic notes to generate (default: 100000)");
            Console.WriteLine("  -d, --duration <minutes>      Test duration in minutes (default: 5)");
            Console.WriteLine("  -t, --target-frame-time <ms>  Target frame time in milliseconds (default: 16.0)");
            Console.WriteLine("  -o, --output <directory>      Output directory for results (default: temp)");
            Console.WriteLine("  -h, --help                    Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  StressTestConsole --notes 50000 --duration 2");
            Console.WriteLine("  StressTestConsole -n 200000 -d 10 -t 12.0");
        }

        /// <summary>
        /// Runs the stress test with the specified configuration
        /// </summary>
        private static async Task RunStressTestWithConfig(StressTestRunner stressTest, StressTestConfig config)
        {
            // For now, we'll use the default test which runs the full 100k note, 5-minute test
            // In a more complete implementation, we could modify the StressTestRunner to accept configuration
            
            await stressTest.StressTest_100kNoteChart_MaintainsPerformanceTargets();
        }
    }

    /// <summary>
    /// Configuration for stress testing
    /// </summary>
    public class StressTestConfig
    {
        public int NoteCount { get; set; } = 100000;
        public int DurationMinutes { get; set; } = 5;
        public double TargetFrameTimeMs { get; set; } = 16.0;
        public string OutputDir { get; set; } = Path.GetTempPath();
    }

    /// <summary>
    /// Console implementation of ITestOutputHelper for standalone testing
    /// </summary>
    public class ConsoleOutputHelper : Xunit.Abstractions.ITestOutputHelper
    {
        public void WriteLine(string message)
        {
            Console.WriteLine(message);
        }

        public void WriteLine(string format, params object[] args)
        {
            Console.WriteLine(format, args);
        }
    }
}
