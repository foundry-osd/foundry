// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Packages;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>Maintains a cancellable action draft; package imports publish immutable revisions independently of saving the draft.</summary>
public sealed partial class PostInstallationActionEditorViewModel : ObservableObject, IDisposable
{
    private readonly PreOobeActionSettings original;
    private readonly PreOobePackageLibraryService library;
    private readonly IFilePickerService picker;
    private readonly IApplicationLocalizationService localization;
    private readonly CancellationTokenSource cancellation = new();
    private readonly HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
    private PreOobePackageReference? package;
    private string[] FileTypeFilters => original.Kind switch
    {
        PreOobeActionKind.PowerShell => [".ps1"],
        PreOobeActionKind.Application => [".exe", ".msi"],
        _ => ["*"]
    };

    public PostInstallationActionEditorViewModel(PreOobeActionSettings action, PreOobeSettings baseline, bool isNew,
        PreOobePackageLibraryService library, IFilePickerService picker, IApplicationLocalizationService localization)
    {
        original = action;
        Baseline = baseline;
        IsNew = isNew;
        this.library = library;
        this.picker = picker;
        this.localization = localization;
        package = action.Package;
        Name = action.Name;
        EntryPoint = action.EntryPoint ?? string.Empty;
        Arguments = action.Arguments ?? string.Empty;
        PowerShellArguments = action.PowerShellArguments ?? string.Empty;
        GenerateInstallationLog = action.GenerateInstallationLog;
        CommandText = action.Command ?? string.Empty;
        WorkingDirectory = action.WorkingDirectory ?? string.Empty;
        TimeoutSeconds = action.Process?.TimeoutSeconds ?? 1800;
        SuccessCodes = string.Join(", ", action.Process?.SuccessExitCodes ?? (int[])[0]);
        RestartCodes = string.Join(", ", action.Process?.RestartExitCodes ?? []);
        ContinueOnError = action.Process?.ErrorPolicy == PreOobeErrorPolicy.Continue;
        DeferRestart = action.Process?.RestartTiming == PreOobeRestartTiming.Deferred;
        RestartDelaySeconds = action.RestartDelaySeconds;
    }

    public PreOobeSettings Baseline { get; }
    public bool IsNew { get; }
    public ObservableCollection<string> EntryPoints { get; } = [];
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview), nameof(InstallerType), nameof(HasInstallerType), nameof(IsMsi))] public partial string EntryPoint { get; set; } = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))] public partial string PowerShellArguments { get; set; } = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))] public partial bool GenerateInstallationLog { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))] public partial string Arguments { get; set; } = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))] public partial string CommandText { get; set; } = string.Empty;
    [ObservableProperty] public partial string WorkingDirectory { get; set; } = string.Empty;
    [ObservableProperty] public partial double TimeoutSeconds { get; set; }
    [ObservableProperty] public partial string SuccessCodes { get; set; } = "0";
    [ObservableProperty] public partial string RestartCodes { get; set; } = string.Empty;
    [ObservableProperty] public partial bool ContinueOnError { get; set; }
    [ObservableProperty] public partial bool DeferRestart { get; set; }
    [ObservableProperty] public partial double RestartDelaySeconds { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanEdit))] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsProcessing { get; set; }
    [ObservableProperty] public partial string ProcessingStatus { get; set; } = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasError))] public partial string Error { get; set; } = string.Empty;
    [ObservableProperty] public partial string InvalidField { get; set; } = string.Empty;
    [ObservableProperty] public partial string ValidationMessage { get; set; } = string.Empty;
    public Visibility ValidationVisibility(string field, string invalidField) => field == invalidField ? Visibility.Visible : Visibility.Collapsed;
    public bool CanEdit => !IsBusy;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool HasPackage => package is not null;
    public bool IsExecutable => original.Kind != PreOobeActionKind.Restart;
    public bool IsRestart => !IsExecutable;
    public bool IsCommand => original.Kind == PreOobeActionKind.Command;
    public bool HasEntryPoint => original.Kind is PreOobeActionKind.PowerShell or PreOobeActionKind.Application;
    public bool IsPowerShell => original.Kind == PreOobeActionKind.PowerShell;
    public bool IsMsi => ApplicationMode == PreOobeApplicationMode.Msi;
    public bool IsApplication => original.Kind == PreOobeActionKind.Application;
    public string PackageName => package?.DisplayName ?? Text("NoContent");
    public string Title => Text(IsNew ? "Add" : "Edit");
    public string NameLabel => localization.GetString("CustomImages.NameLabel");
    public string SaveLabel => localization.GetString("CustomImages.SaveLabel");
    public string CancelLabel => localization.GetString("Common.Cancel");
    public string ContentLabel => Text(IsCommand ? "OptionalContent" : "Content");
    public string CommandContentDescription => Text("CommandContentDescription");
    public string CommandPlaceholder => Text("CommandPlaceholder");
    public string CommandWorkingDirectoryDescription => Text("CommandWorkingDirectoryDescription");
    public string ImportFileLabel => Text("ImportFile");
    public string ImportFolderLabel => Text("ImportFolder");
    public string EntryPointLabel => Text("EntryPoint");
    public string ArgumentsLabel => Text(IsPowerShell ? "ScriptArguments" : "Arguments");
    public string PowerShellArgumentsLabel => Text("PowerShellArguments");
    public string GenerateInstallationLogLabel => Text("GenerateInstallationLog");
    public string CommandLabel => Text("Command");
    public string CommandPreviewLabel => Text("CommandPreview");
    public string CommandPreviewPlaceholder => Text("CommandPreviewPlaceholder");
    public string InstallerType => ApplicationMode?.ToString().ToUpperInvariant() ?? string.Empty;
    public bool HasInstallerType => ApplicationMode is not null;
    private string NormalizedEntryPoint => EntryPoint.Trim().Replace('\\', '/');
    private PreOobeApplicationMode? ApplicationMode => !IsApplication ? null :
        NormalizedEntryPoint.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ? PreOobeApplicationMode.Msi :
        NormalizedEntryPoint.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? PreOobeApplicationMode.Exe : null;
    public string CommandPreview
    {
        get
        {
            if (!IsExecutable || string.IsNullOrWhiteSpace(IsCommand ? CommandText : EntryPoint)) return string.Empty;
            string entryPath = "{ContentRoot}\\" + NormalizedEntryPoint.Replace('/', '\\');
            string executable = IsCommand ? "cmd.exe" : IsPowerShell ? "powershell.exe" : IsMsi ? "msiexec.exe" : "\"" + entryPath + "\"";
            string arguments = PreOobeCommandLine.BuildArguments(original with
            {
                Arguments = Arguments,
                PowerShellArguments = PowerShellArguments,
                GenerateInstallationLog = IsMsi && GenerateInstallationLog,
                ApplicationMode = ApplicationMode,
                Command = CommandText
            }, entryPath, "{LogRoot}");
            return arguments.Length == 0 ? executable : executable + " " + arguments;
        }
    }
    public string WorkingDirectoryLabel => Text("WorkingDirectory");
    public string TimeoutLabel => Text("Timeout");
    public string SuccessCodesLabel => Text("SuccessCodes");
    public string RestartCodesLabel => Text("RestartCodes");
    public string ContinueLabel => Text("ContinueOnError");
    public string ContinueDescription => Text("ContinueOnErrorDescription");
    public string DeferRestartLabel => Text("DeferRestart");
    public string DeferRestartDescription => Text("DeferRestartDescription");
    public string ApplicationModeLabel => Text("ApplicationMode");
    public string ExecutionHelp => Text("ExecutionHelp");
    public string ExecutionSettingsLabel => Text("ExecutionSettings");
    public string RestartDescription => Text("RestartDescription");
    public string RestartDelayLabel => Text("RestartDelay");
    public string RestartDelayDescription => Text("RestartDelayDescription");
    public int MaximumRestartDelaySeconds => PreOobeConfigurationValidator.MaximumRestartDelaySeconds;
    public string Text(string key) => localization.GetString("PostInstallation." + key);

    /// <summary>Loads selectable entry points from the existing immutable package without changing its reference.</summary>
    public async Task InitializeAsync()
    {
        if (package is null) return;
        IsBusy = true;
        ProcessingStatus = Text("LoadingContent");
        IsProcessing = true;
        try
        {
            using var lease = await library.AcquireAsync(package.ContentHash, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            EntryPoints.Clear();
            foreach (var file in lease.Files.Where(file => IsEntryPoint(file.RelativePath))) EntryPoints.Add(file.RelativePath);
            directories.Clear();
            directories.UnionWith(lease.Manifest.Directories);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Post-installation package could not be opened for editing.");
            Error = Text("ImportFailed");
        }
        finally { IsProcessing = false; IsBusy = false; }
    }

    [RelayCommand] private Task ImportFileAsync() => ImportAsync(false);
    [RelayCommand] private Task ImportFolderAsync() => ImportAsync(true);

    private async Task ImportAsync(bool folder)
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = string.Empty;
        try
        {
            string? source = folder
                ? await picker.PickFolderAsync(new FolderPickerRequest(ContentLabel))
                : await picker.PickOpenFileAsync(new FileOpenPickerRequest(ContentLabel, FileTypeFilters));
            if (source is null || cancellation.IsCancellationRequested) return;
            if (!folder && HasEntryPoint && !IsEntryPoint(source))
            {
                Error = Text("InvalidEntryPoint");
                return;
            }
            ProcessingStatus = Text("ImportingContent");
            IsProcessing = true;
            var imported = await library.ImportAsync(source, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            using var lease = await library.AcquireAsync(imported.ContentHash, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            package = imported;
            EntryPoints.Clear();
            foreach (var file in lease.Files.Where(file => IsEntryPoint(file.RelativePath))) EntryPoints.Add(file.RelativePath);
            directories.Clear();
            directories.UnionWith(lease.Manifest.Directories);
            if (HasEntryPoint && !EntryPoints.Contains(EntryPoint)) EntryPoint = EntryPoints.FirstOrDefault() ?? string.Empty;
            if (IsCommand && !folder && string.IsNullOrWhiteSpace(CommandText) && lease.Files.Count == 1)
            {
                string file = lease.Files[0].RelativePath;
                if (file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                    CommandText = file.Replace('/', '\\');
            }
            if (InvalidField is nameof(PackageName) or nameof(EntryPoint) or nameof(WorkingDirectory))
            {
                InvalidField = string.Empty;
                ValidationMessage = string.Empty;
            }
            OnPropertyChanged(nameof(PackageName));
            OnPropertyChanged(nameof(HasPackage));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Post-installation package import failed.");
            Error = Text("ImportFailed");
        }
        finally { IsProcessing = false; IsBusy = false; }
    }

    private bool IsEntryPoint(string path) => original.Kind switch
    {
        PreOobeActionKind.PowerShell => path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase),
        PreOobeActionKind.Application => path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    /// <summary>Validates the draft and identifies the first field that needs attention.</summary>
    public bool TryBuild(out PreOobeActionSettings? result)
    {
        result = null;
        if (IsBusy) return false;
        InvalidField = string.Empty;
        ValidationMessage = string.Empty;
        Error = string.Empty;
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 256 || Name.Any(char.IsControl))
            return Invalid(nameof(Name), "InvalidName");
        var action = original with { Name = Name.Trim() };
        if (IsRestart)
        {
            if (!double.IsFinite(RestartDelaySeconds) || RestartDelaySeconds != Math.Truncate(RestartDelaySeconds) ||
                RestartDelaySeconds < 0 || RestartDelaySeconds > MaximumRestartDelaySeconds)
                return Invalid(nameof(RestartDelaySeconds), "InvalidRestartDelay");
            action = action with { RestartDelaySeconds = (int)RestartDelaySeconds };
        }
        if (IsExecutable)
        {
            string entryPoint = NormalizedEntryPoint;
            string? workingDirectory = NullIfEmpty(WorkingDirectory.Trim().Replace('\\', '/'));
            if (HasEntryPoint && package is null) return Invalid(nameof(PackageName), "ContentRequired");
            if (HasEntryPoint && !EntryPoints.Contains(entryPoint, StringComparer.OrdinalIgnoreCase))
                return Invalid(nameof(EntryPoint), "InvalidEntryPoint");
            if (IsCommand && string.IsNullOrWhiteSpace(CommandText)) return Invalid(nameof(CommandText), "CommandRequired");
            if (IsCommand && !IsSingleLine(CommandText)) return Invalid(nameof(CommandText), "InvalidCommandText");
            if (workingDirectory is not null && !directories.Contains(workingDirectory))
                return Invalid(nameof(WorkingDirectory), "InvalidWorkingDirectory");
            if (IsPowerShell && !IsSingleLine(PowerShellArguments)) return Invalid(nameof(PowerShellArguments), "InvalidCommandText");
            if (HasEntryPoint && !IsSingleLine(Arguments)) return Invalid(nameof(Arguments), "InvalidCommandText");
            if (!double.IsFinite(TimeoutSeconds) || TimeoutSeconds != Math.Truncate(TimeoutSeconds) || TimeoutSeconds is < 1 or > 86400)
                return Invalid(nameof(TimeoutSeconds), "InvalidTimeout");
            int[]? successCodes = ParseCodes(SuccessCodes, required: true);
            if (successCodes is null) return Invalid(nameof(SuccessCodes), "InvalidExitCodes");
            int[]? restartCodes = ParseCodes(RestartCodes, required: false);
            if (restartCodes is null) return Invalid(nameof(RestartCodes), "InvalidExitCodes");
            if (successCodes.Intersect(restartCodes).Any()) return Invalid(nameof(RestartCodes), "OverlappingExitCodes");
            action = action with
            {
                Package = package,
                EntryPoint = HasEntryPoint ? entryPoint : null,
                Arguments = HasEntryPoint ? NullIfEmpty(Arguments) : null,
                PowerShellArguments = IsPowerShell ? NullIfEmpty(PowerShellArguments) : null,
                GenerateInstallationLog = IsMsi && GenerateInstallationLog,
                Command = IsCommand ? CommandText : null,
                WorkingDirectory = workingDirectory,
                ApplicationMode = ApplicationMode,
                Process = new()
                {
                    TimeoutSeconds = (int)TimeoutSeconds,
                    SuccessExitCodes = successCodes,
                    RestartExitCodes = restartCodes,
                    ErrorPolicy = ContinueOnError ? PreOobeErrorPolicy.Continue : PreOobeErrorPolicy.Stop,
                    RestartTiming = DeferRestart ? PreOobeRestartTiming.Deferred : PreOobeRestartTiming.Immediate
                }
            };
        }
        if (PreOobeConfigurationValidator.Validate(new PreOobeSettings { IsEnabled = true, Actions = (PreOobeActionSettings[])[action with { IsEnabled = true }] }).Count != 0)
        {
            Error = Text("InvalidAction");
            return false;
        }
        result = action;
        return true;
    }

    private bool Invalid(string field, string key)
    {
        ValidationMessage = Text(key);
        InvalidField = field;
        return false;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (!string.IsNullOrEmpty(InvalidField) && (args.PropertyName == InvalidField ||
            InvalidField == nameof(RestartCodes) && args.PropertyName == nameof(SuccessCodes)))
        {
            InvalidField = string.Empty;
            ValidationMessage = string.Empty;
        }
    }

    private static bool IsSingleLine(string value) => value.Length <= PreOobeConfigurationValidator.MaximumCommandLength && value.IndexOfAny(['\0', '\r', '\n']) < 0;

    private static int[]? ParseCodes(string value, bool required)
    {
        if (string.IsNullOrWhiteSpace(value)) return required ? null : [];
        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length > 32) return null;
        var codes = new List<int>();
        foreach (string part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code) || code < 0 || code == 1641 || codes.Contains(code)) return null;
            codes.Add(code);
        }
        return codes.ToArray();
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    public void Dispose() { cancellation.Cancel(); cancellation.Dispose(); }
}
