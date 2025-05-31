using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;

namespace DTX.Song
{
    /// <summary>
    /// Simple test class to verify Phase 1 song system implementation
    /// This will be replaced with proper unit tests later
    /// </summary>
    public static class SongSystemTest
    {
        /// <summary>
        /// Tests the basic functionality of the song system
        /// </summary>
        public static async Task RunBasicTestAsync()
        {
            Debug.WriteLine("=== DTXMania Song System Phase 1 Test ===");

            try
            {
                // Test 1: DTX Metadata Parser
                await TestMetadataParser();

                // Test 2: Song Node Creation
                TestSongNodeCreation();

                // Test 3: Song Manager
                await TestSongManager();

                Debug.WriteLine("=== All tests completed successfully! ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"=== Test failed: {ex.Message} ===");
            }
        }

        /// <summary>
        /// Tests the DTX metadata parser with mock data
        /// </summary>
        private static async Task TestMetadataParser()
        {
            Debug.WriteLine("Testing DTX Metadata Parser...");

            var parser = new DTXMetadataParser();

            // Test basic functionality without file I/O
            Debug.WriteLine("  Testing parser initialization...");

            // Test supported file extensions
            var supportedExtensions = new[] { ".dtx", ".gda", ".g2d", ".bms", ".bme", ".bml" };
            foreach (var ext in supportedExtensions)
            {
                if (!parser.IsSupported(ext))
                {
                    throw new Exception($"Extension {ext} should be supported");
                }
            }

            // Test unsupported extensions
            var unsupportedExtensions = new[] { ".txt", ".mp3", ".wav", ".jpg" };
            foreach (var ext in unsupportedExtensions)
            {
                if (parser.IsSupported(ext))
                {
                    throw new Exception($"Extension {ext} should not be supported");
                }
            }

            Debug.WriteLine("  ✓ DTX Metadata Parser basic tests passed");
            Debug.WriteLine("  Note: File parsing will be tested with real DTX files during enumeration");

            await Task.CompletedTask; // Keep async signature for consistency
        }

        /// <summary>
        /// Tests song node creation and hierarchy
        /// </summary>
        private static void TestSongNodeCreation()
        {
            Debug.WriteLine("Testing Song Node Creation...");

            // Create test metadata
            var metadata = new SongMetadata
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Test Genre",
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 65,
                FilePath = "test_song.dtx"
            };

            // Create song node
            var songNode = SongListNode.CreateSongNode(metadata);

            // Verify node properties
            Debug.WriteLine($"  Node Type: {songNode.Type}");
            Debug.WriteLine($"  Display Title: {songNode.DisplayTitle}");
            Debug.WriteLine($"  Available Difficulties: {songNode.AvailableDifficulties}");
            Debug.WriteLine($"  Max Difficulty Level: {songNode.MaxDifficultyLevel}");

            // Verify scores were created
            var drumScore = songNode.GetScore(0);
            if (drumScore == null) throw new Exception("Drum score not created");
            if (drumScore.Instrument != "DRUMS") throw new Exception("Drum score instrument incorrect");
            if (drumScore.DifficultyLevel != 85) throw new Exception("Drum score level incorrect");

            // Test BOX node creation
            var boxNode = SongListNode.CreateBoxNode("Test Folder", "/test/folder");
            boxNode.AddChild(songNode);

            Debug.WriteLine($"  BOX Node Title: {boxNode.DisplayTitle}");
            Debug.WriteLine($"  BOX Children Count: {boxNode.Children.Count}");
            Debug.WriteLine($"  Child Breadcrumb: {songNode.BreadcrumbPath}");

            // Test back navigation node
            var backNode = SongListNode.CreateBackNode(boxNode);
            Debug.WriteLine($"  Back Node Title: {backNode.DisplayTitle}");

            Debug.WriteLine("  ✓ Song Node Creation test passed");
        }

        /// <summary>
        /// Tests the song manager functionality
        /// </summary>
        private static async Task TestSongManager()
        {
            Debug.WriteLine("Testing Song Manager...");

            var songManager = new SongManager();

            // Test basic initialization
            Debug.WriteLine($"  Initial database count: {songManager.DatabaseScoreCount}");
            Debug.WriteLine($"  Initial root songs count: {songManager.RootSongs.Count}");
            Debug.WriteLine($"  Is enumerating: {songManager.IsEnumerating}");

            // Test enumeration with non-existent paths (should handle gracefully)
            var testPaths = new[] { "NonExistentPath1", "NonExistentPath2" };
            var enumResult = await songManager.EnumerateSongsAsync(testPaths);

            Debug.WriteLine($"  Enumeration result: {enumResult} songs found");
            Debug.WriteLine($"  Root songs count after enumeration: {songManager.RootSongs.Count}");

            // Test basic operations
            songManager.Clear();
            Debug.WriteLine($"  Database count after clear: {songManager.DatabaseScoreCount}");

            Debug.WriteLine("  ✓ Song Manager basic tests passed");
            Debug.WriteLine("  Note: Real enumeration will be tested with DTXFiles folder");
        }

        /// <summary>
        /// Creates a sample song hierarchy for testing
        /// </summary>
        public static SongListNode CreateSampleHierarchy()
        {
            // Create root BOX
            var rootBox = SongListNode.CreateBoxNode("Sample Songs", "/sample");

            // Create some sample songs
            for (int i = 1; i <= 3; i++)
            {
                var metadata = new SongMetadata
                {
                    Title = $"Sample Song {i}",
                    Artist = $"Artist {i}",
                    Genre = "Sample",
                    DrumLevel = 50 + (i * 10),
                    GuitarLevel = 40 + (i * 10),
                    BassLevel = 30 + (i * 10),
                    FilePath = $"sample_song_{i}.dtx"
                };

                var songNode = SongListNode.CreateSongNode(metadata);
                rootBox.AddChild(songNode);
            }

            // Create a sub-folder
            var subBox = SongListNode.CreateBoxNode("Sub Folder", "/sample/sub");
            var subMetadata = new SongMetadata
            {
                Title = "Sub Song",
                Artist = "Sub Artist",
                Genre = "Sub Genre",
                DrumLevel = 90,
                FilePath = "sub_song.dtx"
            };
            var subSong = SongListNode.CreateSongNode(subMetadata);
            subBox.AddChild(subSong);
            rootBox.AddChild(subBox);

            // Add random selection
            var randomNode = SongListNode.CreateRandomNode();
            rootBox.AddChild(randomNode);

            return rootBox;
        }
    }
}
