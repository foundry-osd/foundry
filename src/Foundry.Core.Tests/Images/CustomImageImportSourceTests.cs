// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Images;

namespace Foundry.Core.Tests.Images;

public sealed class CustomImageImportSourceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryIsoChoice", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MultipleInstallationContainersRequireExplicitChoice()
    {
        Directory.CreateDirectory(Path.Combine(root, "sources"));
        File.WriteAllText(Path.Combine(root, "sources", "install.wim"), "wim");
        File.WriteAllText(Path.Combine(root, "sources", "install.esd"), "esd");
        var exception = Assert.Throws<CustomImageSourceChoiceRequiredException>(() =>
            CustomImageImportSource.ResolveInstallationPath(root, null));
        Assert.Equal(["sources/install.wim", "sources/install.esd"], exception.Candidates);
        Assert.Equal(Path.Combine(root, "sources", "install.esd"),
            CustomImageImportSource.ResolveInstallationPath(root, "sources/install.esd"));
    }

    [Fact]
    public void SingleContainerIsResolvedButBootImageCannotBeSelected()
    {
        Directory.CreateDirectory(Path.Combine(root, "sources"));
        File.WriteAllText(Path.Combine(root, "sources", "install.wim"), "wim");
        File.WriteAllText(Path.Combine(root, "sources", "boot.wim"), "boot");
        Assert.Equal(Path.Combine(root, "sources", "install.wim"), CustomImageImportSource.ResolveInstallationPath(root, null));
        Assert.Throws<InvalidDataException>(() => CustomImageImportSource.ResolveInstallationPath(root, "sources/boot.wim"));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
