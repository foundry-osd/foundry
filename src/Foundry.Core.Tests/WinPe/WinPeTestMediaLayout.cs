// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

/// <summary>
/// Creates the boot files that copype places in a WinPE media folder, so USB workflow tests reach the disk stages.
/// </summary>
internal static class WinPeTestMediaLayout
{
    /// <summary>Mirrors the ADK media layout: ARM64 media is UEFI-only and has no BIOS <c>boot\BCD</c>.</summary>
    public static void Create(string mediaRootPath, WinPeArchitecture architecture)
    {
        Directory.CreateDirectory(Path.Combine(mediaRootPath, "sources"));
        Directory.CreateDirectory(Path.Combine(mediaRootPath, "EFI", "Boot"));
        Directory.CreateDirectory(Path.Combine(mediaRootPath, "EFI", "Microsoft", "Boot"));
        File.WriteAllText(Path.Combine(mediaRootPath, "sources", "boot.wim"), "boot");
        File.WriteAllText(Path.Combine(mediaRootPath, "EFI", "Microsoft", "Boot", "BCD"), "bcd");
        File.WriteAllText(Path.Combine(mediaRootPath, "EFI", "Boot", architecture.ToBootEfiName()), "efi");
        if (architecture == WinPeArchitecture.X64)
        {
            Directory.CreateDirectory(Path.Combine(mediaRootPath, "boot"));
            File.WriteAllText(Path.Combine(mediaRootPath, "boot", "BCD"), "bcd");
        }
    }
}
