namespace Foundry.Core.Services.Adk;

public static class AdkUninstallCommandSelector
{
    public static IReadOnlyList<AdkUninstallCommand> SelectBundleUninstallCommands(IReadOnlyList<AdkInstalledProduct> products)
    {
        List<AdkUninstallCommand> commands = [];

        AdkUninstallCommand? winPeCommand = products
            .Where(IsWinPeBundle)
            .Select(TryCreateCommand)
            .FirstOrDefault(command => command is not null);

        if (winPeCommand is not null)
        {
            commands.Add(winPeCommand);
        }

        AdkUninstallCommand? adkCommand = products
            .Where(IsAdkBundle)
            .Select(TryCreateCommand)
            .FirstOrDefault(command => command is not null);

        if (adkCommand is not null)
        {
            commands.Add(adkCommand);
        }

        return commands;
    }

    private static bool IsAdkBundle(AdkInstalledProduct product)
    {
        return ContainsSetupExecutable(product, "adksetup.exe")
            || product.DisplayName.Equals("Windows Assessment and Deployment Kit", StringComparison.OrdinalIgnoreCase)
            || product.DisplayName.Contains("Deployment Kit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWinPeBundle(AdkInstalledProduct product)
    {
        return ContainsSetupExecutable(product, "adkwinpesetup.exe")
            || product.DisplayName.Contains("Windows Preinstallation Environment", StringComparison.OrdinalIgnoreCase)
            || product.DisplayName.Contains("Windows PE", StringComparison.OrdinalIgnoreCase)
            || product.DisplayName.Contains("WinPE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsSetupExecutable(AdkInstalledProduct product, string setupFileName)
    {
        return (product.QuietUninstallString?.Contains(setupFileName, StringComparison.OrdinalIgnoreCase) ?? false)
            || (product.UninstallString?.Contains(setupFileName, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static AdkUninstallCommand? TryCreateCommand(AdkInstalledProduct product)
    {
        string? commandLine = !string.IsNullOrWhiteSpace(product.QuietUninstallString)
            ? product.QuietUninstallString
            : product.UninstallString;

        if (string.IsNullOrWhiteSpace(commandLine) || !TrySplitCommandLine(commandLine, out string fileName, out string arguments))
        {
            return null;
        }

        arguments = EnsureUninstallArgument(arguments);
        arguments = EnsureQuietArgument(arguments);
        arguments = EnsureNoRestartArgument(arguments);

        return new(product.DisplayName, fileName, arguments);
    }

    private static string EnsureUninstallArgument(string arguments)
    {
        return arguments.Contains("/uninstall", StringComparison.OrdinalIgnoreCase)
            ? arguments
            : $"{arguments} /uninstall".Trim();
    }

    private static string EnsureQuietArgument(string arguments)
    {
        return arguments.Contains("/quiet", StringComparison.OrdinalIgnoreCase)
            ? arguments
            : $"{arguments} /quiet".Trim();
    }

    private static string EnsureNoRestartArgument(string arguments)
    {
        return arguments.Contains("/norestart", StringComparison.OrdinalIgnoreCase)
            ? arguments
            : $"{arguments} /norestart".Trim();
    }

    private static bool TrySplitCommandLine(string commandLine, out string fileName, out string arguments)
    {
        commandLine = commandLine.Trim();
        fileName = string.Empty;
        arguments = string.Empty;

        if (commandLine.Length == 0)
        {
            return false;
        }

        if (commandLine[0] == '"')
        {
            int closingQuoteIndex = commandLine.IndexOf('"', 1);
            if (closingQuoteIndex <= 1)
            {
                return false;
            }

            fileName = commandLine[1..closingQuoteIndex];
            arguments = commandLine[(closingQuoteIndex + 1)..].Trim();
            return true;
        }

        int firstSpaceIndex = commandLine.IndexOf(' ');
        if (firstSpaceIndex < 0)
        {
            fileName = commandLine;
            return true;
        }

        fileName = commandLine[..firstSpaceIndex];
        arguments = commandLine[(firstSpaceIndex + 1)..].Trim();
        return true;
    }
}
