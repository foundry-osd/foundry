namespace Foundry.Services.ApplicationUpdate;

public sealed record ApplicationUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string ReleaseTitle,
    string ReleaseUrl,
    DateTimeOffset? PublishedAt,
    string ReleaseNotes)
{
    public string SummaryReleaseTitle =>
        string.IsNullOrWhiteSpace(ReleaseTitle)
            ? LatestVersion
            : ReleaseTitle;
}
