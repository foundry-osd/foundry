// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// A unique folder under <c>%TEMP%\Foundry.Connect.Tests</c>, deleted best-effort on dispose.
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry.Connect.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateFile(string relativePath, string contents)
    {
        string filePath = PrepareFilePath(relativePath);
        File.WriteAllText(filePath, contents);
        return filePath;
    }

    public string CreateFile(string relativePath, byte[] contents)
    {
        string filePath = PrepareFilePath(relativePath);
        File.WriteAllBytes(filePath, contents);
        return filePath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked file must not fail the test; the folder lives under %TEMP%.
        }
    }

    private string PrepareFilePath(string relativePath)
    {
        string filePath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath)!);
        return filePath;
    }
}
