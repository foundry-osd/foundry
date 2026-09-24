// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Publishes owned state and hooks without exposing partially written replacements.</summary>
internal static class DeploymentFilePublication
{
    public static void WriteAllText(string path, string content, Encoding encoding)
    {
        string candidate = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = encoding.GetBytes(content);
            using (var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) { File.Replace(candidate, path, null); }
            else { File.Move(candidate, path); }
        }
        finally
        {
            if (File.Exists(candidate)) { File.Delete(candidate); }
        }
    }
}
