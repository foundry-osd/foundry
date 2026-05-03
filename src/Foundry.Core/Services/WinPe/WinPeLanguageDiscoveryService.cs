namespace Foundry.Core.Services.WinPe;

public sealed class WinPeLanguageDiscoveryService : IWinPeLanguageDiscoveryService
{
    public WinPeResult<IReadOnlyList<string>> GetAvailableLanguages(WinPeLanguageDiscoveryOptions options)
    {
        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult<IReadOnlyList<string>>.Failure(validationError);
        }

        string optionalComponentsRoot = Path.Combine(
            options.Tools!.KitsRootPath,
            "Assessment and Deployment Kit",
            "Windows Preinstallation Environment",
            options.Architecture.ToCopypeArchitecture(),
            "WinPE_OCs");

        if (!Directory.Exists(optionalComponentsRoot))
        {
            return WinPeResult<IReadOnlyList<string>>.Failure(
                WinPeErrorCodes.ToolNotFound,
                "The WinPE optional components folder was not found.",
                $"Expected path: '{optionalComponentsRoot}'.");
        }

        string[] languages = Directory.GetDirectories(optionalComponentsRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => WinPeLanguageUtility.Normalize(name!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return WinPeResult<IReadOnlyList<string>>.Success(languages);
    }

    private static WinPeDiagnostic? ValidateOptions(WinPeLanguageDiscoveryOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE language discovery options are required.",
                "Provide a non-null WinPeLanguageDiscoveryOptions instance.");
        }

        if (!Enum.IsDefined(options.Architecture))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Architecture value is invalid.",
                $"Value: '{options.Architecture}'.");
        }

        if (options.Tools is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE tool paths are required.",
                "Set WinPeLanguageDiscoveryOptions.Tools.");
        }

        if (string.IsNullOrWhiteSpace(options.Tools.KitsRootPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "ADK kits root path is required.",
                "Set WinPeToolPaths.KitsRootPath.");
        }

        return null;
    }
}
