// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Profiles;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>Coordinates profile actions while the view owns native dialog and password-control lifetimes.</summary>
public sealed partial class DeploymentProfilesViewModel : ObservableObject
{
    private readonly DeploymentProfileCoordinator coordinator;
    private readonly IApplicationLocalizationService localization;
    private readonly IFilePickerService picker;
    private bool attached;

    internal DeploymentProfilesViewModel(DeploymentProfileCoordinator coordinator,
        IApplicationLocalizationService localization, IFilePickerService picker)
    {
        this.coordinator = coordinator;
        this.localization = localization;
        this.picker = picker;
    }

    internal Func<ProfileDialogRequest, Task<ProfileDialogResponse?>>? ShowDialogAsync { get; set; }
    public ObservableCollection<LocalProfileDescriptor> Profiles { get; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanActivate))] public partial LocalProfileDescriptor? SelectedProfile { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusVisibility))] public partial string Status { get; set; } = string.Empty;
    public bool CanInteract => !IsBusy;
    public bool CanActivate => CanInteract && SelectedProfile is { } selected && selected.LocalId != coordinator.Active?.LocalId;
    public string ActiveProfileDescription => coordinator.Active is { } active ? $"{Text("Profiles.Active")}: {active.DisplayName}" : string.Empty;
    public Visibility StatusVisibility => string.IsNullOrEmpty(Status) || Status == Text("Profiles.Ready") ? Visibility.Collapsed : Visibility.Visible;
    public bool HasActive => coordinator.Active is not null;
    public bool IsShared => coordinator.Active?.Enrollment is not null;
    public Visibility SharedVisibility => IsShared ? Visibility.Visible : Visibility.Collapsed;
    public bool RememberSecrets => coordinator.Active?.RememberSecrets == true;
    public string SynchronizeActionText => Text(IsShared ? "Profiles.SyncNow" : "Profiles.Setup");
    public bool SyncEnabled => coordinator.Active?.Enrollment?.IsEnabled == true;
    public Visibility ConflictVisibility => coordinator.HasConflict && coordinator.StatusKey != "Profiles.DeletedRemote" ? Visibility.Visible : Visibility.Collapsed;
    public string Text(string key) => localization.GetString(key);

    internal void Attach()
    {
        if (attached) return;
        attached = true;
        coordinator.IsSettingsOpen = true;
        coordinator.Changed += OnChanged;
        Refresh();
    }

    internal void Detach()
    {
        attached = false;
        coordinator.IsSettingsOpen = false;
        coordinator.Changed -= OnChanged;
    }

    internal void Refresh()
    {
        Profiles.Clear();
        foreach (LocalProfileDescriptor profile in coordinator.Profiles) Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault(profile => profile.LocalId == coordinator.Active?.LocalId);
        Status = Text(coordinator.StatusKey);
        OnPropertyChanged(string.Empty);
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanActivate));
    }

    [RelayCommand]
    private Task ActivateAsync() => RunAsync(async () =>
    {
        if (SelectedProfile is not { } selected || selected.LocalId == coordinator.Active?.LocalId) return;
        if (await ConfirmAsync("Profiles.Activate", "Profiles.ReplaceWarning")) await coordinator.ActivateAsync(selected.LocalId);
    });

    [RelayCommand]
    private Task SaveCopyAsync() => RunAsync(async () =>
    {
        ProfileDialogResponse? result = await DialogAsync(new("Profiles.SaveCopy") { Name = coordinator.Active?.DisplayName ?? string.Empty });
        if (result is not null) await coordinator.SaveAsCopyAsync(result.Name.Trim());
    });

    [RelayCommand]
    private Task RenameAsync() => RunAsync(async () =>
    {
        ProfileDialogResponse? result = await DialogAsync(new("Profiles.Rename") { Name = coordinator.Active?.DisplayName ?? string.Empty });
        if (result is not null) await coordinator.RenameAsync(result.Name.Trim());
    });

    [RelayCommand]
    private Task ToggleRememberAsync() => RunAsync(async () =>
    {
        bool remember = !RememberSecrets;
        if (await ConfirmAsync("Profiles.Remember", remember ? "Profiles.LocalPrivacy" : "Profiles.StopRemembering"))
            await coordinator.SaveAsync(remember);
    });

    [RelayCommand]
    private Task ForgetAsync() => RunAsync(async () =>
    {
        if (await ConfirmAsync("Profiles.Forget", "Profiles.ForgetWarning")) await coordinator.ForgetAsync();
    });

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        ProfileDialogResponse? result = await DialogAsync(new("Profiles.Delete")
        { Message = Text("Profiles.DeleteWarning"), DeleteSharedOption = IsShared });
        if (result is null) return;
        if (result.DeleteShared && !await ConfirmAsync("Profiles.DeleteShared", "Profiles.DeleteSharedWarning")) return;
        await coordinator.DeleteAsync(result.DeleteShared);
    });

    [RelayCommand] private Task ExportAsync() => RunAsync(() => ExportFileAsync(false));
    [RelayCommand] private Task ExportRecoveryAsync() => RunAsync(() => ExportFileAsync(true));

    private async Task ExportFileAsync(bool recovery)
    {
        string title = recovery ? "Profiles.ExportRecovery" : "Profiles.Export";
        string? path = await picker.PickSaveFileAsync(new(Text(title), recovery ? "Foundry-connection" : "Foundry-profile",
            new FilePickerTypeChoice[] { new(Text("Profiles.Heading"), new string[] { ".foundryprofile" }) }, ".foundryprofile"));
        if (path is null) return;
        ProfileDialogResponse? result = await DialogAsync(new(title)
        {
            Message = recovery ? Text("Profiles.RecoveryWarning") : Text("Profiles.ExportProtection") + "\n\n" + Text("Profiles.ShareWarning"),
            Passphrase = true,
            ConfirmPassphrase = true,
            IncludeSecretsOption = !recovery
        });
        if (result is null) return;
        if (recovery) await coordinator.ExportRecoveryAsync(path, result.Password);
        else await coordinator.ExportAsync(path, result.Password, result.IncludeSecrets);
    }

    [RelayCommand] private Task ImportAsync() => RunAsync(() => ImportFileAsync(false));

    private async Task ImportFileAsync(bool join)
    {
        string title = join ? "Profiles.JoinShared" : "Profiles.Import";
        string? path = await picker.PickOpenFileAsync(new(Text(title), new string[] { ".foundryprofile" }));
        if (path is null) return;
        ProfileDialogResponse? password = await DialogAsync(new(title) { Passphrase = true });
        if (password is null) return;
        DeploymentProfileDocument profile = await coordinator.PreviewImportAsync(path, password.Password);
        try
        {
            int secrets = profile.Secrets.Entries.Count(secret => secret.State is ProfileValueState.Present or ProfileValueState.Blank);
            int assets = profile.Assets.Count(asset => asset.State == ProfileValueState.Present);
            string preview = localization.FormatString("Profiles.Preview", profile.DisplayName, assets, secrets);
            int missingAssets = profile.Assets.Count(asset => asset.State is ProfileValueState.Omitted or ProfileValueState.Unavailable);
            if (missingAssets > 0) preview += "\n\n" + localization.FormatString("Profiles.MissingAssets", missingAssets);
            ProfileDialogResponse? result = await DialogAsync(new(title)
            {
                Message = preview + "\n\n" + Text("Profiles.ShareWarning") + "\n\n" + Text("Profiles.ReplaceWarning"),
                RememberOption = true,
                SharedKeyOption = join,
                SharePathOption = join
            });
            if (result is null) return;
            if (join) await coordinator.JoinSharedAsync(profile, result.SharePath.Trim(), result.Remember, result.RememberKey);
            else await coordinator.ImportAsCopyAsync(profile, result.Remember);
        }
        finally { DeploymentProfileSecretBinding.Clear(profile); }
    }

    private async Task CreateSharedAsync()
    {
        ProfileDialogResponse? result = await DialogAsync(new("Profiles.CreateShared")
        { Message = Text("Profiles.ShareWarning"), SharePathOption = true, IncludeSecretsOption = true, SharedKeyOption = true });
        if (result is not null) await coordinator.CreateSharedAsync(result.SharePath.Trim(), result.IncludeSecrets, result.RememberKey);
    }

    [RelayCommand]
    private Task SynchronizeAsync() => RunAsync(async () =>
    {
        if (IsShared)
        {
            await coordinator.SynchronizeAsync();
            return;
        }

        await SetupSynchronizationCoreAsync();
    });

    [RelayCommand]
    private Task SetupSynchronizationAsync() => RunAsync(SetupSynchronizationCoreAsync);

    private async Task SetupSynchronizationCoreAsync()
    {
        ProfileDialogResponse? setup = await DialogAsync(new("Profiles.Setup")
        { Message = Text("Profiles.SetupDescription"), SynchronizationSetupOption = true });
        if (setup is null) return;
        if (setup.JoinShared) await ImportFileAsync(true);
        else await CreateSharedAsync();
    }
    [RelayCommand] private Task ToggleSyncAsync() => RunAsync(() => coordinator.SetSynchronizationEnabledAsync(!SyncEnabled));
    [RelayCommand] private Task UseRemoteAsync() => ResolveAsync(true);
    [RelayCommand] private Task KeepLocalAsync() => ResolveAsync(false);

    private Task ResolveAsync(bool useRemote) => RunAsync(async () =>
    {
        if (await ConfirmAsync(useRemote ? "Profiles.UseRemote" : "Profiles.KeepLocal", "Profiles.ConflictWarning"))
            await coordinator.ResolveConflictAsync(useRemote);
    });

    private async Task<bool> ConfirmAsync(string title, string message) =>
        await DialogAsync(new(title) { Message = Text(message) }) is not null;

    private Task<ProfileDialogResponse?> DialogAsync(ProfileDialogRequest request) =>
        ShowDialogAsync?.Invoke(request) ?? Task.FromResult<ProfileDialogResponse?>(null);

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using IDisposable suspension = coordinator.SuspendActivation();
            await action();
            Refresh();
        }
        catch (OperationCanceledException) { }
        catch (LocalProfileLockedException) { Status = Text("Profiles.Locked"); }
        catch (IncompleteProfileCheckpointException) { Status = Text("Profiles.Incomplete"); }
        catch (NotSupportedException) { Status = Text("Profiles.Unsupported"); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or CryptographicException or System.ComponentModel.Win32Exception or
            System.Runtime.InteropServices.COMException or System.Text.Json.JsonException)
        { Status = Text("Profiles.Failed"); }
        finally { IsBusy = false; }
    }
}

/// <summary>Dialog layout inputs; no configuration mutation occurs in the view.</summary>
internal sealed record ProfileDialogRequest(string Title)
{
    public string? Message { get; init; }
    public string? Name { get; init; }
    public bool Passphrase { get; init; }
    public bool ConfirmPassphrase { get; init; }
    public bool IncludeSecretsOption { get; init; }
    public bool RememberOption { get; init; }
    public bool SharedKeyOption { get; init; }
    public bool SharePathOption { get; init; }
    public bool DeleteSharedOption { get; init; }
    public bool SynchronizationSetupOption { get; init; }
}

/// <summary>Transient dialog values; callers never log or persist the package passphrase.</summary>
internal sealed record ProfileDialogResponse(string Name, string Password, bool IncludeSecrets, bool Remember,
    bool RememberKey, string SharePath, bool DeleteShared, bool JoinShared);
