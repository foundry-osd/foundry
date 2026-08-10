// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Connect.Models.Diagnostics;
using Foundry.Connect.Services.Diagnostics;

namespace Foundry.Connect.ViewModels;

public sealed partial class ConnectDiagnosticsDialogViewModel(IConnectDiagnosticsSnapshotProvider snapshotProvider)
    : ObservableObject
{
    [ObservableProperty]
    private string displayText = string.Empty;

    [ObservableProperty]
    private string errorText = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    public Task Initialization { get; internal set; } = Task.CompletedTask;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        ErrorText = string.Empty;

        try
        {
            ConnectDiagnosticsSnapshot snapshot = await snapshotProvider.CaptureAsync(CancellationToken.None);
            DisplayText = Format(snapshot);
        }
        catch (Exception)
        {
            ErrorText = "Unable to capture diagnostics.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    private static string Format(ConnectDiagnosticsSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Application version: {snapshot.ApplicationVersion}");
        builder.AppendLine($"Runtime: {snapshot.RuntimeIdentifier}");
        builder.AppendLine($"Process architecture: {snapshot.ProcessArchitecture}");
        builder.AppendLine($"Configuration: {snapshot.ConfigurationSource}");
        builder.AppendLine($"Refresh interval: {snapshot.RefreshInterval}");
        builder.AppendLine($"Last updated: {snapshot.LastUpdated?.ToString("O") ?? "Pending"}");
        builder.AppendLine($"Readiness: {snapshot.ReadinessState}");
        builder.AppendLine($"Active connection: {snapshot.ActiveConnectionSource ?? "None"}");
        builder.AppendLine();
        builder.AppendLine("Adapters");
        foreach (string adapterSummary in snapshot.AdapterSummaries)
        {
            builder.AppendLine($"- {adapterSummary}");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            builder.AppendLine();
            builder.AppendLine($"Last error: {snapshot.LastError}");
        }

        builder.AppendLine();
        builder.AppendLine($"Captured: {snapshot.CapturedAt:O}");
        return builder.ToString().TrimEnd();
    }
}
