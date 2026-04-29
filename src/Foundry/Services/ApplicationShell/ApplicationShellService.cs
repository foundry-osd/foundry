using System.Diagnostics;
using System.Runtime.InteropServices;
using Foundry.Models.Configuration;
using Foundry.Services.ApplicationUpdate;
using Foundry.Services.Localization;
using Foundry.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Foundry.Services.ApplicationShell;

public sealed class ApplicationShellService : IApplicationShellService
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxYesNo = 0x00000004;
    private const uint MessageBoxIconInformation = 0x00000040;
    private const uint MessageBoxIconWarning = 0x00000030;
    private const uint MessageBoxIconError = 0x00000010;
    private const uint MessageBoxDefaultButton2 = 0x00000100;
    private const int MessageBoxResultYes = 6;

    private readonly ILocalizationService _localizationService;
    private MainWindow? _mainWindow;

    public ApplicationShellService(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    public void AttachMainWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void Shutdown()
    {
        Application.Current.Exit();
    }

    public void ShowAbout()
    {
        _ = ShowAboutAsync();
    }

    public void ShowUpdateAvailable(ApplicationUpdateInfo updateInfo)
    {
        ArgumentNullException.ThrowIfNull(updateInfo);
        _ = ShowUpdateAvailableAsync(updateInfo);
    }

    public void OpenUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    public Task<string?> PickIsoOutputPathAsync(string defaultFileName)
    {
        StringsWrapper strings = _localizationService.Strings;
        return PickSaveFilePathCore(
            strings["General.IsoPickerTitle"],
            ".iso",
            string.IsNullOrWhiteSpace(defaultFileName) ? "foundry-winpe.iso" : defaultFileName);
    }

    public async Task<string?> PickOpenFilePathAsync(string title, string filter)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };

        foreach (string extension in ParseFilterExtensions(filter, ".json"))
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializePicker(picker);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public Task<string?> PickSaveFilePathAsync(string title, string filter, string defaultFileName)
    {
        string extension = ParseFilterExtensions(filter, ".json").FirstOrDefault() ?? ".json";
        return PickSaveFilePathCore(title, extension, defaultFileName);
    }

    public Task<IReadOnlyList<AutopilotProfileSettings>?> PickAutopilotProfilesForImportAsync(IReadOnlyList<AutopilotProfileSettings> availableProfiles)
    {
        ArgumentNullException.ThrowIfNull(availableProfiles);
        return ShowAutopilotProfileSelectionAsync(availableProfiles);
    }

    public async Task<string?> PickFolderPathAsync(string title, string? initialPath = null)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add("*");

        InitializePicker(picker);
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    public void ShowMessage(string title, string message, ApplicationMessageKind kind)
    {
        _ = ShowNativeMessage(title, message, MessageBoxOk | ToNativeIcon(kind));
    }

    public bool ConfirmWarning(string title, string message)
    {
        return ShowNativeMessage(title, message, MessageBoxYesNo | MessageBoxIconWarning | MessageBoxDefaultButton2) == MessageBoxResultYes;
    }

    private async Task ShowAboutAsync()
    {
        ContentDialog dialog = CreateContentDialog();
        dialog.Title = _localizationService.Strings["About.Title"];
        dialog.CloseButtonText = "Close";
        dialog.Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Foundry", Style = TryGetTextStyle("TitleTextBlockStyle") },
                new TextBlock { Text = string.Format(_localizationService.CurrentCulture, _localizationService.Strings["Common.VersionFormat"], FoundryApplicationInfo.Version) },
                new TextBlock { Text = _localizationService.Strings["About.DescriptionLine1"], TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = _localizationService.Strings["About.DescriptionLine2"], TextWrapping = TextWrapping.Wrap }
            }
        };

        await dialog.ShowAsync();
    }

    private async Task ShowUpdateAvailableAsync(ApplicationUpdateInfo updateInfo)
    {
        ContentDialog dialog = CreateContentDialog();
        dialog.Title = _localizationService.Strings["UpdateAvailable.Title"];
        dialog.PrimaryButtonText = _localizationService.Strings["UpdateAvailable.OpenRelease"];
        dialog.CloseButtonText = _localizationService.Strings["UpdateAvailable.Later"];
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.Content = new ScrollViewer
        {
            MaxHeight = 460,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = string.Format(
                            _localizationService.CurrentCulture,
                            _localizationService.Strings["UpdateAvailable.SummaryFormat"],
                            updateInfo.CurrentVersion,
                            updateInfo.LatestVersion),
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(updateInfo.ReleaseNotes)
                            ? _localizationService.Strings["UpdateAvailable.NotesEmpty"]
                            : updateInfo.ReleaseNotes,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            OpenUrl(updateInfo.ReleaseUrl);
        }
    }

    private async Task<IReadOnlyList<AutopilotProfileSettings>?> ShowAutopilotProfileSelectionAsync(IReadOnlyList<AutopilotProfileSettings> availableProfiles)
    {
        var viewModel = new AutopilotProfileSelectionDialogViewModel(_localizationService, availableProfiles);
        var listView = new ListView
        {
            ItemsSource = viewModel.Profiles,
            SelectionMode = ListViewSelectionMode.Multiple,
            MaxHeight = 520
        };
        listView.ItemTemplate = CreateAutopilotProfileTemplate();

        ContentDialog dialog = CreateContentDialog();
        dialog.Title = _localizationService.Strings["Autopilot.ProfileSelectionTitle"];
        dialog.PrimaryButtonText = _localizationService.Strings["Autopilot.ProfileSelectionImport"];
        dialog.CloseButtonText = _localizationService.Strings["Common.Cancel"];
        dialog.Content = listView;

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            viewModel.Dispose();
            return null;
        }

        foreach (SelectableAutopilotProfileEntry profile in viewModel.Profiles)
        {
            profile.IsSelected = listView.SelectedItems.Contains(profile);
        }

        IReadOnlyList<AutopilotProfileSettings> selected = viewModel.GetSelectedProfiles();
        viewModel.Dispose();
        return selected;
    }

    private async Task<string?> PickSaveFilePathCore(string title, string extension, string defaultFileName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(defaultFileName)
        };
        picker.FileTypeChoices.Add(title, [extension]);

        InitializePicker(picker);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    private ContentDialog CreateContentDialog()
    {
        if (_mainWindow?.Content is not FrameworkElement root)
        {
            throw new InvalidOperationException("The main window content is not available.");
        }

        return new ContentDialog
        {
            XamlRoot = root.XamlRoot
        };
    }

    private void InitializePicker(object picker)
    {
        IntPtr hwnd = _mainWindow is null ? IntPtr.Zero : WindowNative.GetWindowHandle(_mainWindow);
        if (hwnd != IntPtr.Zero)
        {
            InitializeWithWindow.Initialize(picker, hwnd);
        }
    }

    private int ShowNativeMessage(string title, string message, uint flags)
    {
        IntPtr hwnd = _mainWindow is null ? IntPtr.Zero : WindowNative.GetWindowHandle(_mainWindow);
        return MessageBox(hwnd, message, title, flags);
    }

    private static uint ToNativeIcon(ApplicationMessageKind kind)
    {
        return kind switch
        {
            ApplicationMessageKind.Warning => MessageBoxIconWarning,
            ApplicationMessageKind.Error => MessageBoxIconError,
            _ => MessageBoxIconInformation
        };
    }

    private static IEnumerable<string> ParseFilterExtensions(string filter, string fallback)
    {
        string[] parts = filter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string part in parts)
        {
            foreach (string token in part.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!token.StartsWith("*.", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return token[1..];
            }
        }

        if (!parts.Any(part => part.Contains("*.", StringComparison.Ordinal)))
        {
            yield return fallback;
        }
    }

    private static Style? TryGetTextStyle(string key)
    {
        return Application.Current.Resources.TryGetValue(key, out object value) ? value as Style : null;
    }

    private static DataTemplate CreateAutopilotProfileTemplate()
    {
        const string xaml = """
            <DataTemplate
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid ColumnDefinitions="2*,*,*" Padding="8">
                    <TextBlock Text="{Binding DisplayName}" TextTrimming="CharacterEllipsis" />
                    <TextBlock Grid.Column="1" Text="{Binding FolderName}" TextTrimming="CharacterEllipsis" />
                    <TextBlock Grid.Column="2" Text="{Binding Profile.Source}" TextTrimming="CharacterEllipsis" />
                </Grid>
            </DataTemplate>
            """;

        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
