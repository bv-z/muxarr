using System.Text;

namespace Muxarr.Core.MkvToolNix;

/// <summary>
/// Shared utilities for mkvmerge and mkvpropedit command building.
/// </summary>
public static class MkvToolNixHelper
{
    /// <summary>
    /// Quotes one argument (a path, a title, a track name) so the tool receives it unchanged.
    /// .NET splits a command line by the Windows rules on Linux too: a quote is escaped with a
    /// backslash, and backslashes are literal except right before a quote, where they double.
    /// </summary>
    public static string EscapeValue(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
            }
            else
            {
                sb.Append('\\', backslashes);
            }

            sb.Append(c);
            backslashes = 0;
        }

        // Before the closing quote, so it is not taken as an escaped quote.
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}
