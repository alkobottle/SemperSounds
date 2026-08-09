namespace SemperSounds.Core.Configuration;

/// <summary>
/// Hosting-level settings. Bound from the "App" configuration section.
/// </summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// The externally visible base URL, e.g. https://sounds.example.com.
    /// Behind a TLS-terminating reverse proxy the app would otherwise build an http://
    /// OAuth redirect_uri and Discord would reject it, so this pins the correct value.
    /// Leave empty when running directly (local development).
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    public bool HasPublicBaseUrl => !string.IsNullOrWhiteSpace(PublicBaseUrl);

    /// <summary>
    /// Short git commit the image was built from, supplied as a Docker build argument.
    /// The container has no .git directory, so it cannot work this out for itself.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>When the image was built, distinguishing rebuilds of the same commit.</summary>
    public string BuiltAt { get; set; } = string.Empty;

    /// <summary>What to show in the UI. "dev" means it was not built through Docker.</summary>
    public string DisplayVersion => string.IsNullOrWhiteSpace(Version) ? "dev" : Version;
}
