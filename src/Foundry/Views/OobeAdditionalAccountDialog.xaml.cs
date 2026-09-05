// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Services.Configuration;
using Foundry.ViewModels;

namespace Foundry.Views;

public sealed partial class OobeAdditionalAccountDialog : ContentDialog, IDisposable
{
    private bool isDisposed;

    public OobeAdditionalAccountDialog(
        OobeAdditionalAccountDialogViewModel viewModel,
        char[] initialPassword,
        char[] initialConfirmation)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
        Title = ViewModel.Title;
        PrimaryButtonText = ViewModel.PrimaryButtonText;
        CloseButtonText = ViewModel.CloseButtonText;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyPassword(PasswordBox, initialPassword);
        ApplyPassword(ConfirmationBox, initialConfirmation);
    }

    public OobeAdditionalAccountDialogViewModel ViewModel { get; }

    public OobeAdditionalAccountDialogResult? Result { get; private set; }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        isDisposed = true;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result?.Dispose();
        Result = ViewModel.TryCreateResult(PasswordBox.Password, ConfirmationBox.Password, out string validationMessage);
        ValidationTextBlock.Text = validationMessage;
        args.Cancel = Result is null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(OobeAdditionalAccountDialogViewModel.Title), StringComparison.Ordinal))
        {
            Title = ViewModel.Title;
            return;
        }

        if (string.Equals(e.PropertyName, nameof(OobeAdditionalAccountDialogViewModel.PrimaryButtonText), StringComparison.Ordinal))
        {
            PrimaryButtonText = ViewModel.PrimaryButtonText;
            return;
        }

        if (string.Equals(e.PropertyName, nameof(OobeAdditionalAccountDialogViewModel.CloseButtonText), StringComparison.Ordinal))
        {
            CloseButtonText = ViewModel.CloseButtonText;
            return;
        }

        if (string.Equals(e.PropertyName, nameof(OobeAdditionalAccountDialogViewModel.UsePassword), StringComparison.Ordinal) &&
            !ViewModel.UsePassword)
        {
            PasswordBox.Password = string.Empty;
            ConfirmationBox.Password = string.Empty;
            ValidationTextBlock.Text = string.Empty;
        }
    }

    private static void ApplyPassword(PasswordBox passwordBox, char[] value)
    {
        try
        {
            passwordBox.Password = new string(value);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(value.AsSpan()));
        }
    }
}
