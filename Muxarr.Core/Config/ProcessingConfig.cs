using System.Text.Json.Serialization;

namespace Muxarr.Core.Config;

public class ProcessingConfig
{
    public int ScanIntervalMinutes { get; set; }
    public int ConversionTimeoutMinutes { get; set; } = 60;

    // 0 means no limit. Handing the tools no timeout at all would give mkvmerge its 30-second default.
    [JsonIgnore]
    public TimeSpan ConversionTimeout => ConversionTimeoutMinutes > 0
        ? TimeSpan.FromMinutes(ConversionTimeoutMinutes)
        : Timeout.InfiniteTimeSpan;
    public bool QueueScannedFiles { get; set; }

    public bool PostProcessingEnabled { get; set; }
    public string PostProcessingCommand { get; set; } = string.Empty;

    public string ResolveCommand(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var filename = Path.GetFileNameWithoutExtension(filePath);

        return PostProcessingCommand
            .Replace("{{file}}", filePath)
            .Replace("{file}", filePath)
            .Replace("{{filename}}", filename)
            .Replace("{filename}", filename)
            .Replace("{{directory}}", directory)
            .Replace("{directory}", directory);
    }
}
