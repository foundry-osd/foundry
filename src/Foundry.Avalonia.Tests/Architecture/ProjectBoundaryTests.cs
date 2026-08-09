// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Xml.Linq;

namespace Foundry.Avalonia.Tests.Architecture;

public sealed class ProjectBoundaryTests
{
    private static readonly string[] ForbiddenNamespaceFragments =
    [
        "Foundry.Connect",
        "Foundry.Deploy",
        "Foundry.Core",
        "Foundry.Services",
        "Foundry.Utilities",
        "Foundry.Localization",
        "Foundry.Telemetry",
    ];

    [Fact]
    public void SharedProjectHasNoProjectReferences()
    {
        string projectDirectory = GetSharedProjectDirectory();
        XDocument project = XDocument.Load(Path.Combine(projectDirectory, "Foundry.Avalonia.csproj"));

        XElement[] projectReferences = project
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .ToArray();

        Assert.Empty(projectReferences);
    }

    [Fact]
    public void SharedSourceDoesNotReferenceApplicationOrDomainNamespaces()
    {
        string projectDirectory = GetSharedProjectDirectory();
        string[] sourceFiles = Directory
            .EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".axaml")
            .Where(path => !IsBuildOutput(path, projectDirectory))
            .ToArray();

        var violations = new List<string>();
        foreach (string sourceFile in sourceFiles)
        {
            string contents = File.ReadAllText(sourceFile);
            foreach (string forbiddenNamespace in ForbiddenNamespaceFragments)
            {
                if (contents.Contains(forbiddenNamespace, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{Path.GetRelativePath(projectDirectory, sourceFile)} references {forbiddenNamespace}.");
                }
            }
        }

        Assert.Empty(violations);
    }

    private static string GetSharedProjectDirectory() =>
        Path.Combine(FindSourceRoot(), "Foundry.Avalonia");

    private static string FindSourceRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Foundry.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Foundry source root.");
    }

    private static bool IsBuildOutput(string path, string projectDirectory)
    {
        string relativePath = Path.GetRelativePath(projectDirectory, path);
        string firstSegment = relativePath.Split(Path.DirectorySeparatorChar, 2)[0];
        return firstSegment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               firstSegment.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }
}
