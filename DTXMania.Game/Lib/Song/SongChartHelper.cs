
using System;
using System.Linq;
using DTX.Song;

namespace DTXMania.Game.Lib.Song
{
    public static class SongChartHelper
    {
        /// <summary>
        /// Gets the chart for the current difficulty level
        /// </summary>
        public static DTXMania.Game.Lib.Song.Entities.SongChart GetCurrentDifficultyChart(this SongListNode currentSong, int currentDifficulty)
        {
            // If no song is selected, return null
            if (currentSong?.DatabaseSong == null)
            {
                return null;
            }

            // Get all charts for this song
            var allCharts = currentSong.DatabaseSong.Charts?.ToList();

            if (allCharts == null || allCharts.Count == 0)
            {
                var fallbackChart = currentSong.DatabaseChart;
                return fallbackChart; // Fallback to primary chart
            }

            // If we only have one chart, return it
            if (allCharts.Count == 1)
                return allCharts[0];

            // For simplicity, assume drums mode and map difficulty to chart index
            var drumCharts = allCharts.Where(chart => chart.HasDrumChart && chart.DrumLevel > 0)
                                     .OrderBy(chart => chart.DrumLevel)
                                     .ToList();

            if (drumCharts.Count == 0)
                return allCharts[0]; // Fallback if no drum charts

            // Map difficulty index to chart (0=easiest, higher=harder)
            int chartIndex = Math.Clamp(currentDifficulty, 0, drumCharts.Count - 1);
            return drumCharts[chartIndex];
        }
    }
}
