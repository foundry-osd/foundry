using System.Reflection;

namespace Foundry;

public static class FoundryApplicationInfo
{
    public const string AppName = Constants.ApplicationName;
    public const string DocumentationUrl = "https://foundry-osd.github.io/docs/intro";
    public const string RepositoryUrl = Constants.RepositoryUrl;
    public const string IssuesUrl = Constants.RepositoryUrl + "/issues";
    public const string LicenseUrl = Constants.RepositoryUrl + "/blob/main/LICENSE";
    public const string ReleasesUrl = Constants.RepositoryUrl + "/releases";
    public const string LatestReleaseUrl = Constants.LatestReleaseUrl;
    public const string SupportUrl = IssuesUrl;

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
