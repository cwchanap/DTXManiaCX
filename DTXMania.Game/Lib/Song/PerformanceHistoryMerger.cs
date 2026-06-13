#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTXMania.Game.Lib.Song
{
    internal readonly record struct PerformanceHistoryCandidate(string Text, DateTime Date);

    internal static class PerformanceHistoryMerger
    {
        public static async Task MergeAsync(
            SongDbContext context,
            int songId,
            int songScoreId,
            IEnumerable<PerformanceHistoryCandidate> incoming,
            CancellationToken cancellationToken = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (songId <= 0) throw new ArgumentOutOfRangeException(nameof(songId));
            if (songScoreId <= 0) throw new ArgumentOutOfRangeException(nameof(songScoreId));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            var existing = await context.PerformanceHistory
                .Where(p => p.SongScoreId == songScoreId)
                .OrderBy(p => p.DisplayOrder)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new List<PerformanceHistoryCandidate>();

            foreach (var row in existing)
            {
                if (!string.IsNullOrWhiteSpace(row.HistoryLine) && seen.Add(row.HistoryLine))
                {
                    candidates.Add(new PerformanceHistoryCandidate(row.HistoryLine, row.PerformedAt));
                }
            }

            foreach (var row in incoming)
            {
                if (!string.IsNullOrWhiteSpace(row.Text) && seen.Add(row.Text))
                {
                    candidates.Add(row);
                }
            }

            var topFive = candidates
                .OrderByDescending(c => c.Date)
                .Take(5)
                .ToList();

            context.PerformanceHistory.RemoveRange(existing);

            var displayOrder = 1;
            foreach (var row in topFive)
            {
                context.PerformanceHistory.Add(new PerformanceHistory
                {
                    SongId = songId,
                    SongScoreId = songScoreId,
                    PerformedAt = row.Date,
                    HistoryLine = row.Text,
                    DisplayOrder = displayOrder++,
                });
            }
        }
    }
}
