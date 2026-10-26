using Microsoft.Extensions.Logging;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

public static class MediaConversionExtensions
{
    public static void Log(this MediaConversion conversion, string message, ILogger? logger = null,
        bool isError = false)
    {
        if (isError)
        {
            logger?.LogError(message);
        }
        else
        {
            logger?.LogInformation(message);
        }

        // Process output arrives on two threads while the UI and notifications read along.
        lock (conversion.LogEntries)
        {
            conversion.LogEntries.Add(new ConversionLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Message = message,
                IsError = isError
            });
        }
    }

    public static List<ConversionLogEntry> GetLogSnapshot(this MediaConversion conversion)
    {
        lock (conversion.LogEntries)
        {
            return conversion.LogEntries.ToList();
        }
    }

    public static void LogError(this MediaConversion conversion, string message, ILogger? logger = null)
    {
        conversion.State = ConversionState.Failed;
        Log(conversion, message, logger, true);
    }

    /// <summary>
    /// The source tracks the plan keeps, in output order: what the file will have once a queued
    /// or running conversion finishes. Empty when the conversion has no source snapshot.
    /// </summary>
    public static List<TrackSnapshot> PlannedTracks(this MediaConversion conversion)
    {
        var source = conversion.BeforeSnapshot?.Tracks;
        if (source == null || conversion.ConversionPlan.Tracks.Count == 0)
        {
            return [];
        }

        var byIndex = source.GroupBy(t => t.Index).ToDictionary(g => g.Key, g => g.First());
        return conversion.ConversionPlan.Tracks
            .Where(t => byIndex.ContainsKey(t.Index))
            .Select(t => byIndex[t.Index])
            .ToList();
    }

    public static string GetSizeChangePercentage(this MediaConversion conversion)
    {
        return GetSizeChangePercentage(conversion.SizeBefore, conversion.SizeAfter);
    }

    public static string GetSizeChangePercentage(long sizeBefore, long sizeAfter)
    {
        if (sizeBefore == 0)
        {
            return "0.0%";
        }

        var reduction = Math.Abs(100 - (double)sizeAfter / sizeBefore * 100);
        return $"{reduction:F1}%";
    }
}
