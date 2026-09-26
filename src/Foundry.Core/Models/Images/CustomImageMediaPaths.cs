// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Images;

/// <summary>Defines the shared custom-image location on ISO and USB media.</summary>
public static class CustomImageMediaPaths
{
    /// <summary>Separates imported and manual images from catalog downloads in the OS cache.</summary>
    public const string RelativeRoot = "Cache/OperatingSystems/Custom";
}
