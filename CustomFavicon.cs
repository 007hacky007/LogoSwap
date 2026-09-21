using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LogoSwap;

/// <summary>
/// Locates the uploaded favicon and describes it.
/// </summary>
internal static class CustomFavicon
{
    /// <summary>
    /// The accepted favicon formats, by file extension.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> MimeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { ".png", "image/png" },
        { ".svg", "image/svg+xml" },
        { ".ico", "image/x-icon" },
    };

    /// <summary>
    /// Gets the uploaded favicon file, or null when none is configured.
    /// </summary>
    /// <returns>The favicon file, or null.</returns>
    public static FileInfo? Find()
    {
        var path = Plugin.Instance?.Configuration.FaviconPath;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var file = new FileInfo(path);
        return file.Exists ? file : null;
    }

    /// <summary>
    /// Gets a version string that changes whenever the favicon is replaced.
    /// </summary>
    /// <param name="file">The favicon file.</param>
    /// <returns>The version string.</returns>
    public static string Version(FileInfo file)
        => file.LastWriteTimeUtc.Ticks.ToString("X", CultureInfo.InvariantCulture);
}
