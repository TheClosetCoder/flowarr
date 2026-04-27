using Flowarr.Infrastructure.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/connections")]
public sealed class ConnectionsController(IOptions<IntegrationOptions> options) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var value = options.Value;
        return Ok(new
        {
            qBittorrent = new { configured = HasValue(value.QBittorrent.BaseUrl), value.QBittorrent.BaseUrl },
            radarr = new { configured = HasValue(value.Radarr.BaseUrl) && HasValue(value.Radarr.ApiKey), value.Radarr.BaseUrl },
            sonarr = new { configured = false, status = "placeholder" },
            plex = new { configured = false, status = "placeholder" },
            jellyfin = new { configured = false, status = "placeholder" }
        });
    }

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
}
