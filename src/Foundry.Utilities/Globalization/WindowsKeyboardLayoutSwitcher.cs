// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Foundry.Utilities.Runtime;

namespace Foundry.Utilities.Globalization;

/// <summary>
/// Switches keyboard layouts with user32 for the current process and, in Windows PE, with <c>wpeutil SetKeyboardLayout</c>
/// for processes started afterwards. Foundry Connect and Foundry Deploy run in the same process, so a layout chosen in one
/// stays active in the other. Outside Windows PE only the current process switches; Windows keeps the loaded layout in the
/// session until sign-out. A switcher from <see cref="CreateForCurrentProcess"/> only ever switches the current process.
/// </summary>
public sealed class WindowsKeyboardLayoutSwitcher : IKeyboardLayoutSwitcher
{
    private const uint KlfActivate = 0x00000001;
    private const uint KlfSetForProcess = 0x00000100;
    private const int KeyboardLayoutNameLength = 9;
    private static readonly TimeSpan WpeUtilTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<IReadOnlyList<KeyboardLayout>> loadLayouts;
    private readonly Func<string, bool> activateLayout;
    private readonly Func<string> getActiveLayoutId;
    private readonly Func<bool> isWinPe;
    private readonly Func<string, string?> setWinPeSessionLayout;

    /// <summary>
    /// Initializes a switcher for a WinPE application: in Windows PE, a switch also applies to processes started afterwards.
    /// </summary>
    public WindowsKeyboardLayoutSwitcher()
        : this(
            KeyboardLayoutCatalog.GetInstalled,
            ActivateForProcess,
            ReadActiveLayoutId,
            WinPeRuntimeDetector.IsWinPeRuntime,
            SetWinPeSessionLayout)
    {
    }

    internal WindowsKeyboardLayoutSwitcher(
        Func<IReadOnlyList<KeyboardLayout>> loadLayouts,
        Func<string, bool> activateLayout,
        Func<string> getActiveLayoutId,
        Func<bool> isWinPe,
        Func<string, string?> setWinPeSessionLayout)
    {
        this.loadLayouts = loadLayouts;
        this.activateLayout = activateLayout;
        this.getActiveLayoutId = getActiveLayoutId;
        this.isWinPe = isWinPe;
        this.setWinPeSessionLayout = setWinPeSessionLayout;
    }

    /// <summary>
    /// Creates a switcher that changes the keyboard layout of the current process only, for applications that run in an
    /// installed Windows (for example during Windows setup). It never runs <c>wpeutil</c> and never changes the Windows input
    /// settings of a user or of the system.
    /// </summary>
    /// <param name="loadLayouts">Returns the layouts the application offers, for example the installed layouts filtered further.</param>
    /// <returns>A process-only switcher.</returns>
    public static WindowsKeyboardLayoutSwitcher CreateForCurrentProcess(Func<IReadOnlyList<KeyboardLayout>> loadLayouts)
    {
        ArgumentNullException.ThrowIfNull(loadLayouts);
        return new WindowsKeyboardLayoutSwitcher(
            loadLayouts,
            ActivateForProcess,
            ReadActiveLayoutId,
            isWinPe: static () => false,
            setWinPeSessionLayout: static _ => null);
    }

    /// <inheritdoc />
    public IReadOnlyList<KeyboardLayout> GetLayouts()
    {
        return loadLayouts();
    }

    /// <inheritdoc />
    public string GetActiveLayoutId()
    {
        return getActiveLayoutId();
    }

    /// <inheritdoc />
    public KeyboardLayoutSwitchResult Apply(KeyboardLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        // LoadKeyboardLayout falls back to the default layout instead of failing, so check what is actually active.
        if (!activateLayout(layout.Id) || getActiveLayoutId() != layout.Id)
        {
            return new KeyboardLayoutSwitchResult(false, layout.Id, "The keyboard layout could not be activated.");
        }

        if (!isWinPe())
        {
            return new KeyboardLayoutSwitchResult(true, layout.Id, null);
        }

        // The layout is already active for this process; a wpeutil failure only affects processes started afterwards.
        return new KeyboardLayoutSwitchResult(true, layout.Id, setWinPeSessionLayout(layout.InputLocale));
    }

    private static bool ActivateForProcess(string layoutId)
    {
        return LoadKeyboardLayout(layoutId, KlfActivate | KlfSetForProcess) != IntPtr.Zero;
    }

    private static string ReadActiveLayoutId()
    {
        var name = new StringBuilder(KeyboardLayoutNameLength);
        return GetKeyboardLayoutName(name) && KeyboardLayoutCatalog.TryNormalizeId(name.ToString(), out string id)
            ? id
            : string.Empty;
    }

    private static string? SetWinPeSessionLayout(string inputLocale)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wpeutil.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("SetKeyboardLayout");
        startInfo.ArgumentList.Add(inputLocale);

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return "wpeutil.exe could not be started.";
            }

            if (!process.WaitForExit(WpeUtilTimeout))
            {
                process.Kill();
                return "wpeutil.exe SetKeyboardLayout timed out.";
            }

            return process.ExitCode == 0 ? null : $"wpeutil.exe SetKeyboardLayout failed with exit code {process.ExitCode}.";
        }
        catch (Win32Exception exception)
        {
            return $"wpeutil.exe could not be started: {exception.Message}";
        }
    }

    [DllImport("user32.dll", EntryPoint = "LoadKeyboardLayoutW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LoadKeyboardLayout(string layoutId, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetKeyboardLayoutNameW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardLayoutName(StringBuilder layoutName);
}
