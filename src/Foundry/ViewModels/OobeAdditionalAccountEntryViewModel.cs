// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

public sealed partial class OobeAdditionalAccountEntryViewModel : ObservableObject
{
    private readonly Func<OobeAdditionalAccountEntryViewModel, Task> editAsync;
    private readonly Action<OobeAdditionalAccountEntryViewModel> remove;

    public OobeAdditionalAccountEntryViewModel(
        OobeAdditionalAccountSettings account,
        string accountTypeDisplayName,
        string editText,
        string removeText,
        Func<OobeAdditionalAccountEntryViewModel, Task> editAsync,
        Action<OobeAdditionalAccountEntryViewModel> remove)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
        AccountTypeDisplayName = accountTypeDisplayName;
        EditText = editText;
        RemoveText = removeText;
        this.editAsync = editAsync ?? throw new ArgumentNullException(nameof(editAsync));
        this.remove = remove ?? throw new ArgumentNullException(nameof(remove));
        EditCommand = new AsyncRelayCommand(() => this.editAsync(this));
        RemoveCommand = new RelayCommand(() => this.remove(this));
    }

    [ObservableProperty]
    public partial OobeAdditionalAccountSettings Account { get; set; }

    [ObservableProperty]
    public partial string AccountTypeDisplayName { get; set; }

    [ObservableProperty]
    public partial string EditText { get; set; }

    [ObservableProperty]
    public partial string RemoveText { get; set; }

    public string Id => Account.Id;

    public string UserName => Account.UserName ?? string.Empty;

    public IAsyncRelayCommand EditCommand { get; }

    public IRelayCommand RemoveCommand { get; }

    public void RefreshPresentation(string accountTypeDisplayName, string editText, string removeText)
    {
        AccountTypeDisplayName = accountTypeDisplayName;
        EditText = editText;
        RemoveText = removeText;
    }

    partial void OnAccountChanged(OobeAdditionalAccountSettings value)
    {
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(UserName));
    }
}
