using Flowarr.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Renci.SshNet;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/seedbox-folders")]
public sealed class SeedboxFoldersController(IConnectionSettingsService connectionSettingsService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Browse([FromQuery] string kind = "Download", [FromQuery] string? path = null, CancellationToken cancellationToken = default)
    {
        var settings = await connectionSettingsService.GetSftpSeedboxAsync(cancellationToken);
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Username))
        {
            return BadRequest(new { message = "SFTP seedbox is not configured." });
        }

        var roots = settings.SeedboxRoots
            .Where(root => root.Enabled && root.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (string.IsNullOrWhiteSpace(path))
        {
            return Ok(new SeedboxFolderListing(string.Empty, null, roots.Select(root => new SeedboxFolderEntry(root.Name, root.Path)).ToList()));
        }

        var normalized = path.TrimEnd('/');
        if (!roots.Any(root => normalized.Equals(root.Path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
                               normalized.StartsWith(root.Path.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new { message = $"Path is outside configured {kind} seedbox roots." });
        }

        return await Task.Run<IActionResult>(() =>
        {
            using var client = CreateClient(settings);
            client.Connect();
            var parent = GetParentWithinRoots(normalized, roots.Select(root => root.Path));
            var entries = client.ListDirectory(normalized)
                .Where(entry => entry.IsDirectory && entry.Name is not "." and not "..")
                .OrderBy(entry => entry.Name)
                .Select(entry => new SeedboxFolderEntry(entry.Name, entry.FullName))
                .ToList();
            client.Disconnect();
            return Ok(new SeedboxFolderListing(normalized, parent, entries));
        }, cancellationToken);
    }

    private static string? GetParentWithinRoots(string path, IEnumerable<string> roots)
    {
        var rootList = roots.Select(root => root.TrimEnd('/')).ToList();
        if (rootList.Any(root => path.Equals(root, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var index = path.LastIndexOf('/');
        return index <= 0 ? null : path[..index];
    }

    private static SftpClient CreateClient(SftpSeedboxConnectionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrWhiteSpace(settings.PrivateKeyPassphrase)
                ? new PrivateKeyFile(settings.PrivateKeyPath)
                : new PrivateKeyFile(settings.PrivateKeyPath, settings.PrivateKeyPassphrase);
            return new SftpClient(settings.Host, settings.Port, settings.Username, keyFile);
        }

        return new SftpClient(settings.Host, settings.Port, settings.Username, settings.Password);
    }
}

public sealed record SeedboxFolderListing(string CurrentPath, string? ParentPath, IReadOnlyCollection<SeedboxFolderEntry> Entries);

public sealed record SeedboxFolderEntry(string Name, string Path);
