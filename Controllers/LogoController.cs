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
/// Controller for managing custom logo uploads.
/// </summary>
[ApiController]
[Route("logoswap")]
public class LogoController : ControllerBase
{
    private const long MaxLogoBytes = 4 * 1024 * 1024;

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
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<string>> UploadLogo([Required] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        if (file.Length > MaxLogoBytes)
        {
            return BadRequest("Logo must be 4 MiB or smaller.");
        }

        // Validate the file content, not the client-supplied content type
        await using (var probe = file.OpenReadStream())
        {
            if (!await IsPngAsync(probe).ConfigureAwait(false))
            {
                return BadRequest("Only PNG files are accepted.");
            }
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
    /// Checks whether a stream starts with the PNG file signature.
    /// </summary>
    /// <param name="stream">The stream to inspect.</param>
    /// <returns>True if the stream starts with the PNG signature.</returns>
    private static async Task<bool> IsPngAsync(Stream stream)
    {
        var header = new byte[8];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false).ConfigureAwait(false);

        return read == header.Length
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
    }

    /// <summary>
    /// Gets the current custom logo.
    /// </summary>
    /// <response code="200">Returns the custom logo image.</response>
    /// <response code="304">Logo not modified (ETag matches).</response>
    /// <response code="404">No custom logo found.</response>
    /// <returns>The logo image file.</returns>
    [HttpGet("image")]
    [AllowAnonymous]
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
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
}
