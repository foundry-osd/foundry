// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Services.Runtime;
using Foundry.Utilities.Runtime;

namespace Foundry.Connect.Tests;

public sealed class PackageValidationCommandTests
{
    [Fact]
    public void ManagedResourcesAreReadableWithoutCreatingViews()
    {
        PackageValidation.ValidateResources(typeof(FoundryConnectApplicationInfo).Assembly, ["en-US", "fr-FR"]);
    }

    [Fact]
    public void MissingExactSatelliteDoesNotPassThroughLanguageFallback()
    {
        Assert.Throws<FileNotFoundException>(() => PackageValidation.ValidateResources(typeof(FoundryConnectApplicationInfo).Assembly, ["fr-CH"]));
    }

    [Theory]
    [InlineData("--offline")]
    [InlineData("--validate-package")]
    public void MixedValidationArgumentsCannotReachPackageOrStartupWork(string extra)
    {
        string[] args = ["--validate-package", "--expected-version", FoundryConnectApplicationInfo.Version, "--expected-runtime", "win-x64", extra];
        Assert.True(PackageValidationCommand.IsRequested(args));
        Assert.Equal(2, PackageValidationCommand.Run(args, _ => throw new InvalidOperationException("must not run"), TextWriter.Null));
    }
}
