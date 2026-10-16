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
/// Outside Windows PE only the current process changes, so debugging never alters the host's keyboard settings.
/// </summary>
public sealed class WindowsKeyboardLayoutSwitcher : IKeyboardLayoutSwitcher
{
    private const uint KlfActivate = 0x00000001;
    private const uint KlfSetForProcess = 0x00000100;
    private const int KeyboardLayoutNameLength = 9;
    private static readonly TimeSpan WpeUtilTimeout = TimeSpan.FromSeconds(30);

    private readonly string handoffPath;
    private IReadOnlyList<KeyboardLayout>? layouts;

    /// <summary>
    /// Initializes a switcher that persists choices to <paramref name="handoffPath"/>.
    /// </summary>
    /// <param name="handoffPath">Hand-off file path, normally <see cref="KeyboardLayoutHandoff.DefaultPath"/>.</param>
    public WindowsKeyboardLayoutSwitcher(string handoffPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handoffPath);
        this.handoffPath = handoffPath;
    }

    /// <inheritdoc />
    public IReadOnlyList<KeyboardLayout> GetLayouts()
    {
        return layouts ??= KeyboardLayoutCatalog.LoadInstalled();
    }

    /// <inheritdoc />
    public string GetActiveLayoutId()
    {
        var name = new StringBuilder(KeyboardLayoutNameLength);
        return GetKeyboardLayoutName(name) && KeyboardLayoutCatalog.TryNormalizeId(name.ToString(), out string id)
            ? id
            : string.Empty;
    }

    /// <inheritdoc />
    public KeyboardLayoutSwitchResult Apply(KeyboardLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (LoadKeyboardLayout(layout.Id, KlfActivate | KlfSetForProcess) == IntPtr.Zero)
        {
            return new KeyboardLayoutSwitchResult(false, layout.Id, new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        if (!WinPeRuntimeDetector.IsWinPeRuntime())
        {
            return new KeyboardLayoutSwitchResult(true, layout.Id, null);
        }

        string? error = SetWinPeSessionLayout(layout.InputLocale);
        if (error is not null)
        {
            return new KeyboardLayoutSwitchResult(false, layout.Id, error);
        }

        try
        {
            KeyboardLayoutHandoff.Write(handoffPath, layout.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new KeyboardLayoutSwitchResult(false, layout.Id, $"The layout is active but could not be handed off: {exception.Message}");
        }

        return new KeyboardLayoutSwitchResult(true, layout.Id, null);
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
        return layout is null
            ? new KeyboardLayoutSwitchResult(false, layoutId, "The handed-off keyboard layout is not installed.")
            : Apply(layout);
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

    [DllImport("user32.dll", EntryPoint = "LoadKeyboardLayoutW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LoadKeyboardLayout(string layoutId, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetKeyboardLayoutNameW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardLayoutName(StringBuilder layoutName);
}
