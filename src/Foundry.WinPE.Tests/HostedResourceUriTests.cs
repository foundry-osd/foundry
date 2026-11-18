// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;
using Foundry.WinPE.Tests.Support;
using Xunit;

namespace Foundry.WinPE.Tests;

public sealed partial class HostedResourceUriTests
{
    // Foundry.WinPE is the entry assembly, so a root-relative application URI resolves against it, not the hosted library.
    [Theory]
    [InlineData("Foundry.Connect")]
    [InlineData("Foundry.Deploy")]
    public void HostedLibrarySourcesQualifyApplicationResourceUrisWithTheirComponent(string project)
    {
        string projectRoot = Path.Combine(RepositoryRoot.Find(), "src", project);
        string[] violations = Directory.EnumerateFiles(projectRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsBuildOutput(projectRoot, path))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, number: index + 1)))
            .SelectMany(entry => RootRelativeResourceUri().Matches(entry.line)
                .Select(match => $"{Path.GetRelativePath(projectRoot, entry.path)}:{entry.number}: {match.Value}"))
            .ToArray();

        Assert.Empty(violations);
    }

    private static bool IsBuildOutput(string projectRoot, string path)
    {
        string firstSegment = Path.GetRelativePath(projectRoot, path).Split(Path.DirectorySeparatorChar)[0];
        return firstSegment is "bin" or "obj";
    }

    [GeneratedRegex("""
        "(?:pack://application:,,,)?/(?!/)(?![^"]*;component/)[^"]*\.(?:xaml|ico|png|jpe?g|gif|bmp|svg|ttf|otf)"
        """, RegexOptions.IgnoreCase)]
    private static partial Regex RootRelativeResourceUri();
}
