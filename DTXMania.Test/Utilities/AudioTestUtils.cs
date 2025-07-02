using System;
using System.IO;

namespace DTXMania.Test.Utilities
{
    /// <summary>
    /// Utility methods for creating test audio files and other test data
    /// </summary>
    public static class AudioTestUtils
    {
        /// <summary>
        /// Creates a minimal WAV file for testing purposes
        /// </summary>
        /// <param name="outputPath">The path where the WAV file should be created</param>
        /// <param name="durationSeconds">Duration of the audio in seconds (default: 0.1s)</param>
        /// <param name="sampleRate">Sample rate in Hz (default: 44100)</param>
        /// <param name="channels">Number of channels (default: 1 for mono)</param>
        /// <returns>The path to the created WAV file</returns>
        public static string CreateTestWavFile(string outputPath, double durationSeconds = 0.1, int sampleRate = 44100, short channels = 1)
        {
            var samples = (int)(sampleRate * durationSeconds);
            var dataSize = samples * channels * 2; // 16-bit = 2 bytes per sample per channel
            
            // Ensure directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            using var writer = new BinaryWriter(File.Create(outputPath));
            
            // WAV header
            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + dataSize); // ChunkSize
            writer.Write("WAVE".ToCharArray());
            writer.Write("fmt ".ToCharArray());
            writer.Write(16); // Subchunk1Size
            writer.Write((short)1); // AudioFormat (PCM)
            writer.Write(channels); // NumChannels
            writer.Write(sampleRate); // SampleRate
            writer.Write(sampleRate * channels * 2); // ByteRate
            writer.Write((short)(channels * 2)); // BlockAlign
            writer.Write((short)16); // BitsPerSample
            writer.Write("data".ToCharArray());
            writer.Write(dataSize); // Subchunk2Size
            
            // Silent audio data
            for (int i = 0; i < samples * channels; i++)
            {
                writer.Write((short)0);
            }
            
            return outputPath;
        }

        /// <summary>
        /// Creates a minimal WAV file in a temporary directory
        /// </summary>
        /// <param name="tempDir">Temporary directory path</param>
        /// <param name="filename">Filename for the WAV file (default: "test.wav")</param>
        /// <param name="durationSeconds">Duration of the audio in seconds (default: 0.1s)</param>
        /// <returns>The full path to the created WAV file</returns>
        public static string CreateTestWavFile(string tempDir, string filename = "test.wav", double durationSeconds = 0.1)
        {
            var wavPath = Path.Combine(tempDir, filename);
            return CreateTestWavFile(wavPath, durationSeconds);
        }

        /// <summary>
        /// Creates a fake MP3 file with dummy content for testing error handling
        /// </summary>
        /// <param name="outputPath">The path where the fake MP3 file should be created</param>
        /// <param name="content">Content to write (default: "fake mp3 content")</param>
        /// <returns>The path to the created fake MP3 file</returns>
        public static string CreateFakeMp3File(string outputPath, string content = "fake mp3 content")
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            File.WriteAllText(outputPath, content);
            return outputPath;
        }

        /// <summary>
        /// Creates a fake MP3 file in a temporary directory
        /// </summary>
        /// <param name="tempDir">Temporary directory path</param>
        /// <param name="filename">Filename for the MP3 file (default: "test.mp3")</param>
        /// <param name="content">Content to write (default: "fake mp3 content")</param>
        /// <returns>The full path to the created fake MP3 file</returns>
        public static string CreateFakeMp3File(string tempDir, string filename = "test.mp3", string content = "fake mp3 content")
        {
            var mp3Path = Path.Combine(tempDir, filename);
            return CreateFakeMp3File(mp3Path, content);
        }

        /// <summary>
        /// Creates a test file with specified content
        /// </summary>
        /// <param name="outputPath">The path where the file should be created</param>
        /// <param name="content">Content to write to the file</param>
        /// <returns>The path to the created file</returns>
        public static string CreateTestFile(string outputPath, string content)
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            File.WriteAllText(outputPath, content);
            return outputPath;
        }

        /// <summary>
        /// Creates a test file in a temporary directory
        /// </summary>
        /// <param name="tempDir">Temporary directory path</param>
        /// <param name="filename">Name of the file to create</param>
        /// <param name="content">Content to write to the file</param>
        /// <returns>The full path to the created file</returns>
        public static string CreateTestFile(string tempDir, string filename, string content)
        {
            var filePath = Path.Combine(tempDir, filename);
            return CreateTestFile(filePath, content);
        }
    }
}
