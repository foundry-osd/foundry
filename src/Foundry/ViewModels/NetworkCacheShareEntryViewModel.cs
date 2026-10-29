// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

/// <summary>One row of the network cache share table.</summary>
public sealed partial class NetworkCacheShareEntryViewModel(NetworkCacheShare settings) : ObservableObject
{
    public string Id => Settings.Id;
    public string Path => Settings.Path;
    public Style StatusStyle => (Style)Application.Current.Resources[!IsReady || IsUnpublished ? "FoundryCriticalTextBlockStyle"
        : NeedsPublication ? "FoundryCautionTextBlockStyle" : "FoundrySuccessTextBlockStyle"];

    [ObservableProperty]
    public partial NetworkCacheShare Settings { get; set; } = settings;

    /// <summary>Gets or sets the user name of the share's account, or the text that stands for no account.</summary>
    [ObservableProperty]
    public partial string AccountText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what this share still needs before media can be created, or, once it is valid, whether it has the latest
    /// changes of this PC.
    /// </summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsReady { get; set; }

    /// <summary>Gets or sets whether this valid share does not have the latest changes of this PC yet.</summary>
    [ObservableProperty]
    public partial bool NeedsPublication { get; set; }

    /// <summary>Gets or sets whether this valid share holds no usable publication of this PC, so devices that use it stop at startup.</summary>
    [ObservableProperty]
    public partial bool IsUnpublished { get; set; }

    /// <summary>Gets or sets the space of what devices downloaded from the internet, or why it is unknown; empty until measured.</summary>
    [ObservableProperty]
    public partial string DownloadsText { get; set; } = string.Empty;

    /// <summary>Gets or sets the space of the content Foundry OSD published, or why it is unknown; empty until measured.</summary>
    [ObservableProperty]
    public partial string ImportedContentText { get; set; } = string.Empty;

    /// <summary>Gets or sets the measured bytes of <see cref="DownloadsText"/>; <see langword="null"/> while unknown.</summary>
    [ObservableProperty]
    public partial long? DownloadsBytes { get; set; }

    /// <summary>Gets or sets the measured bytes of <see cref="ImportedContentText"/>; <see langword="null"/> while unknown.</summary>
    [ObservableProperty]
    public partial long? ImportedContentBytes { get; set; }

    /// <summary>
    /// Gets the sort key of the downloads column: its bytes, so sizes sort by value rather than as text, with an unknown size
    /// after every measured one in ascending order.
    /// </summary>
    public long DownloadsSortKey => DownloadsBytes ?? long.MaxValue;

    /// <summary>Gets the sort key of the imported content column, ordered like <see cref="DownloadsSortKey"/>.</summary>
    public long ImportedContentSortKey => ImportedContentBytes ?? long.MaxValue;

    /// <summary>Gets or sets whether the last measurement opened the share; cleaning it up requires a reachable share.</summary>
    [ObservableProperty]
    public partial bool IsReachable { get; set; }

    partial void OnSettingsChanged(NetworkCacheShare oldValue, NetworkCacheShare newValue)
    {
        OnPropertyChanged(nameof(Path));
        // A new path or account is another share as far as space goes: its last measurement no longer applies.
        if (!string.Equals(oldValue.Path, newValue.Path, StringComparison.OrdinalIgnoreCase) || !string.Equals(oldValue.AccountId, newValue.AccountId, StringComparison.Ordinal))
        {
            DownloadsText = string.Empty;
            ImportedContentText = string.Empty;
            DownloadsBytes = null;
            ImportedContentBytes = null;
            IsReachable = false;
        }
    }

    partial void OnDownloadsBytesChanged(long? value) => OnPropertyChanged(nameof(DownloadsSortKey));

    partial void OnImportedContentBytesChanged(long? value) => OnPropertyChanged(nameof(ImportedContentSortKey));

    partial void OnIsReadyChanged(bool value) => OnPropertyChanged(nameof(StatusStyle));

    partial void OnNeedsPublicationChanged(bool value) => OnPropertyChanged(nameof(StatusStyle));

    partial void OnIsUnpublishedChanged(bool value) => OnPropertyChanged(nameof(StatusStyle));
}
