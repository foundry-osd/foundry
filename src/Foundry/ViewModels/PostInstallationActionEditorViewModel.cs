// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
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
    }

    public PreOobeSettings Baseline { get; }
    public bool IsNew { get; }
    public ObservableCollection<string> EntryPoints { get; } = [];
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanSave))] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview), nameof(InstallerType), nameof(IsMsi), nameof(CanSave))] public partial string EntryPoint { get; set; } = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))] public partial string PowerShellArguments { get; set; } = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))] public partial bool GenerateInstallationLog { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview))] public partial string Arguments { get; set; } = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CommandPreview), nameof(CanSave))] public partial string CommandText { get; set; } = string.Empty;
    [ObservableProperty] public partial string WorkingDirectory { get; set; } = string.Empty;
    [ObservableProperty] public partial double TimeoutSeconds { get; set; }
    [ObservableProperty] public partial string SuccessCodes { get; set; } = "0";
    [ObservableProperty] public partial string RestartCodes { get; set; } = string.Empty;
    [ObservableProperty] public partial bool ContinueOnError { get; set; }
    [ObservableProperty] public partial bool DeferRestart { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanSave), nameof(CanEdit), nameof(HasFeedback))] public partial bool IsBusy { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasError), nameof(HasFeedback))] public partial string Error { get; set; } = string.Empty;
    public bool CanEdit => !IsBusy;
    public bool CanSave => CanEdit && !string.IsNullOrWhiteSpace(Name) &&
        (!IsExecutable || (IsCommand ? !string.IsNullOrWhiteSpace(CommandText) : HasPackage && !string.IsNullOrWhiteSpace(EntryPoint)));
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool HasFeedback => IsBusy || HasError;
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
    public string ContentLabel => Text("Content");
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
    public string Text(string key) => localization.GetString("PostInstallation." + key);

    /// <summary>Loads selectable entry points from the existing immutable package without changing its reference.</summary>
    public async Task InitializeAsync()
    {
        if (package is null) return;
        IsBusy = true;
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
        finally { IsBusy = false; }
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
                : await picker.PickOpenFileAsync(new FileOpenPickerRequest(ContentLabel, (string[])[".ps1", ".exe", ".msi", ".cmd", ".bat"]));
            if (source is null || cancellation.IsCancellationRequested) return;
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
            OnPropertyChanged(nameof(PackageName));
            OnPropertyChanged(nameof(HasPackage));
            OnPropertyChanged(nameof(CanSave));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Post-installation package import failed.");
            Error = Text("ImportFailed");
        }
        finally { IsBusy = false; }
    }

    private bool IsEntryPoint(string path) => original.Kind switch
    {
        PreOobeActionKind.PowerShell => path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase),
        PreOobeActionKind.Application => path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    /// <summary>Validates the immutable draft before the dialog is allowed to close.</summary>
    public bool TryBuild(out PreOobeActionSettings? result)
    {
        result = null;
        if (IsBusy) return false;
        try
        {
            var action = original with { Name = Name.Trim() };
            if (IsExecutable)
            {
                string entryPoint = NormalizedEntryPoint;
                string? workingDirectory = NullIfEmpty(WorkingDirectory.Trim().Replace('\\', '/'));
                if (HasEntryPoint && !EntryPoints.Contains(entryPoint, StringComparer.OrdinalIgnoreCase)) throw new FormatException();
                if (workingDirectory is not null && !directories.Contains(workingDirectory)) throw new FormatException();
                if (!double.IsFinite(TimeoutSeconds) || TimeoutSeconds != Math.Truncate(TimeoutSeconds)) throw new FormatException();
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
                        TimeoutSeconds = checked((int)TimeoutSeconds),
                        SuccessExitCodes = ParseCodes(SuccessCodes),
                        RestartExitCodes = ParseCodes(RestartCodes),
                        ErrorPolicy = ContinueOnError ? PreOobeErrorPolicy.Continue : PreOobeErrorPolicy.Stop,
                        RestartTiming = DeferRestart ? PreOobeRestartTiming.Deferred : PreOobeRestartTiming.Immediate
                    }
                };
            }
            if (PreOobeConfigurationValidator.Validate(new PreOobeSettings { IsEnabled = true, Actions = (PreOobeActionSettings[])[action with { IsEnabled = true }] }).Count != 0)
                throw new FormatException();
            result = action;
            Error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            Error = Text("InvalidAction");
            return false;
        }
    }

    private static int[] ParseCodes(string value) => string.IsNullOrWhiteSpace(value) ? [] :
        value.Split(',', StringSplitOptions.TrimEntries).Select(item => int.Parse(item, NumberStyles.Integer, CultureInfo.InvariantCulture)).ToArray();
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    public void Dispose() { cancellation.Cancel(); cancellation.Dispose(); }
}
