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
/// for processes started afterwards. The choice is written to a hand-off file so the next WinPE application can restore it.
/// Outside Windows PE only the current process switches; Windows keeps the loaded layout in the session until sign-out.
/// </summary>
public sealed class WindowsKeyboardLayoutSwitcher : IKeyboardLayoutSwitcher
{
    private const uint KlfActivate = 0x00000001;
    private const uint KlfSetForProcess = 0x00000100;
    private const int KeyboardLayoutNameLength = 9;
    private static readonly TimeSpan WpeUtilTimeout = TimeSpan.FromSeconds(30);

    private readonly string handoffPath;
    private readonly Func<IReadOnlyList<KeyboardLayout>> loadLayouts;
    private readonly Func<string, bool> activateLayout;
    private readonly Func<string> getActiveLayoutId;
    private readonly Func<bool> isWinPe;
    private readonly Func<string, string?> setWinPeSessionLayout;

    /// <summary>
    /// Initializes a switcher that persists choices to <paramref name="handoffPath"/>.
    /// </summary>
    /// <param name="handoffPath">Hand-off file path, normally <see cref="KeyboardLayoutHandoff.DefaultPath"/>.</param>
    public WindowsKeyboardLayoutSwitcher(string handoffPath)
        : this(
            handoffPath,
            KeyboardLayoutCatalog.GetInstalled,
            ActivateForProcess,
            ReadActiveLayoutId,
            WinPeRuntimeDetector.IsWinPeRuntime,
            SetWinPeSessionLayout)
    {
    }

    internal WindowsKeyboardLayoutSwitcher(
        string handoffPath,
        Func<IReadOnlyList<KeyboardLayout>> loadLayouts,
        Func<string, bool> activateLayout,
        Func<string> getActiveLayoutId,
        Func<bool> isWinPe,
        Func<string, string?> setWinPeSessionLayout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handoffPath);
        this.handoffPath = handoffPath;
        this.loadLayouts = loadLayouts;
        this.activateLayout = activateLayout;
        this.getActiveLayoutId = getActiveLayoutId;
        this.isWinPe = isWinPe;
        this.setWinPeSessionLayout = setWinPeSessionLayout;
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

        // The hand-off backs up wpeutil inheritance, so it is written even when wpeutil fails.
        var warnings = new List<string>();
        try
        {
            KeyboardLayoutHandoff.Write(handoffPath, layout.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"The keyboard layout could not be handed off: {exception.Message}");
        }

        string? wpeUtilError = setWinPeSessionLayout(layout.InputLocale);
        if (wpeUtilError is not null)
        {
            warnings.Add(wpeUtilError);
        }

        return new KeyboardLayoutSwitchResult(true, layout.Id, warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    /// <inheritdoc />
    public KeyboardLayoutSwitchResult? RestoreHandoff()
    {
        if (!File.Exists(handoffPath))
        {
            return null;
        }

        if (!KeyboardLayoutHandoff.TryRead(handoffPath, out string layoutId))
        {
            return new KeyboardLayoutSwitchResult(false, null, "The keyboard layout hand-off file is empty or invalid.");
        }

        KeyboardLayout? layout = KeyboardLayoutSelection.Find(GetLayouts(), layoutId);
        if (layout is null)
        {
            return new KeyboardLayoutSwitchResult(false, layoutId, "The handed-off keyboard layout is not installed.");
        }

        // wpeutil usually passed the layout on already; calling it again only delays startup.
        return getActiveLayoutId() == layout.Id
            ? new KeyboardLayoutSwitchResult(true, layout.Id, null)
            : Apply(layout);
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
