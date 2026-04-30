using Foundry.Core.Services.Application;
using Microsoft.Windows.Storage.Pickers;

namespace Foundry.Services.Application;

public sealed class WinUiFilePickerService : IFilePickerService
{
    public async Task<string?> PickOpenFileAsync(FileOpenPickerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var picker = new FileOpenPicker(App.MainWindow.AppWindow.Id)
        {
            Title = request.Title
        };

        foreach (string filter in NormalizeFileTypeFilters(request.FileTypeFilters))
        {
            picker.FileTypeFilter.Add(filter);
        }

        PickFileResult? result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    public async Task<string?> PickSaveFileAsync(FileSavePickerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var picker = new FileSavePicker(App.MainWindow.AppWindow.Id)
        {
            Title = request.Title,
            SuggestedFileName = request.SuggestedFileName,
            DefaultFileExtension = request.DefaultFileExtension ?? ResolveDefaultExtension(request.FileTypeChoices)
        };

        foreach (FilePickerTypeChoice choice in request.FileTypeChoices)
        {
            picker.FileTypeChoices.Add(choice.Name, choice.Extensions.Select(NormalizeExtension).ToList());
        }

        PickFileResult? result = await picker.PickSaveFileAsync();
        return result?.Path;
    }

    public async Task<string?> PickFolderAsync(FolderPickerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var picker = new FolderPicker(App.MainWindow.AppWindow.Id)
        {
            Title = request.Title
        };

        PickFolderResult? result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }

    private static IReadOnlyList<string> NormalizeFileTypeFilters(IReadOnlyList<string> filters)
    {
        if (filters.Count == 0)
        {
            return ["*"];
        }

        return filters.Select(NormalizeExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || extension.Trim() == "*")
        {
            return "*";
        }

        string trimmed = extension.Trim();
        return trimmed.StartsWith(".", StringComparison.Ordinal) ? trimmed : $".{trimmed}";
    }

    private static string? ResolveDefaultExtension(IReadOnlyList<FilePickerTypeChoice> choices)
    {
        return choices.FirstOrDefault()?.Extensions.FirstOrDefault() is { } extension
            ? NormalizeExtension(extension)
            : null;
    }
}
