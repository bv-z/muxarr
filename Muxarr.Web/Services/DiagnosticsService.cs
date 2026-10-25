using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Config;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Helpers;

namespace Muxarr.Web.Services;

/// <summary>
/// One text file with what it takes to look into a problem: versions, the settings with
/// every secret left out, the state of the library, recent conversions with their logs and
/// the application log. Made to be attached to a bug report or a chat as it is.
/// </summary>
public class DiagnosticsService(IDbContextFactory<AppDbContext> contextFactory)
{
    public const int LogDays = 7;
    public const int MaxLogLines = 20_000;
    private const int RecentConversions = 200;
    private const int ConversionLogs = 60;
    private const int ListedFiles = 100;

    // API keys and tokens are long runs of letters and digits; paths and names rarely are.
    private static readonly Regex SecretLike = new("[A-Za-z0-9_-]{20,}", RegexOptions.Compiled);

    public static string FileName(DateTime utcNow)
    {
        return $"muxarr-diagnostics-{ToLocal(utcNow):yyyy-MM-dd-HHmm}.txt";
    }

    public async Task<string> BuildReportAsync(CancellationToken token = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(token);
        var sb = new StringBuilder();

        await WriteHeader(sb);
        var profiles = await context.Profiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync(token);
        WriteDisks(sb, profiles);
        await WriteSettings(sb, context, profiles, token);
        await WriteLibrary(sb, context, token);
        await WriteNeedingWorkAgain(sb, context, token);
        await WriteConversions(sb, context, token);
        await WriteLog(sb, context, token);

        return sb.ToString();
    }

    private static async Task WriteHeader(StringBuilder sb)
    {
        var now = DateTime.UtcNow;
        var zone = TimeZoneInfo.Local;
        sb.AppendLine("Muxarr diagnostics");
        sb.AppendLine($"Generated   {ToLocal(now):yyyy-MM-dd HH:mm:ss} ({zone.Id}, UTC{FormatOffset(zone.GetUtcOffset(now))}). Times below are in this zone, except the lines inside conversion logs, which are UTC.");
        sb.AppendLine($"Version     {AppInfo.Version}{(AppInfo.Commit != null ? $" (commit {AppInfo.Commit})" : "")}");
        sb.AppendLine($"Runtime     {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"Container   {(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ? "yes" : "no")}");
        sb.AppendLine($"Process     {ProcessIds()}");
        sb.AppendLine($"mkvmerge    {await FirstLine("mkvmerge", "--version")}");
        sb.AppendLine($"ffmpeg      {await FirstLine("ffmpeg", "-version")}");
    }

    private static void WriteDisks(StringBuilder sb, List<Profile> profiles)
    {
        Section(sb, "Disk space (an Unraid user share reports the whole pool or array)");
        foreach (var directory in profiles.SelectMany(p => p.Directories).Distinct())
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    sb.AppendLine($"{directory}: NOT FOUND");
                    continue;
                }

                var drive = new DriveInfo(directory);
                sb.AppendLine($"{directory}: {Size(drive.AvailableFreeSpace)} free of {Size(drive.TotalSize)}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"{directory}: could not read ({ex.Message})");
            }
        }
    }

    private static async Task WriteSettings(StringBuilder sb, AppDbContext context, List<Profile> profiles,
        CancellationToken token)
    {
        Section(sb, "Settings (passwords, API keys and notification settings are left out)");

        var processing = context.Configs.GetOrDefault<ProcessingConfig>();
        sb.AppendLine($"Scan interval: {(processing.ScanIntervalMinutes > 0 ? $"{processing.ScanIntervalMinutes} min" : "off")}");
        sb.AppendLine($"Conversion timeout: {(processing.ConversionTimeoutMinutes > 0 ? $"{processing.ConversionTimeoutMinutes} min" : "none")}");
        sb.AppendLine($"Queue files found by a scan: {YesNo(processing.QueueScannedFiles)}");
        sb.AppendLine($"Post-processing: {(processing.PostProcessingEnabled ? Redact(processing.PostProcessingCommand) : "off")}");

        var webhook = context.Configs.GetOrDefault<WebhookConfig>();
        sb.AppendLine($"Webhooks: delay {webhook.DelaySeconds}s, auto-queue {YesNo(webhook.AutoQueue)}, " +
                      $"Muxarr URL {(string.IsNullOrWhiteSpace(webhook.MuxarrUrl) ? "-" : webhook.MuxarrUrl)}, " +
                      $"API key {(string.IsNullOrEmpty(webhook.ApiKey) ? "not set" : "set")}");

        var auth = context.Configs.Get<AuthConfig>(AuthConfig.Key);
        sb.AppendLine($"Login: {(string.IsNullOrEmpty(auth?.PasswordHash) ? "off" : "on")}, " +
                      $"trust local network {YesNo(auth?.TrustLocalNetwork == true)}");

        foreach (var integration in await context.Integrations.AsNoTracking().OrderBy(i => i.Id).ToListAsync(token))
        {
            sb.AppendLine($"{integration.Type} \"{integration.Name}\": {(string.IsNullOrWhiteSpace(integration.Url) ? "no URL" : integration.Url)}, " +
                          $"API key {(string.IsNullOrEmpty(integration.ApiKey) ? "not set" : "set")}");
        }

        var lastSync = context.Configs.Get<DateTime?>("LastArrSync");
        sb.AppendLine($"Last Sonarr/Radarr sync: {(lastSync is { } synced && synced > DateTime.MinValue ? Time(synced) : "never")}");

        foreach (var mapping in context.Configs.GetOrDefault<PathMappingConfig>().Mappings)
        {
            sb.AppendLine($"Path mapping: {mapping.From} -> {mapping.To}");
        }

        foreach (var notification in context.Configs.GetOrDefault<List<NotificationConfig>>())
        {
            sb.AppendLine($"Notification \"{notification.Name}\": {notification.Provider}, " +
                          $"{(notification.Enabled ? "on" : "off")}, on {notification.Triggers}");
        }

        foreach (var profile in profiles)
        {
            sb.AppendLine();
            sb.AppendLine($"Profile \"{profile.Name}\" (#{profile.Id})");
            sb.AppendLine($"  Folders: {string.Join(", ", profile.Directories)}");
            sb.AppendLine($"  Skip hardlinked {YesNo(profile.SkipHardlinkedFiles)}, clear file title {YesNo(profile.ClearFileTitle)}, " +
                          $"remove chapters {YesNo(profile.RemoveChapters)}, clear video names {YesNo(profile.ClearVideoTrackNames)}, " +
                          $"trim to video length {YesNo(profile.TrimToVideoLength)}");
            WriteTrackSettings(sb, "Audio", profile.AudioSettings);
            WriteTrackSettings(sb, "Subtitles", profile.SubtitleSettings);
        }
    }

    private static void WriteTrackSettings(StringBuilder sb, string label, TrackSettings s)
    {
        if (!s.Enabled)
        {
            sb.AppendLine($"  {label}: track removal off; default {s.DefaultStrategy}, order {s.ReorderStrategy}, standardize names {YesNo(s.StandardizeTrackNames)}");
            return;
        }

        var languages = s.AllowedLanguages.Select(l =>
        {
            var extras = new List<string>();
            if (l.IsFallback)
            {
                extras.Add("only if the one above is missing");
            }

            if (l.MaxTracks is { } max)
            {
                extras.Add($"max {max}");
            }

            return extras.Count > 0 ? $"{l.Name} ({string.Join(", ", extras)})" : l.Name;
        });
        sb.AppendLine($"  {label}: keep {string.Join(", ", languages)}");
        sb.AppendLine($"    remove commentary {YesNo(s.RemoveCommentary)}, remove SDH/AD {YesNo(s.RemoveImpaired)}, " +
                      $"undetermined is original {YesNo(s.AssumeUndeterminedIsOriginal)}, default {s.DefaultStrategy}, " +
                      $"order {s.ReorderStrategy}, standardize names {YesNo(s.StandardizeTrackNames)}" +
                      (s.StandardizeTrackNames ? $" (\"{s.TrackNameTemplate}\")" : ""));
        if (s.ExcludeCodecs)
        {
            var codecs = s.ExcludedAudioCodecs.Select(c => c.ToString()).Concat(s.ExcludedCodecs.Select(c => c.ToString()));
            sb.AppendLine($"    excluded codecs: {string.Join(", ", codecs)}");
        }
    }

    private static async Task WriteLibrary(StringBuilder sb, AppDbContext context, CancellationToken token)
    {
        Section(sb, "Library");
        var files = context.MediaFiles.AsNoTracking();
        sb.AppendLine($"Files: {await files.CountAsync(token)} (hardlinked, skipped: {await files.CountAsync(f => f.IsHardlinked, token)})");
        sb.AppendLine($"Need track removal: {await files.CountAsync(f => f.HasRedundantTracks, token)} " +
                      $"(audio {await files.CountAsync(f => f.HasRedundantAudio, token)}, subtitles {await files.CountAsync(f => f.HasRedundantSubtitles, token)}), " +
                      $"need a metadata fix: {await files.CountAsync(f => f.HasNonStandardMetadata || f.HasRemovableChapters, token)}, " +
                      $"scan warnings: {await files.CountAsync(f => f.HasScanWarning, token)}");

        var noLanguage = await files.Where(f => f.OriginalLanguage == null || f.OriginalLanguage == "")
            .OrderBy(f => f.Path).Select(f => f.Path).ToListAsync(token);
        sb.AppendLine($"Without an original language from Sonarr/Radarr: {noLanguage.Count}");
        foreach (var path in noLanguage.Take(ListedFiles))
        {
            sb.AppendLine($"  {path}");
        }

        var warnings = await files.Where(f => f.HasScanWarning).OrderBy(f => f.Path).Select(f => f.Path)
            .Take(ListedFiles).ToListAsync(token);
        if (warnings.Count > 0)
        {
            sb.AppendLine("With a scan warning:");
            foreach (var path in warnings)
            {
                sb.AppendLine($"  {path}");
            }
        }
    }

    // A file that still needs work after it was converted was either replaced since (an upgrade or
    // re-import from Sonarr/Radarr) or converted without reaching its target, which is a Muxarr problem.
    // Telling the two apart needs the conversion's result next to the file as it is now.
    private static async Task WriteNeedingWorkAgain(StringBuilder sb, AppDbContext context, CancellationToken token)
    {
        var files = await context.MediaFiles.AsNoTracking()
            .Include(f => f.Snapshot).ThenInclude(s => s.Tracks)
            .Include(f => f.Profile)
            .Where(f => f.HasRedundantTracks || f.HasNonStandardMetadata || f.HasRemovableChapters)
            .OrderBy(f => f.Path)
            .ToListAsync(token);
        var conversions = await context.MediaConversions.AsNoTracking()
            .Include(c => c.AfterSnapshot).ThenInclude(s => s!.Tracks)
            .Where(c => c.State == ConversionState.Completed)
            .OrderByDescending(c => c.Id)
            .ToListAsync(token);

        var never = 0;
        var replaced = new List<string>();
        var unchanged = new List<string>();
        foreach (var file in files)
        {
            var byFile = conversions.FirstOrDefault(c => c.MediaFileId == file.Id);
            var byTitle = byFile == null && !string.IsNullOrEmpty(file.Title)
                ? conversions.FirstOrDefault(c => c.Name == file.Title)
                : null;
            var last = byFile ?? byTitle;
            if (last == null)
            {
                never++;
                continue;
            }

            // A swap or an in-place edit ends with the file at SizeAfter, written before UpdatedDate.
            var changed = byTitle != null || file.Size != last.SizeAfter
                          || file.FileLastWriteTime > last.UpdatedDate.AddMinutes(1);
            var entry = new StringBuilder();
            entry.AppendLine($"--- {file.Path}");
            entry.AppendLine($"  Converted {Time(last.UpdatedDate)} (#{last.Id}){(byTitle != null ? ", found by title: the file was replaced and is a new library entry" : "")}");
            entry.AppendLine($"  File now: {Size(file.Size)}, modified {Time(file.FileLastWriteTime)}; after that conversion: {Size(last.SizeAfter)}" +
                             $" -> {(changed ? "CHANGED since, most likely replaced by Sonarr/Radarr" : "unchanged, so the conversion did not reach its target")}");
            entry.AppendLine($"  Original language: {(string.IsNullOrEmpty(file.OriginalLanguage) ? "unknown" : file.OriginalLanguage)}");
            if (last.AfterSnapshot != null)
            {
                entry.AppendLine($"  After the conversion: {Tracks(last.AfterSnapshot.Tracks)}");
            }

            entry.AppendLine($"  Now:                  {Tracks(file.Snapshot?.Tracks ?? [])}");
            if (file.Profile != null && file.Snapshot != null)
            {
                var keep = file.BuildTargetFromProfile(file.Profile).Tracks.Select(t => t.Index).ToHashSet();
                var removed = file.Snapshot.Tracks.Where(t => !keep.Contains(t.Index)).ToList();
                entry.AppendLine($"  Would remove now:     {(removed.Count > 0 ? Tracks(removed) : "nothing")}" +
                                 (file.HasNonStandardMetadata || file.HasRemovableChapters ? "; also needs a metadata fix" : ""));
            }

            (changed ? replaced : unchanged).Add(entry.ToString());
        }

        Section(sb, "Files that need work again after a conversion");
        sb.AppendLine($"Needing work: {files.Count}. Never converted: {never}. Converted, then replaced: {replaced.Count}. " +
                      $"Converted and unchanged, yet still needing work: {unchanged.Count}.");
        foreach (var entry in unchanged.Concat(replaced).Take(ListedFiles))
        {
            sb.Append(entry);
        }
    }

    private static string Tracks(IEnumerable<TrackSnapshot> tracks)
    {
        var listed = tracks.Where(t => t.Type != MediaTrackType.Video).OrderBy(t => t.Index)
            .Select(t =>
            {
                var flags = new List<string>();
                if (t.IsForced) flags.Add("forced");
                if (t.IsHearingImpaired) flags.Add("SDH");
                if (t.IsCommentary) flags.Add("commentary");
                var kind = t.Type == MediaTrackType.Audio ? "a" : "s";
                var name = string.IsNullOrEmpty(t.Name) ? "" : $" \"{t.Name}\"";
                return $"{kind}#{t.Index} {t.LanguageName}{name}{(flags.Count > 0 ? $" ({string.Join(", ", flags)})" : "")}";
            })
            .ToList();
        return listed.Count > 0 ? string.Join(", ", listed) : "(no audio or subtitles)";
    }

    private static async Task WriteConversions(StringBuilder sb, AppDbContext context, CancellationToken token)
    {
        Section(sb, "Conversions");
        var counts = await context.MediaConversions.AsNoTracking()
            .GroupBy(c => c.State).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(token);
        sb.AppendLine(counts.Count == 0
            ? "None yet."
            : string.Join(", ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key}: {c.Count}")));

        var recent = await context.MediaConversions.AsNoTracking()
            .Include(c => c.MediaFile)
            .OrderByDescending(c => c.Id)
            .Take(RecentConversions)
            .ToListAsync(token);
        if (recent.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"Most recent {recent.Count}:");
        foreach (var c in recent)
        {
            var took = c.StartedDate is { } started && c.State is ConversionState.Completed or ConversionState.Failed or ConversionState.Cancelled
                ? $"{(c.UpdatedDate - started).TotalSeconds:0}s"
                : "-";
            sb.AppendLine($"  #{c.Id} {c.State,-10} queued {Time(c.CreatedDate)}  took {took,-6} " +
                          $"{Size(c.SizeBefore)} -> {(c.SizeAfter > 0 ? Size(c.SizeAfter) : "-")}  " +
                          $"{(c.IsCustomConversion ? "[custom] " : "")}{c.Name}");
        }

        // Every failure has its log, then the latest of the rest for context.
        var withLogs = recent.Where(c => c.State is ConversionState.Failed or ConversionState.Cancelled)
            .Concat(recent.Where(c => c.State is not (ConversionState.Failed or ConversionState.Cancelled)).Take(10))
            .Take(ConversionLogs)
            .OrderByDescending(c => c.Id);
        foreach (var c in withLogs)
        {
            sb.AppendLine();
            sb.AppendLine($"--- #{c.Id} {c.Name} - {c.State} - {c.MediaFile?.Path ?? "(file no longer in the library)"}");
            sb.AppendLine(string.IsNullOrWhiteSpace(c.Log) ? "(no log)" : c.Log.TrimEnd());
        }
    }

    private static async Task WriteLog(StringBuilder sb, AppDbContext context, CancellationToken token)
    {
        var since = DateTime.UtcNow.AddDays(-LogDays);
        var entries = await context.LogEntries.AsNoTracking()
            .Where(e => e.Timestamp >= since)
            .OrderByDescending(e => e.Id)
            .Take(MaxLogLines)
            .ToListAsync(token);
        Section(sb, $"Application log (last {LogDays} days, {entries.Count} lines, oldest first)");
        foreach (var e in entries.OrderBy(e => e.Id))
        {
            sb.AppendLine($"{Time(e.Timestamp)} {e.Level,-3} {e.Source}: {e.Message}");
            if (!string.IsNullOrWhiteSpace(e.Exception))
            {
                foreach (var line in e.Exception.TrimEnd().Split('\n'))
                {
                    sb.AppendLine($"    {line.TrimEnd('\r')}");
                }
            }
        }
    }

    internal static string Redact(string? text)
    {
        return string.IsNullOrEmpty(text) ? "" : SecretLike.Replace(text, "[redacted]");
    }

    private static async Task<string> FirstLine(string tool, string arguments)
    {
        try
        {
            var result = await ProcessExecutor.ExecuteProcessAsync(tool, arguments, TimeSpan.FromSeconds(5));
            var line = (result.Output ?? "").Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
            return string.IsNullOrEmpty(line) ? $"not found (exit {result.ExitCode})" : line;
        }
        catch (Exception ex)
        {
            return $"not found ({ex.Message})";
        }
    }

    // The user and group the files are written as, which decides who else can change them.
    private static string ProcessIds()
    {
        try
        {
            var status = File.ReadAllLines("/proc/self/status");
            string Field(string name) =>
                status.FirstOrDefault(l => l.StartsWith(name + ":"))?.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries)
                    .ElementAtOrDefault(1) ?? "?";
            return $"uid {Field("Uid")}, gid {Field("Gid")}";
        }
        catch
        {
            return "unknown";
        }
    }

    private static void Section(StringBuilder sb, string title)
    {
        sb.AppendLine();
        sb.AppendLine($"== {title} ==");
    }

    private static DateTime ToLocal(DateTime utc)
    {
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
    }

    private static string Time(DateTime utc)
    {
        return ToLocal(utc).ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string FormatOffset(TimeSpan offset)
    {
        return (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm");
    }

    private static string YesNo(bool value)
    {
        return value ? "yes" : "no";
    }

    private static string Size(long bytes)
    {
        return bytes switch
        {
            >= 1L << 40 => $"{bytes / (double)(1L << 40):0.00} TB",
            >= 1L << 30 => $"{bytes / (double)(1L << 30):0.00} GB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
            _ => $"{bytes / 1024.0:0} KB"
        };
    }
}
