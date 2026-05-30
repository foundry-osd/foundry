using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Stages the interactive Autopilot registration assistant into the retained Foundry runtime path.
/// </summary>
public sealed class AutopilotInteractiveRegistrationProvisioningService : IAutopilotInteractiveRegistrationProvisioningService
{
    private const string ScriptFileName = "Start-FoundryAutopilotRegistration.ps1";
    private const string LauncherFileName = "Start-FoundryAutopilotRegistration.cmd";
    private const string OobeLauncherFileName = "Start-FoundryAutopilotRegistrationOobe.cmd";
    private const string OobeWaiterFileName = "Wait-FoundryAutopilotRegistrationOobe.ps1";
    private const string ForegroundWrapperFileName = "Start-FoundryAutopilotRegistrationForeground.ps1";
    private const string ServiceUiFileName = "ServiceUI.exe";
    private const string OobeCommandFileName = "OOBE.cmd";
    private const string ConfigFileName = "config.json";
    private const string SetupCompleteMarkerKey = "FOUNDRY AUTOPILOT REGISTRATION";
    private const string ScriptResourceName = "Foundry.Deploy.AutopilotRegistration.Start-FoundryAutopilotRegistration.ps1";
    private const string ServiceUiResourceName = "Foundry.Deploy.AutopilotRegistration.ServiceUI.exe";
    private const string RuntimeRegistrationRoot = "%SystemRoot%\\Temp\\Foundry\\AutopilotRegistration";
    private const string RuntimeLogRoot = "%SystemRoot%\\Temp\\Foundry\\Logs\\AutopilotRegistration";
    private const string RuntimeStateRoot = "%SystemRoot%\\Temp\\Foundry\\AutopilotRegistration\\State";
    private const string FoundryBootstrapClientId = "83eb3a92-030d-49b7-881b-32a1eb3e110a";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly ISetupCompleteScriptService _setupCompleteScriptService;

    public AutopilotInteractiveRegistrationProvisioningService(ISetupCompleteScriptService setupCompleteScriptService)
    {
        _setupCompleteScriptService = setupCompleteScriptService;
    }

    /// <inheritdoc />
    public AutopilotInteractiveRegistrationProvisioningResult Provision(string targetWindowsPartitionRoot)
    {
        if (string.IsNullOrWhiteSpace(targetWindowsPartitionRoot))
        {
            throw new ArgumentException("Target Windows partition root is required.", nameof(targetWindowsPartitionRoot));
        }

        string registrationRoot = GetRegistrationRoot(targetWindowsPartitionRoot);
        string stateRoot = Path.Combine(registrationRoot, "State");
        string logRoot = GetLogRoot(targetWindowsPartitionRoot);
        string scriptPath = Path.Combine(registrationRoot, ScriptFileName);
        string launcherPath = Path.Combine(registrationRoot, LauncherFileName);
        string oobeLauncherPath = Path.Combine(registrationRoot, OobeLauncherFileName);
        string oobeWaiterPath = Path.Combine(registrationRoot, OobeWaiterFileName);
        string foregroundWrapperPath = Path.Combine(registrationRoot, ForegroundWrapperFileName);
        string serviceUiPath = Path.Combine(registrationRoot, ServiceUiFileName);
        string oobeCommandPath = GetOobeCommandPath(targetWindowsPartitionRoot);
        string configPath = Path.Combine(registrationRoot, ConfigFileName);
        string setupCompletePath = GetSetupCompletePath(targetWindowsPartitionRoot);

        Directory.CreateDirectory(registrationRoot);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(logRoot);

        StageEmbeddedResource(ScriptResourceName, scriptPath);
        StageEmbeddedResource(ServiceUiResourceName, serviceUiPath);
        File.WriteAllText(launcherPath, BuildLauncher(), Encoding.ASCII);
        File.WriteAllText(oobeLauncherPath, BuildOobeLauncher(), Encoding.ASCII);
        File.WriteAllText(oobeWaiterPath, BuildOobeWaiter(), Encoding.ASCII);
        File.WriteAllText(foregroundWrapperPath, BuildForegroundWrapper(), Encoding.ASCII);
        File.WriteAllText(configPath, BuildConfig(), Utf8NoBom);

        _setupCompleteScriptService.RemoveBlock(setupCompletePath, SetupCompleteMarkerKey);
        _setupCompleteScriptService.RemoveBlock(oobeCommandPath, SetupCompleteMarkerKey);
        _setupCompleteScriptService.EnsureBlock(
            oobeCommandPath,
            SetupCompleteMarkerKey,
            BuildOobeCommandLauncher());

        return new AutopilotInteractiveRegistrationProvisioningResult
        {
            RegistrationRootPath = registrationRoot,
            ScriptPath = scriptPath,
            LauncherPath = launcherPath,
            OobeLauncherPath = oobeLauncherPath,
            OobeWaiterPath = oobeWaiterPath,
            ForegroundWrapperPath = foregroundWrapperPath,
            ServiceUiPath = serviceUiPath,
            OobeCommandPath = oobeCommandPath,
            ConfigPath = configPath,
            StateRootPath = stateRoot,
            LogRootPath = logRoot
        };
    }

    private static string GetRegistrationRoot(string targetWindowsPartitionRoot)
    {
        return Path.Combine(targetWindowsPartitionRoot, "Windows", "Temp", "Foundry", "AutopilotRegistration");
    }

    private static string GetLogRoot(string targetWindowsPartitionRoot)
    {
        return Path.Combine(targetWindowsPartitionRoot, "Windows", "Temp", "Foundry", "Logs", "AutopilotRegistration");
    }

    private static string GetSetupCompletePath(string targetWindowsPartitionRoot)
    {
        return Path.Combine(targetWindowsPartitionRoot, "Windows", "Setup", "Scripts", "SetupComplete.cmd");
    }

    private static string GetOobeCommandPath(string targetWindowsPartitionRoot)
    {
        return Path.Combine(targetWindowsPartitionRoot, "Windows", "Setup", "Scripts", OobeCommandFileName);
    }

    private static void StageEmbeddedResource(string resourceName, string destinationPath)
    {
        Assembly assembly = typeof(AutopilotInteractiveRegistrationProvisioningService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded Autopilot registration resource '{resourceName}' was not found.");
        }

        using FileStream destination = File.Create(destinationPath);
        stream.CopyTo(destination);
    }

    private static string BuildLauncher()
    {
        return string.Join(
            Environment.NewLine,
            [
                "@echo off",
                "setlocal",
                $"set \"FOUNDRY_AUTOPILOT_LOG_ROOT={RuntimeLogRoot}\"",
                "mkdir \"%FOUNDRY_AUTOPILOT_LOG_ROOT%\" >nul 2>&1",
                "echo [%date% %time%] Starting Foundry Autopilot registration assistant.>>\"%FOUNDRY_AUTOPILOT_LOG_ROOT%\\launcher.log\"",
                $"powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File \"{RuntimeRegistrationRoot}\\{ScriptFileName}\" -ConfigPath \"{RuntimeRegistrationRoot}\\{ConfigFileName}\"",
                "set \"FOUNDRY_AUTOPILOT_EXIT=%ERRORLEVEL%\"",
                "echo [%date% %time%] Foundry Autopilot registration assistant exited with %FOUNDRY_AUTOPILOT_EXIT%.>>\"%FOUNDRY_AUTOPILOT_LOG_ROOT%\\launcher.log\"",
                "exit /b %FOUNDRY_AUTOPILOT_EXIT%",
                string.Empty
            ]);
    }

    private static string BuildOobeCommandLauncher()
    {
        return string.Join(
            Environment.NewLine,
            [
                $"mkdir \"{RuntimeLogRoot}\" >nul 2>&1",
                $"echo [%date% %time%] Calling Foundry Autopilot OOBE registration launcher.>>\"{RuntimeLogRoot}\\OOBE.log\"",
                $"call \"{RuntimeRegistrationRoot}\\{OobeLauncherFileName}\"",
                "set \"FOUNDRY_AUTOPILOT_OOBE_EXIT=%ERRORLEVEL%\"",
                $"echo [%date% %time%] Foundry Autopilot OOBE registration launcher exited with %FOUNDRY_AUTOPILOT_OOBE_EXIT%.>>\"{RuntimeLogRoot}\\OOBE.log\""
            ]);
    }

    private static string BuildOobeLauncher()
    {
        return string.Join(
            Environment.NewLine,
            [
                "@echo off",
                "setlocal EnableExtensions",
                $"set \"FOUNDRY_AUTOPILOT_REGISTRATION_ROOT={RuntimeRegistrationRoot}\"",
                $"set \"FOUNDRY_AUTOPILOT_LOG_ROOT={RuntimeLogRoot}\"",
                "set \"FOUNDRY_AUTOPILOT_SCRIPT=%FOUNDRY_AUTOPILOT_REGISTRATION_ROOT%\\Start-FoundryAutopilotRegistration.ps1\"",
                "set \"FOUNDRY_AUTOPILOT_CONFIG=%FOUNDRY_AUTOPILOT_REGISTRATION_ROOT%\\config.json\"",
                "set \"FOUNDRY_AUTOPILOT_WAITER=%FOUNDRY_AUTOPILOT_REGISTRATION_ROOT%\\Wait-FoundryAutopilotRegistrationOobe.ps1\"",
                "set \"FOUNDRY_AUTOPILOT_LOG=%FOUNDRY_AUTOPILOT_LOG_ROOT%\\oobe-launcher.log\"",
                "set \"FOUNDRY_AUTOPILOT_PS=%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\"",
                "mkdir \"%FOUNDRY_AUTOPILOT_LOG_ROOT%\" >nul 2>&1",
                "echo [%date% %time%] Starting Foundry Autopilot OOBE registration launcher.>>\"%FOUNDRY_AUTOPILOT_LOG%\"",
                "if not exist \"%FOUNDRY_AUTOPILOT_WAITER%\" (",
                "    echo [%date% %time%] OOBE waiter was not found: %FOUNDRY_AUTOPILOT_WAITER%.>>\"%FOUNDRY_AUTOPILOT_LOG%\"",
                "    exit /b 0",
                ")",
                "start \"\" \"%FOUNDRY_AUTOPILOT_PS%\" -NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"%FOUNDRY_AUTOPILOT_WAITER%\"",
                "echo [%date% %time%] Foundry Autopilot OOBE waiter started.>>\"%FOUNDRY_AUTOPILOT_LOG%\"",
                "exit /b 0",
                string.Empty
            ]);
    }

    private static string BuildOobeWaiter()
    {
        return string.Join(
            Environment.NewLine,
            [
                "$ErrorActionPreference = 'Stop'",
                "$registrationRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\AutopilotRegistration'",
                "$logRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\Logs\\AutopilotRegistration'",
                "$statePath = Join-Path $registrationRoot 'State\\registration-result.json'",
                "$registrationScriptPath = Join-Path $registrationRoot 'Start-FoundryAutopilotRegistration.ps1'",
                "$foregroundWrapperPath = Join-Path $registrationRoot 'Start-FoundryAutopilotRegistrationForeground.ps1'",
                "$configPath = Join-Path $registrationRoot 'config.json'",
                "$serviceUiPath = Join-Path $registrationRoot 'ServiceUI.exe'",
                "$powershellPath = Join-Path $env:SystemRoot 'System32\\WindowsPowerShell\\v1.0\\powershell.exe'",
                "$waitLogPath = Join-Path $logRoot 'oobe-waiter.log'",
                "$sessionDiagLogPath = Join-Path $logRoot 'oobe-sessiondiag.log'",
                "$diagnosticProcessNames = @('CloudExperienceHost', 'CloudExperienceHostBroker', 'UserOOBEBroker', 'oobenetworkconnectionflow', 'ApplicationFrameHost', 'ShellExperienceHost', 'RuntimeBroker', 'powershell', 'pwsh', 'cmd')",
                "$timeout = [DateTimeOffset]::UtcNow.AddMinutes(20)",
                "$stableSeconds = 5",
                "New-Item -Path $logRoot -ItemType Directory -Force | Out-Null",
                "Add-Type -TypeDefinition @'",
                "using System;",
                "using System.Runtime.InteropServices;",
                "public static class FoundryOobeNativeMethods",
                "{",
                "    [DllImport(\"kernel32.dll\")]",
                "    public static extern uint WTSGetActiveConsoleSessionId();",
                "}",
                "'@",
                "function Write-FoundryOobeWaiterLog {",
                "    param([Parameter(Mandatory = $true)][string]$Message)",
                "    $timestamp = [DateTimeOffset]::Now.ToString('o')",
                "    Add-Content -LiteralPath $waitLogPath -Value \"[$timestamp] $Message\"",
                "}",
                "function Get-FoundryActiveConsoleSessionId {",
                "    $sessionId = [FoundryOobeNativeMethods]::WTSGetActiveConsoleSessionId()",
                "    if ($sessionId -eq [uint32]::MaxValue) {",
                "        return $null",
                "    }",
                "    return [int]$sessionId",
                "}",
                "function Write-FoundryOobeSessionDiagnostics {",
                "    param(",
                "        [Parameter(Mandatory = $true)][string]$Stage,",
                "        [int]$ActiveSessionId = -1,",
                "        [int]$AssistantProcessId = 0",
                "    )",
                "    try {",
                "        Add-Content -LiteralPath $sessionDiagLogPath -Value \"==== $Stage $([DateTimeOffset]::Now.ToString('o')) ====\"",
                "        Add-Content -LiteralPath $sessionDiagLogPath -Value \"whoami: $(whoami)\"",
                "        Add-Content -LiteralPath $sessionDiagLogPath -Value \"waiter session: $([System.Diagnostics.Process]::GetCurrentProcess().SessionId)\"",
                "        Add-Content -LiteralPath $sessionDiagLogPath -Value \"active console session: $ActiveSessionId\"",
                "        Add-Content -LiteralPath $sessionDiagLogPath -Value 'query session:'",
                "        try {",
                "            Add-Content -LiteralPath $sessionDiagLogPath -Value (query session 2>&1 | Out-String)",
                "        }",
                "        catch {",
                "            Add-Content -LiteralPath $sessionDiagLogPath -Value \"query session failed: $($_.Exception.Message)\"",
                "        }",
                "        $processSnapshot = Get-Process -Name $diagnosticProcessNames -ErrorAction SilentlyContinue |",
                "            Select-Object Name, Id, SessionId, MainWindowHandle, MainWindowTitle, Path |",
                "            Sort-Object SessionId, Name |",
                "            Format-Table -AutoSize |",
                "            Out-String",
                "        Add-Content -LiteralPath $sessionDiagLogPath -Value $processSnapshot",
                "        if ($AssistantProcessId -gt 0) {",
                "            $assistantSnapshot = Get-Process -Id $AssistantProcessId -ErrorAction SilentlyContinue |",
                "                Select-Object Name, Id, SessionId, MainWindowHandle, MainWindowTitle, Path |",
                "                Format-List |",
                "                Out-String",
                "            Add-Content -LiteralPath $sessionDiagLogPath -Value 'assistant process:'",
                "            Add-Content -LiteralPath $sessionDiagLogPath -Value $assistantSnapshot",
                "        }",
                "    }",
                "    catch {",
                "        Write-FoundryOobeWaiterLog -Message \"Failed to write OOBE session diagnostics. $($_.Exception.Message)\"",
                "    }",
                "}",
                "try {",
                "function Test-FoundryRegistrationCompleted {",
                "    if (-not (Test-Path -LiteralPath $statePath)) {",
                "        return $false",
                "    }",
                "    try {",
                "        $result = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json",
                "        return $result.status -eq 'completed'",
                "    }",
                "    catch {",
                "        Write-FoundryOobeWaiterLog -Message \"Failed to read existing registration result. $($_.Exception.Message)\"",
                "        return $false",
                "    }",
                "}",
                "Write-FoundryOobeWaiterLog -Message 'Waiting for active OOBE console session.'",
                "$activeSessionId = $null",
                "while ([DateTimeOffset]::UtcNow -lt $timeout) {",
                "    if (Test-FoundryRegistrationCompleted) {",
                "        Write-FoundryOobeWaiterLog -Message 'Autopilot registration is already completed.'",
                "        exit 0",
                "    }",
                "    $activeSessionId = Get-FoundryActiveConsoleSessionId",
                "    if ($null -ne $activeSessionId) {",
                "        Write-FoundryOobeWaiterLog -Message \"Active console session $activeSessionId detected. Waiting $stableSeconds seconds before launching assistant.\"",
                "        Start-Sleep -Seconds $stableSeconds",
                "        break",
                "    }",
                "    Start-Sleep -Seconds 2",
                "}",
                "if ($null -eq $activeSessionId) {",
                "    Write-FoundryOobeWaiterLog -Message 'Timed out while waiting for active console session.'",
                "    exit 0",
                "}",
                "if (-not (Test-Path -LiteralPath $registrationScriptPath)) {",
                "    Write-FoundryOobeWaiterLog -Message \"Registration script was not found: $registrationScriptPath\"",
                "    exit 0",
                "}",
                "if (-not (Test-Path -LiteralPath $foregroundWrapperPath)) {",
                "    Write-FoundryOobeWaiterLog -Message \"Foreground wrapper was not found: $foregroundWrapperPath\"",
                "    exit 0",
                "}",
                "if (-not (Test-Path -LiteralPath $serviceUiPath)) {",
                "    Write-FoundryOobeWaiterLog -Message \"ServiceUI was not found: $serviceUiPath\"",
                "    exit 0",
                "}",
                "$assistantArguments = @(",
                "    '-NoLogo',",
                "    '-NoProfile',",
                "    '-ExecutionPolicy',",
                "    'Bypass',",
                "    '-STA',",
                "    '-WindowStyle',",
                "    'Hidden',",
                "    '-File',",
                "    $foregroundWrapperPath,",
                "    '-RegistrationScriptPath',",
                "    $registrationScriptPath,",
                "    '-ConfigPath',",
                "    $configPath",
                ")",
                "$serviceUiArguments = @(\"-session:$activeSessionId\", $powershellPath) + $assistantArguments",
                "Write-FoundryOobeWaiterLog -Message 'Launching Foundry Autopilot registration assistant.'",
                "Write-FoundryOobeSessionDiagnostics -Stage 'Before assistant launch' -ActiveSessionId $activeSessionId",
                "Write-FoundryOobeWaiterLog -Message \"Launching assistant through ServiceUI in active console session $activeSessionId.\"",
                "$process = Start-Process -FilePath $serviceUiPath -ArgumentList $serviceUiArguments -WindowStyle Hidden -PassThru",
                "Write-FoundryOobeWaiterLog -Message \"ServiceUI process started with PID $($process.Id).\"",
                "Write-FoundryOobeSessionDiagnostics -Stage 'After assistant launch' -ActiveSessionId $activeSessionId -AssistantProcessId $process.Id",
                "}",
                "catch {",
                "    try {",
                "        Write-FoundryOobeWaiterLog -Message \"OOBE waiter failed. $($_.Exception.Message)\"",
                "    }",
                "    catch {",
                "    }",
                "    exit 0",
                "}",
                string.Empty
            ]);
    }

    private static string BuildForegroundWrapper()
    {
        return string.Join(
            Environment.NewLine,
            [
                "param(",
                "    [Parameter(Mandatory = $true)][string]$RegistrationScriptPath,",
                "    [Parameter(Mandatory = $true)][string]$ConfigPath",
                ")",
                "$ErrorActionPreference = 'Stop'",
                "$logRoot = Join-Path $env:SystemRoot 'Temp\\Foundry\\Logs\\AutopilotRegistration'",
                "$foregroundLogPath = Join-Path $logRoot 'foreground.log'",
                "New-Item -Path $logRoot -ItemType Directory -Force | Out-Null",
                "function Write-FoundryForegroundLog {",
                "    param([Parameter(Mandatory = $true)][string]$Message)",
                "    $timestamp = [DateTimeOffset]::Now.ToString('o')",
                "    Add-Content -LiteralPath $foregroundLogPath -Value \"[$timestamp] $Message\"",
                "}",
                "Add-Type -TypeDefinition @'",
                "using System;",
                "using System.Runtime.InteropServices;",
                "public static class FoundryOobeForegroundNativeMethods",
                "{",
                "    private const int INPUT_KEYBOARD = 1;",
                "    private const uint KEYEVENTF_KEYUP = 0x0002;",
                "    private const ushort VK_SHIFT = 0x10;",
                "    private const ushort VK_F10 = 0x79;",
                string.Empty,
                "    [StructLayout(LayoutKind.Sequential)]",
                "    private struct INPUT",
                "    {",
                "        public int type;",
                "        public INPUTUNION u;",
                "    }",
                string.Empty,
                "    [StructLayout(LayoutKind.Explicit)]",
                "    private struct INPUTUNION",
                "    {",
                "        [FieldOffset(0)]",
                "        public MOUSEINPUT mi;",
                string.Empty,
                "        [FieldOffset(0)]",
                "        public KEYBDINPUT ki;",
                string.Empty,
                "        [FieldOffset(0)]",
                "        public HARDWAREINPUT hi;",
                "    }",
                string.Empty,
                "    [StructLayout(LayoutKind.Sequential)]",
                "    private struct MOUSEINPUT",
                "    {",
                "        public int dx;",
                "        public int dy;",
                "        public uint mouseData;",
                "        public uint dwFlags;",
                "        public uint time;",
                "        public IntPtr dwExtraInfo;",
                "    }",
                string.Empty,
                "    [StructLayout(LayoutKind.Sequential)]",
                "    private struct KEYBDINPUT",
                "    {",
                "        public ushort wVk;",
                "        public ushort wScan;",
                "        public uint dwFlags;",
                "        public uint time;",
                "        public IntPtr dwExtraInfo;",
                "    }",
                string.Empty,
                "    [StructLayout(LayoutKind.Sequential)]",
                "    private struct HARDWAREINPUT",
                "    {",
                "        public uint uMsg;",
                "        public ushort wParamL;",
                "        public ushort wParamH;",
                "    }",
                string.Empty,
                "    [DllImport(\"user32.dll\", SetLastError = true)]",
                "    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);",
                string.Empty,
                "    public static int LastWin32Error",
                "    {",
                "        get { return Marshal.GetLastWin32Error(); }",
                "    }",
                string.Empty,
                "    public static bool SendShiftF10()",
                "    {",
                "        INPUT[] inputs = new INPUT[]",
                "        {",
                "            CreateKeyInput(VK_SHIFT, 0),",
                "            CreateKeyInput(VK_F10, 0),",
                "            CreateKeyInput(VK_F10, KEYEVENTF_KEYUP),",
                "            CreateKeyInput(VK_SHIFT, KEYEVENTF_KEYUP)",
                "        };",
                string.Empty,
                "        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) == inputs.Length;",
                "    }",
                string.Empty,
                "    private static INPUT CreateKeyInput(ushort virtualKey, uint flags)",
                "    {",
                "        INPUT input = new INPUT();",
                "        input.type = INPUT_KEYBOARD;",
                "        input.u.ki.wVk = virtualKey;",
                "        input.u.ki.dwFlags = flags;",
                "        return input;",
                "    }",
                "}",
                "'@",
                "function Get-FoundryCurrentSessionId {",
                "    return [System.Diagnostics.Process]::GetCurrentProcess().SessionId",
                "}",
                "function Get-FoundryCommandPromptIds {",
                "    param([Parameter(Mandatory = $true)][int]$SessionId)",
                "    return @(Get-Process -Name cmd -ErrorAction SilentlyContinue |",
                "        Where-Object { $_.SessionId -eq $SessionId } |",
                "        Select-Object -ExpandProperty Id)",
                "}",
                "function Invoke-FoundryShiftF10 {",
                "    Write-FoundryForegroundLog -Message 'Sending Shift+F10 to unlock OOBE foreground input.'",
                "    if (-not [FoundryOobeForegroundNativeMethods]::SendShiftF10()) {",
                "        Write-FoundryForegroundLog -Message \"SendInput did not report all keyboard inputs as sent. LastWin32Error=$([FoundryOobeForegroundNativeMethods]::LastWin32Error)\"",
                "    }",
                "}",
                "function Wait-FoundryOobeCommandPrompt {",
                "    param(",
                "        [Parameter(Mandatory = $true)][int]$SessionId,",
                "        [int[]]$ExistingCommandPromptIds = @()",
                "    )",
                "    if ($null -eq $ExistingCommandPromptIds) {",
                "        $ExistingCommandPromptIds = @()",
                "    }",
                "    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)",
                "    while ([DateTimeOffset]::UtcNow -lt $deadline) {",
                "        $commandPrompt = Get-Process -Name cmd -ErrorAction SilentlyContinue |",
                "            Where-Object { $_.SessionId -eq $SessionId -and $_.Id -notin $ExistingCommandPromptIds } |",
                "            Sort-Object Id |",
                "            Select-Object -First 1",
                "        if ($commandPrompt) {",
                "            return $commandPrompt",
                "        }",
                "        Start-Sleep -Milliseconds 100",
                "    }",
                "    return $null",
                "}",
                "function Close-FoundryOobeCommandPrompt {",
                "    param([Parameter(Mandatory = $true)]$CommandPrompt)",
                "    Write-FoundryForegroundLog -Message \"Closing OOBE command prompt PID $($CommandPrompt.Id).\"",
                "    try {",
                "        if ($CommandPrompt.MainWindowHandle -ne 0) {",
                "            $CommandPrompt.CloseMainWindow() | Out-Null",
                "            Start-Sleep -Milliseconds 500",
                "        }",
                "        $refreshed = Get-Process -Id $CommandPrompt.Id -ErrorAction SilentlyContinue",
                "        if ($refreshed) {",
                "            Stop-Process -Id $CommandPrompt.Id -Force -ErrorAction SilentlyContinue",
                "        }",
                "    }",
                "    catch {",
                "        Write-FoundryForegroundLog -Message \"Failed to close OOBE command prompt. $($_.Exception.Message)\"",
                "    }",
                "}",
                "try {",
                "    Write-FoundryForegroundLog -Message 'Starting Foundry Autopilot foreground wrapper.'",
                "    $sessionId = Get-FoundryCurrentSessionId",
                "    [int[]]$existingCommandPromptIds = @(Get-FoundryCommandPromptIds -SessionId $sessionId)",
                "    Invoke-FoundryShiftF10",
                "    $commandPrompt = Wait-FoundryOobeCommandPrompt -SessionId $sessionId -ExistingCommandPromptIds @($existingCommandPromptIds)",
                "    if ($commandPrompt) {",
                "        Close-FoundryOobeCommandPrompt -CommandPrompt $commandPrompt",
                "    }",
                "    else {",
                "        Write-FoundryForegroundLog -Message 'No new OOBE command prompt was detected after Shift+F10.'",
                "    }",
                "    Write-FoundryForegroundLog -Message 'Starting Foundry Autopilot WPF assistant.'",
                "    & $RegistrationScriptPath -ConfigPath $ConfigPath",
                "    exit $LASTEXITCODE",
                "}",
                "catch {",
                "    Write-FoundryForegroundLog -Message \"Foreground wrapper failed. $($_.Exception.Message)\"",
                "    exit 1",
                "}",
                string.Empty
            ]);
    }

    private static string BuildConfig()
    {
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            provisioningMode = "interactiveHardwareHashUpload",
            tenant = "common",
            clientId = FoundryBootstrapClientId,
            graphBaseUri = "https://graph.microsoft.com/v1.0",
            scopes = new[]
            {
                "DeviceManagementServiceConfig.ReadWrite.All"
            },
            registrationRootPath = RuntimeRegistrationRoot,
            logRootPath = RuntimeLogRoot,
            stateRootPath = RuntimeStateRoot,
            importPollingTimeoutSeconds = 900,
            importPollingIntervalSeconds = 15
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return json + Environment.NewLine;
    }
}
