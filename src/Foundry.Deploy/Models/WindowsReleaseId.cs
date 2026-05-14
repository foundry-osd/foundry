namespace Foundry.Deploy.Models;

internal static class WindowsReleaseId
{
    public static int GetSortRank(string releaseId)
    {
        string normalized = releaseId.Trim().ToLowerInvariant();
        if (normalized.Length != 4 ||
            !int.TryParse(normalized[..2], out int year) ||
            normalized[2] != 'h' ||
            !int.TryParse(normalized[3..], out int half))
        {
            return 0;
        }

        return year * 10 + half;
    }
}
