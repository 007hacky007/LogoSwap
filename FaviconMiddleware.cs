using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace LogoSwap;

/// <summary>
/// Points the web client's favicon links at the uploaded favicon.
/// </summary>
/// <remarks>
/// The favicon is a link in the web client's index.html, which Jellyfin serves as a
/// static file and branding CSS cannot reach. This rewrites that page as it is served,
/// so nothing on disk changes and removing the favicon restores the original page.
/// </remarks>
public class FaviconMiddleware
{
    private static readonly Regex LinkTag = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RelAttribute = new(@"\brel\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HrefAttribute = new(@"\bhref\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="FaviconMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public FaviconMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Handles a request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the request is handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var favicon = HttpMethods.IsGet(context.Request.Method) && IsWebIndex(context.Request.Path)
            ? CustomFavicon.Find()
            : null;

        if (favicon == null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Ask for the plain file: uncompressed so it can be edited, and never a 304 or a
        // range, since the rewritten page carries its own validator
        var request = context.Request;
        var clientEtag = request.Headers.IfNoneMatch.ToString();
        request.Headers.Remove(HeaderNames.AcceptEncoding);
        request.Headers.Remove(HeaderNames.IfNoneMatch);
        request.Headers.Remove(HeaderNames.IfModifiedSince);
        request.Headers.Remove(HeaderNames.Range);

        var response = context.Response;
        var originalBody = response.Body;
        using var buffer = new MemoryStream();
        response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            response.Body = originalBody;
        }

        buffer.Position = 0;
        if (response.StatusCode != StatusCodes.Status200OK
            || response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) != true)
        {
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            return;
        }

        // Relative to the web client's /web/ page, so it resolves under a base URL or a
        // reverse proxy prefix alike. The version makes browsers fetch a replaced favicon.
        var faviconUrl = "../logoswap/favicon/image?v=" + CustomFavicon.Version(favicon);
        var isPng = favicon.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
        var html = RewriteIcons(Encoding.UTF8.GetString(buffer.ToArray()), faviconUrl, isPng);
        var body = Encoding.UTF8.GetBytes(html);

        var etag = "\"logoswap-" + Convert.ToHexString(SHA256.HashData(body), 0, 8) + "\"";
        response.Headers.ETag = etag;
        response.Headers.Remove(HeaderNames.LastModified);

        if (clientEtag == etag)
        {
            response.StatusCode = StatusCodes.Status304NotModified;
            response.ContentLength = null;
            return;
        }

        response.ContentLength = body.Length;
        await originalBody.WriteAsync(body).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the favicon links in a page, adding one if the page has none.
    /// </summary>
    /// <param name="html">The page.</param>
    /// <param name="faviconUrl">The favicon URL to link to.</param>
    /// <param name="isPng">Whether the favicon is a PNG, which the apple-touch-icon requires.</param>
    /// <returns>The rewritten page.</returns>
    internal static string RewriteIcons(string html, string faviconUrl, bool isPng)
    {
        var href = "href=\"" + faviconUrl + "\"";
        var replacedIcon = false;

        var result = LinkTag.Replace(html, match =>
        {
            var rel = RelAttribute.Match(match.Value);
            if (!rel.Success)
            {
                return match.Value;
            }

            var tokens = rel.Groups["v"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var isIcon = tokens.Contains("icon", StringComparer.OrdinalIgnoreCase);
            var isTouchIcon = tokens.Any(t => t.StartsWith("apple-touch-icon", StringComparison.OrdinalIgnoreCase));

            if (!isIcon && !(isTouchIcon && isPng))
            {
                return match.Value;
            }

            replacedIcon |= isIcon;
            return HrefAttribute.Replace(match.Value, href, 1);
        });

        if (!replacedIcon)
        {
            var headEnd = result.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (headEnd >= 0)
            {
                result = result.Insert(headEnd, "<link rel=\"icon\" " + href + ">");
            }
        }

        return result;
    }

    private static bool IsWebIndex(PathString path)
    {
        // The request arrives before Jellyfin strips its base URL, so match the end of
        // the path: /web/ and /web/index.html, with or without a base URL in front
        var value = path.Value;
        return value != null
            && (value.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Adds <see cref="FaviconMiddleware"/> to the front of Jellyfin's request pipeline.
/// </summary>
public class FaviconStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<FaviconMiddleware>();
            next(app);
        };
    }
}
