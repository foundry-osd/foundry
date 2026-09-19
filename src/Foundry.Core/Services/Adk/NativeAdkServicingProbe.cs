// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace Foundry.Core.Services.Adk;

/// <summary>Queries machine-wide Windows Installer patch registrations without installing or repairing products.</summary>
internal static class NativeAdkServicingProbe
{
    // ProductCode/PatchCode pairs from the signed Microsoft KB5101684 MSPs for ADK 26100.2454.
    // DisplayVersion does not change when these patches are installed. Review successor identities
    // against https://learn.microsoft.com/windows-hardware/get-started/adk-servicing when updating this baseline.
    private static readonly (string Component, string Product, string Patch)[] RequiredPatches =
    [
        ("DISM", "{765CB4D6-1A08-1F46-81D0-7016DA3604B2}", "{BF926991-C615-45A6-BF73-AFC83E790865}"),
        ("WSIM", "{9BB0E43E-2F04-F989-F188-F787570FD478}", "{4F944331-4B13-4554-9627-2B606D4B4EEE}"),
        ("Oscdimg", "{AA0852D8-D1C3-5D2E-34B2-282A4F10036E}", "{83449E02-24CA-44C1-A0A3-80A9AA2E85F0}")
    ];

    internal static AdkServicingState Detect() => Detect(ReadPatchState);

    /// <summary>Requires positive evidence for every component; missing registration does not prove an installation is unpatched.</summary>
    internal static AdkServicingState Detect(Func<string, string, (uint Error, string State)> readState)
    {
        AdkServicingState result = AdkServicingState.Verified;
        try
        {
            foreach (var patch in RequiredPatches)
            {
                (uint error, string state) = readState(patch.Patch, patch.Product);
                AdkServicingState componentState = error switch
                {
                    1605 or 1647 => AdkServicingState.NotVerified,
                    0 when state is "1" or "2" => AdkServicingState.Verified,
                    0 when state == "4" => AdkServicingState.NotVerified,
                    _ => AdkServicingState.Unknown
                };
                Log.ForContext(typeof(NativeAdkServicingProbe)).Debug(
                    "ADK servicing evidence read. Component={Component}, NativeError={NativeError}, PatchState={PatchState}, ServicingState={ServicingState}",
                    patch.Component, error, state, componentState);
                if (componentState == AdkServicingState.Unknown) result = AdkServicingState.Unknown;
                else if (componentState == AdkServicingState.NotVerified && result != AdkServicingState.Unknown) result = componentState;
            }
            return result;
        }
        catch (Exception exception)
        {
            Log.ForContext(typeof(NativeAdkServicingProbe)).Warning(exception, "ADK servicing evidence could not be read.");
            return AdkServicingState.Unknown;
        }
    }

    private static (uint Error, string State) ReadPatchState(string patch, string product)
    {
        var value = new StringBuilder(16);
        uint length = (uint)value.Capacity;
        uint error = MsiGetPatchInfoExW(patch, product, null, 4, "State", value, ref length);
        return (error, value.ToString());
    }

    [DllImport("msi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint MsiGetPatchInfoExW(string patchCode, string productCode, string? userSid,
        uint context, string property, StringBuilder value, ref uint valueLength);
}
