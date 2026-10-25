namespace Flowarr.Infrastructure.Services;

/// <summary>
/// A saved secret is only ever sent to the server it was saved for. Saving or testing a connection
/// with a blank secret field reuses the stored one, so without this check anyone holding a session
/// could point a connection at a server they control and have Flowarr hand over the stored API key,
/// token or password - either straight away through a connection test, or on the next sync after a save.
/// Changing the target therefore requires typing the secret again.
/// </summary>
public static class SecretReusePolicy
{
    /// <summary>
    /// Throws when the request leaves the secret blank (meaning "keep the saved one") while pointing the
    /// connection at a different target than the one the secret was saved with.
    /// </summary>
    public static void EnsureSavedSecretStaysWithTarget(
        string? requestedSecret,
        bool clearSecret,
        bool hasSavedSecret,
        bool targetChanged,
        string secretLabel,
        string targetLabel)
    {
        if (string.IsNullOrWhiteSpace(requestedSecret) && !clearSecret && hasSavedSecret && targetChanged)
        {
            throw new SecretReuseRefusedException(
                $"Re-enter the {secretLabel} when changing the {targetLabel}. A saved {secretLabel} is only sent to the server it was saved for.");
        }
    }

    /// <summary>
    /// A blank requested URL means "keep the saved one", so it is not a change. Otherwise the scheme, host
    /// and port must match: a different path on the same server is fine, but a different server or a
    /// downgrade from https to http is not.
    /// </summary>
    public static bool IsUrlChange(string? requestedUrl, string? savedUrl)
    {
        if (string.IsNullOrWhiteSpace(requestedUrl))
        {
            return false;
        }

        if (Uri.TryCreate(requestedUrl.Trim(), UriKind.Absolute, out var requested) &&
            Uri.TryCreate(savedUrl?.Trim(), UriKind.Absolute, out var saved))
        {
            return !requested.Scheme.Equals(saved.Scheme, StringComparison.OrdinalIgnoreCase) ||
                   !requested.IdnHost.Equals(saved.IdnHost, StringComparison.OrdinalIgnoreCase) ||
                   requested.Port != saved.Port;
        }

        return !requestedUrl.Trim().TrimEnd('/').Equals(savedUrl?.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Same convention as <see cref="IsUrlChange"/>: a blank host or a non-positive port keeps the saved value.</summary>
    public static bool IsHostChange(string? requestedHost, int requestedPort, string? savedHost, int savedPort)
    {
        var hostChanged = !string.IsNullOrWhiteSpace(requestedHost) &&
                          !requestedHost.Trim().Equals(savedHost?.Trim(), StringComparison.OrdinalIgnoreCase);
        var portChanged = requestedPort > 0 && requestedPort != savedPort;
        return hostChanged || portChanged;
    }
}

public sealed class SecretReuseRefusedException(string message) : InvalidOperationException(message);
