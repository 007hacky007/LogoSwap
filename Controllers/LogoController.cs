using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LogoSwap.Controllers;

/// <summary>
/// Controller for managing custom logo uploads.
/// </summary>
[ApiController]
[Route("logoswap")]
public class LogoController : ControllerBase
{
    private readonly ILogger<LogoController> _logger;
    private readonly IApplicationPaths _applicationPaths;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogoController"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{LogoController}"/> interface.</param>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    public LogoController(ILogger<LogoController> logger, IApplicationPaths applicationPaths)
    {
        _logger = logger;
        _applicationPaths = applicationPaths;
    }

    /// <summary>
    /// Uploads a custom logo.
    /// </summary>
    /// <param name="file">The logo image file (PNG format).</param>
    /// <response code="200">Logo uploaded successfully.</response>
    /// <response code="400">No file uploaded or invalid file.</response>
    /// <response code="500">Error saving file.</response>
    /// <returns>A status message.</returns>
    [HttpPost("upload")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<string>> UploadLogo([Required] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        // Validate file type
        if (!file.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only PNG files are accepted.");
        }

        try
        {
            // Use plugin configurations path for storing the logo
            var pluginDataPath = Path.Combine(_applicationPaths.PluginConfigurationsPath, "LogoSwap");
            
            // Ensure directory exists
            Directory.CreateDirectory(pluginDataPath);
            
            var logoPath = Path.Combine(pluginDataPath, "logo.png");

            _logger.LogInformation("LogoSwap: Uploading custom logo to: {Path}", logoPath);

            await using var stream = new FileStream(logoPath, FileMode.Create);
            await file.CopyToAsync(stream).ConfigureAwait(false);

            // Update plugin configuration
            if (Plugin.Instance != null)
            {
                Plugin.Instance.Configuration.LogoPath = logoPath;
                Plugin.Instance.SaveConfiguration();
            }

            _logger.LogInformation("LogoSwap: Custom logo uploaded successfully");
            return Ok("Logo uploaded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogoSwap: Error uploading custom logo");
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error saving file: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the current custom logo.
    /// </summary>
    /// <response code="200">Returns the custom logo image.</response>
    /// <response code="304">Logo not modified (ETag matches).</response>
    /// <response code="404">No custom logo found.</response>
    /// <returns>The logo image file.</returns>
    [HttpGet("image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetLogo()
    {
        var logoPath = Plugin.Instance?.Configuration.LogoPath;

        if (string.IsNullOrEmpty(logoPath) || !System.IO.File.Exists(logoPath))
        {
            return NotFound("No custom logo configured.");
        }

        var fileInfo = new FileInfo(logoPath);
        var etag = $"\"{fileInfo.LastWriteTimeUtc.Ticks:X}\"";

        // Check If-None-Match header for conditional request
        if (Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch)
            && ifNoneMatch.ToString() == etag)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        // Set caching headers
        Response.Headers["ETag"] = etag;
        Response.Headers["Cache-Control"] = "no-cache, must-revalidate";

        var fileStream = System.IO.File.OpenRead(logoPath);
        return File(fileStream, "image/png");
    }

    /// <summary>
    /// Deletes the current custom logo.
    /// </summary>
    /// <response code="200">Logo deleted successfully.</response>
    /// <response code="404">No custom logo found.</response>
    /// <returns>A status message.</returns>
    [HttpDelete("delete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteLogo()
    {
        var logoPath = Plugin.Instance?.Configuration.LogoPath;

        if (string.IsNullOrEmpty(logoPath) || !System.IO.File.Exists(logoPath))
        {
            return NotFound("No custom logo to delete.");
        }

        try
        {
            System.IO.File.Delete(logoPath);
            
            if (Plugin.Instance != null)
            {
                Plugin.Instance.Configuration.LogoPath = string.Empty;
                Plugin.Instance.SaveConfiguration();
            }

            _logger.LogInformation("LogoSwap: Custom logo deleted successfully");
            return Ok("Logo deleted successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LogoSwap: Error deleting custom logo");
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error deleting file: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if a custom logo is configured.
    /// </summary>
    /// <response code="200">Returns status of custom logo.</response>
    /// <returns>Logo status information.</returns>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetStatus()
    {
        var logoPath = Plugin.Instance?.Configuration.LogoPath;
        var hasLogo = !string.IsNullOrEmpty(logoPath) && System.IO.File.Exists(logoPath);
        
        return Ok(new { hasLogo, logoUrl = hasLogo ? "/logoswap/image" : null });
    }

    /// <summary>
    /// Gets the JavaScript code for logo injection.
    /// </summary>
    /// <response code="200">Returns the JavaScript code.</response>
    /// <returns>JavaScript code for branding injection.</returns>
    [HttpGet("script")]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<string> GetScript()
    {
        return Content(LogoInjector.GetInjectionScript(), "application/javascript");
    }

    private static readonly Dictionary<string, string> AllowedFaviconTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "image/png", ".png" },
        { "image/svg+xml", ".svg" },
        { "image/x-icon", ".ico" },
        { "image/vnd.microsoft.icon", ".ico" },
    };

    private static readonly Dictionary<string, string> FaviconExtensionToMime = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".png", "image/png" },
        { ".svg", "image/svg+xml" },
        { ".ico", "image/x-icon" },
    };

    /// <summary>
    /// Uploads a custom favicon.
    /// </summary>
    /// <param name="file">The favicon image file (PNG, SVG, or ICO format).</param>
    /// <response code="200">Favicon uploaded successfully.</response>
    /// <response code="400">No file uploaded or invalid file type.</response>
    /// <response code="500">Error saving file.</response>
    /// <returns>A status message.</returns>
    [HttpPost("favicon/upload")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<string>> UploadFavicon([Required] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        if (!AllowedFaviconTypes.TryGetValue(file.ContentType, out var extension))
        {
            return BadRequest("Only PNG, SVG, and ICO files are accepted.");
        }

        try
        {
            var pluginDataPath = Path.Combine(_applicationPaths.PluginConfigurationsPath, "LogoSwap");
            Directory.CreateDirectory(pluginDataPath);

            // Remove any existing favicon files
            foreach (var existing in Directory.GetFiles(pluginDataPath, "favicon.*"))
            {
                System.IO.File.Delete(existing);
            }

            var faviconPath = Path.Combine(pluginDataPath, "favicon" + extension);

            _logger.LogInformation("LogoSwap: Uploading custom favicon to: {Path}", faviconPath);

            await using var stream = new FileStream(faviconPath, FileMode.Create);
            await file.CopyToAsync(stream).ConfigureAwait(false);

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
    [HttpGet("favicon/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetFavicon()
    {
        var faviconPath = Plugin.Instance?.Configuration.FaviconPath;

        if (string.IsNullOrEmpty(faviconPath) || !System.IO.File.Exists(faviconPath))
        {
            return NotFound("No custom favicon configured.");
        }

        var fileInfo = new FileInfo(faviconPath);
        var etag = $"\"{fileInfo.LastWriteTimeUtc.Ticks:X}\"";

        if (Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch)
            && ifNoneMatch.ToString() == etag)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers["ETag"] = etag;
        Response.Headers["Cache-Control"] = "no-cache, must-revalidate";

        var ext = Path.GetExtension(faviconPath);
        var contentType = FaviconExtensionToMime.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";

        var fileStream = System.IO.File.OpenRead(faviconPath);
        return File(fileStream, contentType);
    }

    /// <summary>
    /// Deletes the current custom favicon.
    /// </summary>
    /// <response code="200">Favicon deleted successfully.</response>
    /// <response code="404">No custom favicon found.</response>
    /// <returns>A status message.</returns>
    [HttpDelete("favicon/delete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteFavicon()
    {
        var faviconPath = Plugin.Instance?.Configuration.FaviconPath;

        if (string.IsNullOrEmpty(faviconPath) || !System.IO.File.Exists(faviconPath))
        {
            return NotFound("No custom favicon to delete.");
        }

        try
        {
            System.IO.File.Delete(faviconPath);

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
    [HttpGet("favicon/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetFaviconStatus()
    {
        var faviconPath = Plugin.Instance?.Configuration.FaviconPath;
        var hasFavicon = !string.IsNullOrEmpty(faviconPath) && System.IO.File.Exists(faviconPath);

        return Ok(new { hasFavicon, faviconUrl = hasFavicon ? "/logoswap/favicon/image" : null });
    }
}
