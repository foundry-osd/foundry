// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

public static class DeploymentProtectionPasswordRules
{
    public const int MinimumLength = 8;
    public const int RecommendedLength = 12;

    public static bool IsValid(string? password)
    {
        return password?.Length >= MinimumLength;
    }

    public static bool ShouldRecommendStrongerPassword(string? password)
    {
        return IsValid(password) && password!.Length < RecommendedLength;
    }
}
