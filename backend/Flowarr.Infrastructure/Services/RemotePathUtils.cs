namespace Flowarr.Infrastructure.Services;

public static class RemotePathUtils
{
    public static string NormalizePosixPath(string path)
    {
        var isAbsolute = path.StartsWith('/');
        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
                else if (!isAbsolute)
                {
                    segments.Add(segment);
                }

                continue;
            }

            segments.Add(segment);
        }

        var normalized = string.Join('/', segments);
        return isAbsolute ? "/" + normalized : normalized;
    }
}
