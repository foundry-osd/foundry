// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Packages;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

/// <summary>Edits ordered custom actions without exposing Foundry's protected built-in sequence.</summary>
public sealed partial class PostInstallationViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryConfigurationStateService state;
    private readonly PreOobePackageLibraryService library;
    private readonly IFilePickerService picker;
    private readonly IApplicationLocalizationService localization;
    private bool applying;
    private bool disposed;

    public PostInstallationViewModel(IFoundryConfigurationStateService state, PreOobePackageLibraryService library,
        IFilePickerService picker, IApplicationLocalizationService localization)
    {
        this.state = state;
        this.library = library;
        this.picker = picker;
        this.localization = localization;
        state.StateChanged += OnStateChanged;
        localization.LanguageChanged += OnLanguageChanged;
        ApplyState();
    }

    public ObservableCollection<PostInstallationActionRow> Actions { get; } = [];
    [ObservableProperty] public partial bool IsEnabled { get; set; }
    [ObservableProperty] public partial PostInstallationActionRow? SelectedAction { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    public bool CanEdit => IsEnabled && SelectedAction is not null;
    public bool CanMoveUp => CanEdit && Actions.IndexOf(SelectedAction!) > 0;
    public bool CanMoveDown => CanEdit && Actions.IndexOf(SelectedAction!) < Actions.Count - 1;
    public bool HasReadinessIssue => !state.IsPostInstallationReady;
    public string PageTitle => localization.GetString("Nav_PostInstallationKey.Title");
    public string PageDescription => localization.GetString("Nav_PostInstallationKey.Description");
    public string DocumentationUrl => FoundryApplicationInfo.DocumentationUrl + "/foundry-osd/customization/post-installation";
    public string AddLabel => Text("Add");
    public string EditLabel => Text("Edit");
    public string RemoveLabel => localization.GetString("CustomImages.RemoveLabel");
    public string MoveUpLabel => Text("MoveUp");
    public string MoveDownLabel => Text("MoveDown");
    public string RefreshLabel => localization.GetString("Common.Refresh");
    public string ToggleLabel => localization.GetString(SelectedAction?.Action.IsEnabled == true ? "Common.Disable" : "Common.Enable");
    public string EnableLabel => localization.GetString("Common.Enable");
    public string BuiltInDescription => Text("BuiltIns");
    public string ReadinessDescription => Text("Readiness");
    public string OrderHeader => Text("OrderHeader");
    public string NameHeader => localization.GetString("CustomImages.NameLabel");
    public string TypeHeader => Text("TypeHeader");
    public string EnabledHeader => localization.GetString("Common.Enabled");
    public string ContentStatusHeader => Text("ContentStatusHeader");
    public string PowerShellLabel => "PowerShell";
    public string CommandLabel => "CMD";
    public string ApplicationLabel => Text("Application");
    public string RestartLabel => Text("Restart");
    public string Text(string key) => localization.GetString("PostInstallation." + key);

    partial void OnIsEnabledChanged(bool value)
    {
        if (!applying) state.UpdatePreOobe(state.Current.PreOobe with { IsEnabled = value });
        RaiseSelection();
    }

    partial void OnSelectedActionChanged(PostInstallationActionRow? value) => RaiseSelection();

    public PostInstallationActionEditorViewModel CreateEditor(PreOobeActionKind? kind = null)
    {
        PreOobeActionSettings action = kind.HasValue
            ? PreOobeActionSettings.Create(kind.Value, TypeLabel(kind.Value))
            : SelectedAction!.Action;
        return new(action, state.Current.PreOobe, kind.HasValue, library, picker, localization);
    }

    /// <summary>Rejects an edit if a profile switch or concurrent configuration change replaced its baseline.</summary>
    public bool SaveEditor(PostInstallationActionEditorViewModel editor)
    {
        if (!ReferenceEquals(editor.Baseline, state.Current.PreOobe) || !editor.TryBuild(out var action))
        {
            StatusMessage = Text("EditConflict");
            return false;
        }
        var actions = editor.IsNew ? state.Current.PreOobe.Actions.Append(action!).ToArray()
            : state.Current.PreOobe.Actions.Select(item => item.Id == action!.Id ? action : item).ToArray();
        state.UpdatePreOobe(state.Current.PreOobe with { Actions = actions! });
        SelectedAction = Actions.FirstOrDefault(item => item.Action.Id == action!.Id);
        StatusMessage = string.Empty;
        return true;
    }

    [RelayCommand]
    private void Remove()
    {
        if (!CanEdit) return;
        int position = Actions.IndexOf(SelectedAction!);
        string id = SelectedAction!.Action.Id;
        state.UpdatePreOobe(state.Current.PreOobe with { Actions = state.Current.PreOobe.Actions.Where(action => action.Id != id).ToArray() });
        SelectedAction = Actions.ElementAtOrDefault(Math.Min(position, Actions.Count - 1));
    }

    [RelayCommand]
    private void Toggle()
    {
        if (!CanEdit) return;
        string id = SelectedAction!.Action.Id;
        state.UpdatePreOobe(state.Current.PreOobe with
        {
            Actions = state.Current.PreOobe.Actions.Select(action => action.Id == id ? action with { IsEnabled = !action.IsEnabled } : action).ToArray()
        });
    }

    [RelayCommand] private void MoveUp() => Move(-1);
    [RelayCommand] private void MoveDown() => Move(1);
    [RelayCommand] private void Refresh() => ApplyState();

    private void Move(int delta)
    {
        if (!CanEdit) return;
        int index = Actions.IndexOf(SelectedAction!);
        int destination = index + delta;
        if (destination < 0 || destination >= Actions.Count) return;
        var actions = state.Current.PreOobe.Actions.ToList();
        var action = actions[index];
        actions.RemoveAt(index);
        actions.Insert(destination, action);
        state.UpdatePreOobe(state.Current.PreOobe with { Actions = actions });
    }

    private string TypeLabel(PreOobeActionKind kind) => kind switch
    {
        PreOobeActionKind.PowerShell => PowerShellLabel,
        PreOobeActionKind.Command => CommandLabel,
        PreOobeActionKind.Application => ApplicationLabel,
        _ => RestartLabel
    };

    private void ApplyState()
    {
        applying = true;
        try
        {
            string? selectedId = SelectedAction?.Action.Id;
            IsEnabled = state.Current.PreOobe.IsEnabled;
            Actions.Clear();
            foreach (var action in state.Current.PreOobe.Actions)
            {
                bool ready = action.Package is null || library.IsAvailable(action.Package);
                Actions.Add(new(action, Actions.Count + 1, TypeLabel(action.Kind),
                    localization.GetString(action.IsEnabled ? "Common.Enabled" : "Common.Disabled"),
                    localization.GetString(ready ? "CustomImages.Available" : "CustomImages.Missing")));
            }
            SelectedAction = Actions.FirstOrDefault(item => item.Action.Id == selectedId);
            OnPropertyChanged(nameof(HasReadinessIssue));
            RaiseSelection();
        }
        finally { applying = false; }
    }

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        OnPropertyChanged(nameof(ToggleLabel));
    }

    private void OnStateChanged(object? sender, EventArgs args) => ApplyState();
    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs args)
    {
        ApplyState();
        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        state.StateChanged -= OnStateChanged;
        localization.LanguageChanged -= OnLanguageChanged;
    }
}

public sealed record PostInstallationActionRow(PreOobeActionSettings Action, int Position, string Type, string Enabled, string Readiness)
{
    public string Name => Action.Name;
}
