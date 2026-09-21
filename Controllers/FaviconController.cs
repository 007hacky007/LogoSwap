using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LogoSwap.Controllers;

/// <summary>
/// Controller for managing the custom favicon.
/// </summary>
[ApiController]
[Route("logoswap/favicon")]
public class FaviconController : ControllerBase
{
    private const long MaxFaviconBytes = 512 * 1024;

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] IcoSignature = { 0x00, 0x00, 0x01, 0x00 };

    private readonly ILogger<FaviconController> _logger;
    private readonly IApplicationPaths _applicationPaths;

    /// <summary>
    /// Initializes a new instance of the <see cref="FaviconController"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{FaviconController}"/> interface.</param>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    public FaviconController(ILogger<FaviconController> logger, IApplicationPaths applicationPaths)
    {
        _logger = logger;
        _applicationPaths = applicationPaths;
    }

    /// <summary>
    /// Uploads a custom favicon.
    /// </summary>
    /// <param name="file">The favicon image file (PNG, SVG, or ICO format).</param>
    /// <response code="200">Favicon uploaded successfully.</response>
    /// <response code="400">No file uploaded or invalid file.</response>
    /// <response code="500">Error saving file.</response>
    /// <returns>A status message.</returns>
    [HttpPost("upload")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<string>> UploadFavicon([Required] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        if (file.Length > MaxFaviconBytes)
        {
            return BadRequest("Favicon must be 512 KiB or smaller.");
        }

        // Trust the file content, not the client-supplied content type
        string? extension;
        await using (var probe = file.OpenReadStream())
        {
            extension = await DetectExtensionAsync(probe).ConfigureAwait(false);
        }

        if (extension == null)
        {
            return BadRequest("Only PNG, SVG, and ICO files are accepted.");
        }

        try
        {
            var pluginDataPath = Path.Combine(_applicationPaths.PluginConfigurationsPath, "LogoSwap");
            Directory.CreateDirectory(pluginDataPath);

            // The format may change between uploads, so drop any previous favicon first
            foreach (var existing in Directory.GetFiles(pluginDataPath, "favicon.*"))
            {
                System.IO.File.Delete(existing);
            }

            var faviconPath = Path.Combine(pluginDataPath, "favicon" + extension);

            _logger.LogInformation("LogoSwap: Uploading custom favicon to: {Path}", faviconPath);

            await using (var stream = new FileStream(faviconPath, FileMode.Create))
            {
                await file.CopyToAsync(stream).ConfigureAwait(false);
            }

            if (Plugin.Instance != null)
            {
                Plugin.Instance.Configuration.FaviconPath = faviconPath;
                Plugin.Instance.SaveConfiguration();
            }

            _logger.LogInformation("LogoSwap: Custom favicon uploaded successfully");
            return Ok("Favicon uploaded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogoSwap: Error uploading custom favicon");
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error saving file: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the current custom favicon.
    /// </summary>
    /// <response code="200">Returns the custom favicon image.</response>
    /// <response code="304">Favicon not modified (ETag matches).</response>
    /// <response code="404">No custom favicon found.</response>
    /// <returns>The favicon image file.</returns>
    [HttpGet("image")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetFavicon()
    {
        var favicon = CustomFavicon.Find();
        if (favicon == null)
        {
            return NotFound("No custom favicon configured.");
        }

        var etag = $"\"{CustomFavicon.Version(favicon)}\"";

        if (Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch)
            && ifNoneMatch.ToString() == etag)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers["ETag"] = etag;
        Response.Headers["Cache-Control"] = "no-cache, must-revalidate";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        var contentType = CustomFavicon.MimeTypes.TryGetValue(favicon.Extension, out var mime) ? mime : "application/octet-stream";
        if (contentType == "image/svg+xml")
        {
            // Never let an uploaded SVG run script if someone opens it directly
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        }

        return File(favicon.OpenRead(), contentType);
    }

    /// <summary>
    /// Deletes the current custom favicon.
    /// </summary>
    /// <response code="200">Favicon deleted successfully.</response>
    /// <response code="404">No custom favicon found.</response>
    /// <returns>A status message.</returns>
    [HttpDelete("delete")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteFavicon()
    {
        var favicon = CustomFavicon.Find();
        if (favicon == null)
        {
            return NotFound("No custom favicon to delete.");
        }

        try
        {
            favicon.Delete();

            if (Plugin.Instance != null)
            {
                Plugin.Instance.Configuration.FaviconPath = string.Empty;
                Plugin.Instance.SaveConfiguration();
            }

            _logger.LogInformation("LogoSwap: Custom favicon deleted successfully");
            return Ok("Favicon deleted successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogoSwap: Error deleting custom favicon");
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error deleting file: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if a custom favicon is configured.
    /// </summary>
    /// <response code="200">Returns status of custom favicon.</response>
    /// <returns>Favicon status information.</returns>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetFaviconStatus()
    {
        var hasFavicon = CustomFavicon.Find() != null;

        return Ok(new { hasFavicon, faviconUrl = hasFavicon ? Request.PathBase + "/logoswap/favicon/image" : null });
    }

    /// <summary>
    /// Determines the favicon format from the file content.
    /// </summary>
    /// <param name="stream">The uploaded file stream.</param>
    /// <returns>".png", ".ico" or ".svg", or null if the content is not a recognised favicon format.</returns>
    private static async Task<string?> DetectExtensionAsync(Stream stream)
    {
        var header = new byte[1024];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false).ConfigureAwait(false);

        if (read >= PngSignature.Length && header.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            return ".png";
        }

        if (read >= IcoSignature.Length && header.AsSpan(0, IcoSignature.Length).SequenceEqual(IcoSignature))
        {
            return ".ico";
        }

        // SVG: text starting (after an optional BOM and whitespace) with an XML declaration, doctype, comment or <svg root
        var text = System.Text.Encoding.UTF8.GetString(header, 0, read).TrimStart('﻿', ' ', '\t', '\r', '\n');
        if ((text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
             || text.StartsWith("<!DOCTYPE svg", StringComparison.OrdinalIgnoreCase)
             || text.StartsWith("<!--", StringComparison.Ordinal)
             || text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
            && text.Contains("<svg", StringComparison.OrdinalIgnoreCase))
        {
            return ".svg";
        }

        return null;
    }
}
