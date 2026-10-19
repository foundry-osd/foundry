// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Serilog;

namespace Foundry.Services.Startup;

/// <summary>
/// Blocks the x64 build when it runs under emulation on Windows ARM64 and points the user to the ARM64 installer.
/// </summary>
internal static class Arm64BuildRequirementGate
{
    private static readonly ILogger Logger = Log.ForContext(typeof(Arm64BuildRequirementGate));

    /// <summary>
    /// Gets whether this process is the x64 build running on Windows ARM64 and must not continue to the shell.
    /// </summary>
    public static bool IsBlocked { get; } = ProcessArchitectureCompatibility.RequiresArm64Build(
        RuntimeInformation.ProcessArchitecture,
        RuntimeInformation.OSArchitecture);

    /// <summary>
    /// Shows the blocking dialog over the main window and opens the latest release page when the user chooses to download
    /// the ARM64 build. The caller closes the application once this method returns.
    /// </summary>
    /// <param name="rootElement">Main window root that hosts the dialog; the dialog waits until it is loaded.</param>
    public static async Task ShowBlockingDialogAsync(FrameworkElement rootElement)
    {
        Logger.Warning(
            "Foundry startup blocked because the x64 build runs under emulation on ARM64 Windows. ProcessArchitecture={ProcessArchitecture}, OSArchitecture={OSArchitecture}",
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.OSArchitecture.ToString());

        await WaitUntilLoadedAsync(rootElement);

        IApplicationLocalizationService localization = App.GetService<IApplicationLocalizationService>();
        bool downloadRequested = await App.GetService<IDialogService>().ConfirmAsync(new ConfirmationDialogRequest(
            localization.GetString("Startup.Arm64Required.Title"),
            localization.GetString("Startup.Arm64Required.Message"),
            localization.GetString("Startup.Arm64Required.DownloadButton"),
            localization.GetString("Common.Close")));
        if (!downloadRequested)
        {
            return;
        }

        try
        {
            await App.GetService<IExternalProcessLauncher>().OpenUriAsync(new Uri(FoundryApplicationInfo.LatestReleaseUrl));
        }
        catch (Exception)
        {
            // The launcher already logged the failure; the blocked application must still close.
        }
    }

    private static Task WaitUntilLoadedAsync(FrameworkElement element)
    {
        if (element.IsLoaded)
        {
            return Task.CompletedTask;
        }

        // ContentDialog needs the window content's XamlRoot, which is only reliable once the root element is loaded.
        TaskCompletionSource loaded = new();
        void OnLoaded(object sender, RoutedEventArgs args)
        {
            element.Loaded -= OnLoaded;
            loaded.TrySetResult();
        }

        element.Loaded += OnLoaded;
        return loaded.Task;
    }
}
