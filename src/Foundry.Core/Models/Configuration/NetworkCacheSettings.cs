// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes the SMB shares Foundry Deploy uses as a shared cache, the accounts that open them, and the IP scopes that
/// choose a share for a device. Account passwords are never stored here: they live in the secret vault by account id.
/// </summary>
public sealed record NetworkCacheSettings
{
    public bool IsEnabled { get; init; }
    public IReadOnlyList<NetworkCacheAccount> Accounts { get; init; } = [];
    public IReadOnlyList<NetworkCacheShare> Shares { get; init; } = [];
    /// <summary>Gets the scopes in authoring order; the most specific scope that contains the device wins and ties keep this order.</summary>
    public IReadOnlyList<NetworkCacheScope> Scopes { get; init; } = [];
}

/// <summary>An account that can open one or more shares; its password is a vault secret keyed by <see cref="Id"/>.</summary>
public sealed record NetworkCacheAccount
{
    public string Id { get; init; } = string.Empty;
    /// <summary>Gets the account as Windows expects it, for example <c>CONTOSO\svc-foundry</c> or <c>svc@contoso.com</c>.</summary>
    public string UserName { get; init; } = string.Empty;
}

/// <summary>A share used as a network cache. Without an account, devices connect with no credentials and never write to it.</summary>
public sealed record NetworkCacheShare
{
    public string Id { get; init; } = string.Empty;
    /// <summary>Gets the UNC path, for example <c>\\server\share</c>.</summary>
    public string Path { get; init; } = string.Empty;
    public string? AccountId { get; init; }
}

/// <summary>An IP scope and the shares a device inside it tries, in order.</summary>
public sealed record NetworkCacheScope
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public NetworkCacheScopeKind Kind { get; init; }
    public string Value { get; init; } = string.Empty;
    public IReadOnlyList<string> ShareIds { get; init; } = [];
}
