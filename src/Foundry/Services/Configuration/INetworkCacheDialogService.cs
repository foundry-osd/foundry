// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>
/// Shows the dialogs that add and edit network cache accounts, shares and IP scopes. Each dialog stays open while its
/// save callback returns a message, so the entry can be corrected in place.
/// </summary>
public interface INetworkCacheDialogService
{
    /// <summary>Asks for an account user name and its password with a confirmation.</summary>
    /// <param name="request">What the dialog starts with and which inputs it offers.</param>
    /// <param name="trySave">Saves the input; returns the refusal reason, or null.</param>
    Task ShowAccountAsync(NetworkCacheAccountDialogRequest request, Func<NetworkCacheAccountDialogInput, string?> trySave);

    /// <summary>Asks for a share path and the account that opens it.</summary>
    /// <param name="request">What the dialog starts with and the accounts it offers.</param>
    /// <param name="trySave">Saves the input; returns the refusal reason, or null.</param>
    Task ShowShareAsync(NetworkCacheShareDialogRequest request, Func<NetworkCacheShareDialogInput, string?> trySave);

    /// <summary>Asks for an IP scope and the ordered shares a device inside it tries.</summary>
    /// <param name="request">What the dialog starts with and the shares it offers, in their initial order.</param>
    /// <param name="trySave">Saves the input; returns the refusal reason, or null.</param>
    Task ShowScopeAsync(NetworkCacheScopeDialogRequest request, Func<NetworkCacheScopeDialogInput, string?> trySave);
}

/// <summary>Describes the account dialog.</summary>
/// <param name="IsNew">Whether an account is being added rather than edited.</param>
/// <param name="UserName">The current user name, empty for a new account.</param>
/// <param name="PasswordOnly">
/// Whether only the password can be entered: a followed publication manages the account, so its user name is read-only.
/// </param>
/// <param name="HasPassword">Whether the account already has a password, which an empty password box keeps.</param>
public sealed record NetworkCacheAccountDialogRequest(bool IsNew, string UserName, bool PasswordOnly = false, bool HasPassword = false);

/// <summary>Carries what was entered in the account dialog. The save callback copies the passwords and must not keep this record.</summary>
public sealed record NetworkCacheAccountDialogInput(string UserName, ReadOnlyMemory<char> Password, ReadOnlyMemory<char> Confirmation);

/// <summary>One account offered by the share dialog.</summary>
public sealed record NetworkCacheAccountOption(string? AccountId, string DisplayName);

/// <summary>Describes the share dialog.</summary>
/// <param name="IsNew">Whether a share is being added rather than edited.</param>
/// <param name="Path">The current UNC path, empty for a new share.</param>
/// <param name="AccountId">The current account, or <see langword="null"/> for no account.</param>
/// <param name="Accounts">The accounts to offer; the first option stands for no account.</param>
public sealed record NetworkCacheShareDialogRequest(bool IsNew, string Path, string? AccountId, IReadOnlyList<NetworkCacheAccountOption> Accounts);

/// <summary>Carries what was entered in the share dialog.</summary>
public sealed record NetworkCacheShareDialogInput(string Path, string? AccountId);

/// <summary>One share offered by the scope dialog, and whether the scope uses it.</summary>
public sealed record NetworkCacheScopeShareOption(string ShareId, string Path, bool IsIncluded);

/// <summary>Describes the scope dialog.</summary>
/// <param name="IsNew">Whether a scope is being added rather than edited.</param>
/// <param name="Name">The current name, empty for a new scope.</param>
/// <param name="Kind">The current type of value.</param>
/// <param name="Value">The current value, empty for a new scope.</param>
/// <param name="Shares">Every share, with the scope's shares first in their order.</param>
public sealed record NetworkCacheScopeDialogRequest(bool IsNew, string Name, NetworkCacheScopeKind Kind, string Value,
    IReadOnlyList<NetworkCacheScopeShareOption> Shares);

/// <summary>Carries what was entered in the scope dialog; <paramref name="ShareIds"/> lists the checked shares in try order.</summary>
public sealed record NetworkCacheScopeDialogInput(string Name, NetworkCacheScopeKind Kind, string Value, IReadOnlyList<string> ShareIds);
