// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Runtime;

namespace Foundry.Deploy.Tests;

public sealed class PackageValidationCommandTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("digest")]
    [InlineData("duplicate")]
    [InlineData("malformed")]
    public void PackagedProvenanceMustRetainPinnedIdentity(string change)
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-provenance-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "THIRD_PARTY_NOTICES.md"), "Package notices");
            string json = """
                {"schemaVersion":1,"file":"ServiceUI.exe","fileVersion":"1.3.0.0","size":74008,
                "sha256":"1BE85A64AAD2C3CAA0DC28705B49A1548E85157F4D2D522C20FEC4B4570A623F",
                "peMachine":"8664","architecture":"x64","expectedSigner":"Microsoft Corporation",
                "sourcePackageVerification":"unverified","redistributionVerification":"unverified"}
                """;
            if (change == "digest") json = json.Replace("1BE85A64", "00000000", StringComparison.Ordinal);
            if (change == "duplicate") json = json.Replace("{", "{\"schemaVersion\":1,", StringComparison.Ordinal);
            if (change == "malformed") json = "{";
            File.WriteAllText(Path.Combine(root, "ServiceUI.provenance.json"), json);
            if (change == "valid") PackageValidationCommand.ValidatePackagedRecords(root);
            else Assert.ThrowsAny<Exception>(() => PackageValidationCommand.ValidatePackagedRecords(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MixedOfflineRequestCannotEnterEitherWorkflow()
    {
        string[] args = ["--validate-package", "--expected-version", FoundryDeployApplicationInfo.Version, "--expected-runtime", "win-x64", "--offline"];
        Assert.True(PackageValidationCommand.IsRequested(args));
        Assert.Equal(2, PackageValidationCommand.Run(args, _ => throw new InvalidOperationException("must not run"), TextWriter.Null));
    }

    [Fact]
    public void EmbeddedServiceUiBytesMatchPinnedIdentityWithoutExecution()
    {
        using Stream stream = typeof(FoundryDeployApplicationInfo).Assembly.GetManifestResourceStream("Foundry.Deploy.AutopilotRegistration.ServiceUI.exe")!;
        PackageValidationCommand.ValidateServiceUi(stream);
    }

    [Fact]
    public void AllRequiredEmbeddedScriptsAndServiceUiArePresentWithoutExecution()
    {
        PackageValidationCommand.ValidateEmbeddedAssets(typeof(FoundryDeployApplicationInfo).Assembly);
    }

    [Fact]
    public void ChangedServiceUiBytesFailValidation()
    {
        using MemoryStream stream = new(new byte[74008]);
        Assert.Throws<InvalidDataException>(() => PackageValidationCommand.ValidateServiceUi(stream));
    }

    [Fact]
    public void MissingNoticesCannotPassProvenanceCheck()
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-package-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { Assert.Throws<FileNotFoundException>(() => PackageValidationCommand.ValidatePackagedRecords(root)); }
        finally { Directory.Delete(root); }
    }
}
