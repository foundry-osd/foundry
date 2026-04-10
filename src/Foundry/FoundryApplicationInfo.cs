using System.Reflection;

namespace Foundry;

public static class FoundryApplicationInfo
{
    private const string RepositoryBaseUrl = "https://github.com/mchave3/Foundry";
    private static readonly string AppVersion = ResolveVersion();

    public const string AppName = "Foundry";
    public const string DocumentationUrl = RepositoryBaseUrl + "#readme";
    public const string RepositoryUrl = RepositoryBaseUrl;
    public const string IssuesUrl = RepositoryBaseUrl + "/issues";
    public const string LatestReleaseUrl = RepositoryBaseUrl + "/releases/latest";
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/mchave3/Foundry/releases/latest";
    public const string LicenseUrl = RepositoryBaseUrl + "/blob/main/LICENSE";
    public const string AuthorsUrl = RepositoryBaseUrl + "/graphs/contributors";
    public const string SupportUrl = IssuesUrl;
    public static string LatestReleaseDisplayUrl => FormatDisplayUrl(LatestReleaseUrl);

    public static string Version => AppVersion;

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

    private static string FormatDisplayUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return uri.Host + uri.AbsolutePath;
        }

        return url;
    }
}
