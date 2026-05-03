using Foundry.Core.Services.Adk;

namespace Foundry.Core.Tests.Adk;

public sealed class AdkUninstallCommandSelectorTests
{
    [Fact]
    public void SelectBundleUninstallCommands_ReturnsWinPeBeforeAdk()
    {
        AdkInstalledProduct adkProduct = new(
            "Localized Windows ADK bundle",
            "10.1.22621.5337",
            null,
            "\"C:\\ProgramData\\Package Cache\\{adk}\\adksetup.exe\" /uninstall /quiet");
        AdkInstalledProduct winPeProduct = new(
            "Localized Windows PE add-on bundle",
            "10.1.22621.5337",
            null,
            "\"C:\\ProgramData\\Package Cache\\{winpe}\\adkwinpesetup.exe\" /uninstall /quiet");

        IReadOnlyList<AdkUninstallCommand> commands = AdkUninstallCommandSelector.SelectBundleUninstallCommands(
            [adkProduct, winPeProduct]);

        Assert.Collection(
            commands,
            command =>
            {
                Assert.Equal(winPeProduct.DisplayName, command.DisplayName);
                Assert.Equal(@"C:\ProgramData\Package Cache\{winpe}\adkwinpesetup.exe", command.FileName);
                Assert.Equal("/uninstall /quiet /norestart", command.Arguments);
            },
            command =>
            {
                Assert.Equal(adkProduct.DisplayName, command.DisplayName);
                Assert.Equal(@"C:\ProgramData\Package Cache\{adk}\adksetup.exe", command.FileName);
                Assert.Equal("/uninstall /quiet /norestart", command.Arguments);
            });
    }

    [Fact]
    public void SelectBundleUninstallCommands_AddsQuietAndNoRestartArgumentsWhenOnlyInteractiveUninstallExists()
    {
        AdkInstalledProduct adkProduct = new(
            "Windows Assessment and Deployment Kit",
            "10.1.22621.5337",
            "\"C:\\ProgramData\\Package Cache\\{adk}\\adksetup.exe\" /uninstall");

        IReadOnlyList<AdkUninstallCommand> commands = AdkUninstallCommandSelector.SelectBundleUninstallCommands([adkProduct]);

        AdkUninstallCommand command = Assert.Single(commands);
        Assert.Equal(@"C:\ProgramData\Package Cache\{adk}\adksetup.exe", command.FileName);
        Assert.Equal("/uninstall /quiet /norestart", command.Arguments);
    }

    [Fact]
    public void SelectBundleUninstallCommands_IgnoresComponentMsiEntries()
    {
        AdkInstalledProduct componentProduct = new(
            "Windows PE Boot Files (OnecoreUAP)",
            "10.1.22621.5337",
            "MsiExec.exe /I{0C141632-B69C-A38F-0639-743F6F0A8D03}");

        IReadOnlyList<AdkUninstallCommand> commands = AdkUninstallCommandSelector.SelectBundleUninstallCommands([componentProduct]);

        Assert.Empty(commands);
    }
}
