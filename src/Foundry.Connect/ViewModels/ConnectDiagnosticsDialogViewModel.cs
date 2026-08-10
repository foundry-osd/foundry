// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Connect.Models.Diagnostics;
using Foundry.Connect.Services.Diagnostics;

namespace Foundry.Connect.ViewModels;

public sealed partial class ConnectDiagnosticsDialogViewModel : ObservableObject
{
    private readonly IConnectDiagnosticsSnapshotProvider _snapshotProvider;
    private readonly Func<string, string> _getString;

    public ConnectDiagnosticsDialogViewModel(
        IConnectDiagnosticsSnapshotProvider snapshotProvider,
        Func<string, string>? getString = null)
    {
        _snapshotProvider = snapshotProvider;
        _getString = getString ?? GetEnglishString;
    }

    [ObservableProperty]
    private string displayText = string.Empty;

    [ObservableProperty]
    private string errorText = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    public Task Initialization { get; internal set; } = Task.CompletedTask;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string TitleText => _getString("Diagnostics.Title");

    public string LoadingText => _getString("Diagnostics.Loading");

    public string RefreshText => _getString("Action.Refresh");

    public string CloseText => _getString("Action.Close");

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        ErrorText = string.Empty;

        try
        {
            ConnectDiagnosticsSnapshot snapshot = await _snapshotProvider.CaptureAsync(CancellationToken.None);
            DisplayText = Format(snapshot);
        }
        catch (Exception)
        {
            ErrorText = _getString("Diagnostics.CaptureFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    private string Format(ConnectDiagnosticsSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{_getString("Diagnostics.ApplicationVersion")}: {snapshot.ApplicationVersion}");
        builder.AppendLine($"{_getString("Diagnostics.Runtime")}: {snapshot.RuntimeIdentifier}");
        builder.AppendLine($"{_getString("Diagnostics.ProcessArchitecture")}: {snapshot.ProcessArchitecture}");
        builder.AppendLine($"{_getString("Diagnostics.Configuration")}: {snapshot.ConfigurationSource}");
        builder.AppendLine($"{_getString("Diagnostics.RefreshInterval")}: {snapshot.RefreshInterval}");
        builder.AppendLine($"{_getString("Diagnostics.LastUpdated")}: {snapshot.LastUpdated?.ToString("O") ?? _getString("Diagnostics.Pending")}");
        builder.AppendLine($"{_getString("Diagnostics.Readiness")}: {snapshot.ReadinessState}");
        builder.AppendLine($"{_getString("Diagnostics.ActiveConnection")}: {snapshot.ActiveConnectionSource ?? _getString("Diagnostics.None")}");
        builder.AppendLine();
        builder.AppendLine(_getString("Diagnostics.Adapters"));
        foreach (string adapterSummary in snapshot.AdapterSummaries)
        {
            builder.AppendLine($"- {adapterSummary}");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            builder.AppendLine();
            builder.AppendLine($"{_getString("Diagnostics.LastError")}: {snapshot.LastError}");
        }

        builder.AppendLine();
        builder.AppendLine($"{_getString("Diagnostics.Captured")}: {snapshot.CapturedAt:O}");
        return builder.ToString().TrimEnd();
    }

    private static string GetEnglishString(string key) => key switch
    {
        "Diagnostics.Title" => "Diagnostics",
        "Diagnostics.Loading" => "Loading diagnostics…",
        "Diagnostics.CaptureFailed" => "Unable to capture diagnostics.",
        "Diagnostics.ApplicationVersion" => "Application version",
        "Diagnostics.Runtime" => "Runtime",
        "Diagnostics.ProcessArchitecture" => "Process architecture",
        "Diagnostics.Configuration" => "Configuration",
        "Diagnostics.RefreshInterval" => "Refresh interval",
        "Diagnostics.LastUpdated" => "Last updated",
        "Diagnostics.Pending" => "Pending",
        "Diagnostics.Readiness" => "Readiness",
        "Diagnostics.ActiveConnection" => "Active connection",
        "Diagnostics.None" => "None",
        "Diagnostics.Adapters" => "Adapters",
        "Diagnostics.LastError" => "Last error",
        "Diagnostics.Captured" => "Captured",
        "Action.Refresh" => "Refresh",
        "Action.Close" => "Close",
        _ => key
    };
}
