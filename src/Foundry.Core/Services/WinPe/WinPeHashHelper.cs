using System.Security.Cryptography;

namespace Foundry.Core.Services.WinPe;

internal static class WinPeHashHelper
{
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using FileStream stream = File.OpenRead(filePath);
        byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
