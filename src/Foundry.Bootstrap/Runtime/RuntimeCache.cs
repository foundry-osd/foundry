// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap.Runtime;

/// <summary>Promotes a staged payload while retaining the previous cache until the move succeeds.</summary>
internal sealed class RuntimeCache(Action<string, string>? moveDirectory = null)
{
    /// <summary>Restores the previous active directory if staging cannot become active.</summary>
    public void Promote(string stagingRoot, string cacheRoot)
    {
        Action<string, string> move = moveDirectory ?? Directory.Move;
        string backup = cacheRoot + ".previous";
        DeleteDirectory(backup);
        bool activeMoved = false;
        try
        {
            if (Directory.Exists(cacheRoot))
            {
                move(cacheRoot, backup);
                activeMoved = true;
            }
            move(stagingRoot, cacheRoot);
        }
        catch
        {
            if (activeMoved && !Directory.Exists(cacheRoot) && Directory.Exists(backup))
                move(backup, cacheRoot);
            throw;
        }
        DeleteDirectory(backup);
    }

    /// <summary>Removes only a caller-owned cache work directory.</summary>
    internal static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
