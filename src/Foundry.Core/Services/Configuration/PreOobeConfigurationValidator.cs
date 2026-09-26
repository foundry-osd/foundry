// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Packages;
using System.Text.RegularExpressions;

namespace Foundry.Core.Services.Configuration;

/// <summary>Identifies an invariant localization key without exposing user command text in validation diagnostics.</summary>
public sealed record PreOobeValidationIssue(string Code, string? ActionId = null);

/// <summary>Validates portable action syntax without opening content or executing commands.</summary>
public static class PreOobeConfigurationValidator
{
    public const int MaximumActions = 1000;
    public const int MaximumCommandLength = 8191;

    public static IReadOnlyList<PreOobeValidationIssue> Validate(PreOobeSettings? settings)
    {
        if (settings?.Actions is null || settings.Actions.Count > MaximumActions)
            return [new("PreOobe.InvalidConfiguration")];
        List<PreOobeValidationIssue> issues = [];
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PreOobeActionSettings? action in settings.Actions)
        {
            if (action is null) { issues.Add(new("PreOobe.InvalidAction")); continue; }
            if (!Guid.TryParseExact(action.Id, "N", out _) || !ids.Add(action.Id)) Add("PreOobe.InvalidActionId");
            if (string.IsNullOrWhiteSpace(action.Name) || action.Name.Length > 256 || action.Name.Any(char.IsControl)) Add("PreOobe.InvalidActionName");
            if (!Enum.IsDefined(action.Kind)) { Add("PreOobe.InvalidActionKind"); continue; }
            if (action.Package is not null && !IsValidReference(action.Package)) Add("PreOobe.InvalidPackageReference");
            if (!IsRelative(action.EntryPoint) || !IsRelative(action.WorkingDirectory)) Add("PreOobe.InvalidPackagePath");
            if (!IsBoundedText(action.Arguments) || !IsBoundedText(action.Command)) Add("PreOobe.InvalidCommand");

            if (action.Kind == PreOobeActionKind.Restart)
            {
                if (action.Process is not null || action.Package is not null || action.ApplicationMode is not null ||
                    action.EntryPoint is not null || action.WorkingDirectory is not null || action.Arguments is not null || action.Command is not null)
                    Add("PreOobe.InvalidRestartAction");
                continue;
            }
            if (action.Process is not { } process || process.TimeoutSeconds is < 1 or > 86400 ||
                !Enum.IsDefined(process.ErrorPolicy) || !Enum.IsDefined(process.RestartTiming) || !Enum.IsDefined(process.Architecture) ||
                !ValidCodes(process.SuccessExitCodes, required: true) || !ValidCodes(process.RestartExitCodes, required: false) ||
                process.SuccessExitCodes.Intersect(process.RestartExitCodes).Any())
                Add("PreOobe.InvalidProcessPolicy");

            if (action.Kind == PreOobeActionKind.Command)
            {
                if (string.IsNullOrWhiteSpace(action.Command) || action.EntryPoint is not null || action.ApplicationMode is not null || action.Arguments is not null ||
                    (action.Package is null && action.WorkingDirectory is not null)) Add("PreOobe.InvalidCommandAction");
            }
            else
            {
                if (action.Package is null || string.IsNullOrWhiteSpace(action.EntryPoint) || action.Command is not null) Add("PreOobe.MissingEntryPoint");
                if (action.Kind == PreOobeActionKind.PowerShell && (action.ApplicationMode is not null || !HasExtension(action.EntryPoint, ".ps1")))
                    Add("PreOobe.InvalidPowerShellAction");
                if (action.Kind == PreOobeActionKind.Application && (!action.ApplicationMode.HasValue || !Enum.IsDefined(action.ApplicationMode.Value) ||
                    !HasExtension(action.EntryPoint, action.ApplicationMode == PreOobeApplicationMode.Msi ? ".msi" : ".exe") ||
                    action.ApplicationMode == PreOobeApplicationMode.Msi && !AreMsiArgumentsSafe(action.Arguments)))
                    Add("PreOobe.InvalidApplicationAction");
            }
            void Add(string code) => issues.Add(new(code, action.Id));
        }
        return issues;
    }

    public static void ThrowIfInvalid(PreOobeSettings? settings)
    {
        IReadOnlyList<PreOobeValidationIssue> issues = Validate(settings);
        if (issues.Count > 0) throw new InvalidDataException(issues[0].Code);
    }

    public static bool IsValidReference(PreOobePackageReference reference) =>
        PreOobePackagePathPolicy.IsValidHash(reference.ContentHash) && reference.Length >= 0 && reference.FileCount is > 0 and <= 10_000 &&
        !string.IsNullOrWhiteSpace(reference.DisplayName) && reference.DisplayName.Length <= 256 && !reference.DisplayName.Any(char.IsControl);

    /// <summary>Rejects MSI restart overrides and unbalanced quoting before generated suppression arguments are appended.</summary>
    public static bool AreMsiArgumentsSafe(string? arguments)
    {
        if (arguments is null) return true;
        if (!IsBoundedText(arguments) || arguments.Count(character => character == '"') % 2 != 0) return false;
        foreach (Match match in Regex.Matches(arguments, "(?:[^\\s\"]|\"[^\"]*\")+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            string token = match.Value.Replace("\"", string.Empty);
            if (token.StartsWith('-')) token = "/" + token[1..];
            if (token.Equals("/forcerestart", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("/promptrestart", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("/restart", StringComparison.OrdinalIgnoreCase)) return false;
            if (token.StartsWith("REBOOT=", StringComparison.OrdinalIgnoreCase) &&
                !token.Equals("REBOOT=ReallySuppress", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>Disabled actions remain editable; readiness requires only enabled actions whose architecture can run.</summary>
    public static bool IsReady(PreOobeSettings settings, Func<PreOobePackageReference, bool> isAvailable, PreOobeArchitecture architecture = PreOobeArchitecture.Any) =>
        !settings.IsEnabled || (Validate(settings).Count == 0 && settings.Actions.Where(action => action.IsEnabled &&
            (architecture == PreOobeArchitecture.Any || action.Process is null || action.Process.Architecture == PreOobeArchitecture.Any || action.Process.Architecture == architecture))
            .All(action => action.Package is null || isAvailable(action.Package)));

    private static bool IsRelative(string? value)
    {
        if (value is null) return true;
        try { PreOobePackagePathPolicy.ValidateRelativePath(value); return true; }
        catch (InvalidDataException) { return false; }
    }

    private static bool HasExtension(string? path, string extension) => string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);
    private static bool IsBoundedText(string? value) => value is null || (value.Length <= MaximumCommandLength && !value.Contains('\0') && !value.Contains('\r') && !value.Contains('\n'));
    private static bool ValidCodes(IReadOnlyList<int>? codes, bool required) => codes is not null && codes.Count <= 32 &&
        (!required || codes.Count > 0) && codes.All(code => code >= 0 && code != 1641) && codes.Distinct().Count() == codes.Count;
}
