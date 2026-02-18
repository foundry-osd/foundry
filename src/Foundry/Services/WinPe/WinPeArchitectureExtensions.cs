namespace Foundry.Services.WinPe;

internal static class WinPeArchitectureExtensions
{
    public static string ToCopypeArchitecture(this WinPeArchitecture architecture)
    {
        return architecture switch
        {
            WinPeArchitecture.X64 => "amd64",
            WinPeArchitecture.Arm64 => "arm64",
            _ => throw new ArgumentOutOfRangeException(nameof(architecture), architecture, "Unsupported WinPE architecture.")
        };
    }

    public static string ToBootEfiName(this WinPeArchitecture architecture)
    {
        return architecture switch
        {
            WinPeArchitecture.X64 => "bootx64.efi",
            WinPeArchitecture.Arm64 => "bootaa64.efi",
            _ => throw new ArgumentOutOfRangeException(nameof(architecture), architecture, "Unsupported WinPE architecture.")
        };
    }
}
