using System.Reflection;

namespace Foundry;

/// <summary>
/// Provides product metadata and public project links used by the shell and about dialogs.
/// </summary>
public static class FoundryApplicationInfo
{
    public const string AppName = Constants.ApplicationDisplayName;

    /// <summary>
    /// Gets the documentation entry URL.
    /// </summary>
    public const string DocumentationUrl = "https://foundry-osd.github.io/docs/start";

    public const string RepositoryUrl = Constants.RepositoryUrl;

    /// <summary>
    /// Gets the GitHub contributors API endpoint.
    /// </summary>
    public const string ContributorsApiUrl = "https://api.github.com/repos/foundry-osd/foundry/contributors";

    /// <summary>
    /// Gets the issue tracker URL.
    /// </summary>
    public const string IssuesUrl = Constants.RepositoryUrl + "/issues";

    /// <summary>
    /// Gets the license document URL.
    /// </summary>
    public const string LicenseUrl = Constants.RepositoryUrl + "/blob/main/LICENSE";

    /// <summary>
    /// Gets the releases page URL.
    /// </summary>
    public const string ReleasesUrl = Constants.RepositoryUrl + "/releases";

    public const string LatestReleaseUrl = Constants.LatestReleaseUrl;

    /// <summary>
    /// Gets the support URL shown by user-facing dialogs.
    /// </summary>
    public const string SupportUrl = IssuesUrl;

    /// <summary>
    /// Gets the application version resolved from assembly metadata.
    /// </summary>
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        Assembly assembly = typeof(FoundryApplicationInfo).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Trim();
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    }
}
