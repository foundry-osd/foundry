// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes profile roaming consent for one network transport.
/// </summary>
public sealed record NetworkProfileRoamingTransportSettings
{
    private bool _isEnabled;
    private bool _isEnabledConfigured;
    private bool _includePrivateKeyMaterial;
    private bool _includePrivateKeyMaterialConfigured;

    /// <summary>
    /// Gets whether profile roaming is enabled for the transport.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        init
        {
            _isEnabledConfigured = true;
            _isEnabled = value;
        }
    }

    /// <summary>
    /// Gets whether PFX/private-key material may be included for the transport.
    /// </summary>
    public bool IncludePrivateKeyMaterial
    {
        get => IsEnabled && _includePrivateKeyMaterial;
        init
        {
            _includePrivateKeyMaterialConfigured = true;
            _includePrivateKeyMaterial = value;
        }
    }

    internal bool IsEnabledConfigured => _isEnabledConfigured;

    internal bool IncludePrivateKeyMaterialConfigured => _includePrivateKeyMaterialConfigured;
}
