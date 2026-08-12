// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Application;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Foundry.Services.Startup;

internal sealed class WindowsStartupService : IWindowsStartupService
{
    private const string StartupArgument = "/onBoot";
    private const string StartupTaskId = "FoundryStartup";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PackageIdentityHelper.IsPackaged)
        {
            StartupTask task = await StartupTask.GetAsync(StartupTaskId);
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        string? command = key?.GetValue(ApplicationInfo.ProductName) as string;
        return command?.Contains(ApplicationInfo.ExecutablePath, StringComparison.OrdinalIgnoreCase) == true;
    }

    public async Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PackageIdentityHelper.IsPackaged)
        {
            StartupTask task = await StartupTask.GetAsync(StartupTaskId);
            cancellationToken.ThrowIfCancellationRequested();
            if (!enabled)
            {
                task.Disable();
                return false;
            }

            StartupTaskState state = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy
                ? task.State
                : await task.RequestEnableAsync();
            return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(
                ApplicationInfo.ProductName,
                $"\"{ApplicationInfo.ExecutablePath}\" {StartupArgument}",
                RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ApplicationInfo.ProductName, throwOnMissingValue: false);
        }

        return enabled;
    }
}
