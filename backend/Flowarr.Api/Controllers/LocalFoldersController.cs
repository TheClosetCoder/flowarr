using Microsoft.AspNetCore.Mvc;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/local-folders")]
public sealed class LocalFoldersController : ControllerBase
{
    [HttpGet]
    public ActionResult<LocalFolderListing> Browse([FromQuery] string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Ok(GetRootListing());
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            return NotFound(new { message = $"Folder not found: {fullPath}" });
        }

        return Ok(GetDirectoryListing(fullPath));
    }

    private static LocalFolderListing GetRootListing()
    {
        var roots = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName)
            .Concat(["/media", "/mnt", "/data", "/downloads", "/config"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .OrderBy(path => path)
            .Select(path => new LocalFolderEntry(GetDisplayName(path), path))
            .ToList();

        return new LocalFolderListing(string.Empty, null, roots);
    }

    private static LocalFolderListing GetDirectoryListing(string path)
    {
        var directory = new DirectoryInfo(path);
        var parentPath = directory.Parent?.FullName;
        var entries = directory
            .EnumerateDirectories()
            .Where(info => !info.Attributes.HasFlag(FileAttributes.Hidden) &&
                           !info.Attributes.HasFlag(FileAttributes.System))
            .OrderBy(info => info.Name)
            .Select(info => new LocalFolderEntry(info.Name, info.FullName))
            .ToList();

        return new LocalFolderListing(directory.FullName, parentPath, entries);
    }

    private static string GetDisplayName(string path)
    {
        return string.IsNullOrWhiteSpace(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            ? path
            : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}

public sealed record LocalFolderListing(string CurrentPath, string? ParentPath, IReadOnlyCollection<LocalFolderEntry> Entries);

public sealed record LocalFolderEntry(string Name, string Path);
