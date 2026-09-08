using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogoSwap;

/// <summary>
/// Hosted service that runs on startup to log plugin status.
/// The logo itself is swapped by the CSS the plugin writes into Jellyfin's branding settings.
/// </summary>
public class LogoInjector : IHostedService
{
    private readonly ILogger<LogoInjector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogoInjector"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{LogoInjector}"/> interface.</param>
    public LogoInjector(ILogger<LogoInjector> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("LogoSwap: Started successfully");
        
        var logoPath = Plugin.Instance?.Configuration.LogoPath;
        if (!string.IsNullOrEmpty(logoPath) && System.IO.File.Exists(logoPath))
        {
            _logger.LogInformation("LogoSwap: Custom logo configured at {Path}", logoPath);
        }
        else
        {
            _logger.LogInformation("LogoSwap: No custom logo configured. Upload one via the plugin settings.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
