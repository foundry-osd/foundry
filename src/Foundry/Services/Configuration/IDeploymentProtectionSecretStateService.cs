// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Configuration;

/// <summary>
/// Keeps deployment media password values in volatile process memory.
/// </summary>
public interface IDeploymentProtectionSecretStateService
{
    event EventHandler? Changed;

    bool HasPassword { get; }

    bool HasConfirmation { get; }

    bool IsValid { get; }

    bool ShouldRecommendStrongerPassword { get; }

    void SetPassword(string? value);

    void SetConfirmation(string? value);

    char[] GetPasswordCopy();

    char[] GetConfirmationCopy();

    char[] GetConfirmedPasswordCopy();

    void Clear();
}
