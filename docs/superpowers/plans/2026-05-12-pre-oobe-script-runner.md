# Pre-OOBE Script Runner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current direct `SetupComplete.cmd` driver commands with a minimal dynamic pre-OOBE PowerShell runner that supports ordered, conditional scripts.

**Architecture:** `SetupComplete.cmd` remains the Windows entry point, but it only launches one generated PowerShell runner. Foundry.Deploy stages script assets, writes a manifest, sorts enabled script definitions by priority and id, and generates the runner deterministically.

**Tech Stack:** .NET 10, WPF Foundry.Deploy runtime, xUnit, embedded resources, PowerShell, Windows `SetupComplete.cmd`.

---

## File Structure

- Create `src/Foundry.Deploy/Services/Deployment/PreOobe/PreOobeScriptPriority.cs`: priority constants for script ordering.
- Create `src/Foundry.Deploy/Services/Deployment/PreOobe/PreOobeScriptDefinition.cs`: immutable script registration model.
- Create `src/Foundry.Deploy/Services/Deployment/PreOobe/PreOobeScriptProvisioningResult.cs`: staged paths returned by provisioning.
- Create `src/Foundry.Deploy/Services/Deployment/PreOobe/IPreOobeScriptProvisioningService.cs`: service contract used by deployment steps.
- Create `src/Foundry.Deploy/Services/Deployment/PreOobe/PreOobeScriptProvisioningService.cs`: stages embedded scripts, writes manifest and runner, and ensures `SetupComplete.cmd`.
- Create `src/Foundry.Deploy/Assets/PreOobe/Install-DriverPack.ps1`: PowerShell-only deferred driver installation script.
- Modify `src/Foundry.Deploy/Foundry.Deploy.csproj`: embed the pre-OOBE script asset with an explicit logical resource name.
- Modify `src/Foundry.Deploy/DependencyInjection/ServiceCollectionExtensions.cs`: register `IPreOobeScriptProvisioningService`.
- Modify `src/Foundry.Deploy/Services/Deployment/Steps/ApplyDriverPackStep.cs`: replace direct batch body generation with pre-OOBE script registration.
- Modify `src/Foundry.Deploy/Services/Deployment/DeploymentRuntimeState.cs`: add pre-OOBE runner, manifest, and staged script path tracking.
- Modify `src/Foundry.Deploy/Services/Deployment/Steps/FinalizeDeploymentAndWriteLogsStep.cs`: include pre-OOBE paths in deployment summary.
- Add `src/Foundry.Deploy.Tests/PreOobeScriptProvisioningServiceTests.cs`: cover ordering, idempotency, manifest, runner, and `SetupComplete.cmd` output.
- In the separate Docusaurus documentation repo, create `E:/Github/Foundry Project/foundry-osd.github.io/docs/technical/deployment-orchestrator.md`: implementation-level deployment pipeline documentation.
- In the separate Docusaurus documentation repo, create `E:/Github/Foundry Project/foundry-osd.github.io/docs/technical/post-winpe-handoff.md`: post-WinPE and pre-OOBE handoff documentation.
- In the separate Docusaurus documentation repo, modify `E:/Github/Foundry Project/foundry-osd.github.io/sidebars.ts`: add a final top-level `Technical Deep Dives` category.

## Design Rules

- Script file names are semantic, not ordered: `Install-DriverPack.ps1`, `Apply-Customization.ps1`.
- Ordering is dynamic: sort by `Priority`, then by `Id` with `StringComparer.OrdinalIgnoreCase`.
- Duplicate script ids update the existing definition in memory before runner generation.
- `DriverProvisioning = 100` is P1 and must run before customization scripts.
- Optional customizations are registered only when Foundry.Deploy sees the corresponding Foundry OSD configuration option.
- `SetupComplete.cmd` contains one Foundry block and does not contain feature-specific logic.
- All called scripts are PowerShell.
- Technical documentation belongs in a separate last-position Docusaurus sidebar category named `Technical Deep Dives`, because it targets advanced readers rather than the common documentation path.

---

### Task 1: Add Pre-OOBE Models

**Files:**
- Create: `src/Foundry.Deploy/Services/Deployment/PreOobe/PreOobeScriptPriority.cs`
- Create: `src/Foundry.Deploy/Services/Deployment/PreOobe/PreOobeScriptDefinition.cs`
- Create: `src/Foundry.Deploy/Services/Deployment/PreOobe/PreOobeScriptProvisioningResult.cs`

- [ ] **Step 1: Create priority constants**

Add:

```csharp
namespace Foundry.Deploy.Services.Deployment.PreOobe;

public enum PreOobeScriptPriority
{
    DriverProvisioning = 100,
    Customization = 300,
    Validation = 800,
    Cleanup = 900
}
```

- [ ] **Step 2: Create script definition model**

Add:

```csharp
namespace Foundry.Deploy.Services.Deployment.PreOobe;

public sealed record PreOobeScriptDefinition
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string ResourceName { get; init; }
    public required PreOobeScriptPriority Priority { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
}
```

- [ ] **Step 3: Create provisioning result model**

Add:

```csharp
namespace Foundry.Deploy.Services.Deployment.PreOobe;

public sealed record PreOobeScriptProvisioningResult
{
    public required string SetupCompletePath { get; init; }
    public required string RunnerPath { get; init; }
    public required string ManifestPath { get; init; }
    public required IReadOnlyList<string> StagedScriptPaths { get; init; }
}
```

- [ ] **Step 4: Build**

Run:

```powershell
dotnet build src\Foundry.Deploy\Foundry.Deploy.csproj --no-restore
```

Expected: build succeeds.

---

### Task 2: Add Provisioning Service

**Files:**
- Create: `src/Foundry.Deploy/Services/Deployment/PreOobe/IPreOobeScriptProvisioningService.cs`
- Create: `src/Foundry.Deploy/Services/Deployment/PreOobe/PreOobeScriptProvisioningService.cs`
- Modify: `src/Foundry.Deploy/DependencyInjection/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Create service contract**

Add:

```csharp
namespace Foundry.Deploy.Services.Deployment.PreOobe;

public interface IPreOobeScriptProvisioningService
{
    PreOobeScriptProvisioningResult Provision(
        string targetWindowsPartitionRoot,
        IEnumerable<PreOobeScriptDefinition> scripts);
}
```

- [ ] **Step 2: Implement path layout**

Use these paths inside `PreOobeScriptProvisioningService`:

```csharp
private const string SetupCompleteMarkerKey = "FOUNDRY PRE-OOBE";
private const string RunnerFileName = "Invoke-FoundryPreOobe.ps1";
private const string ManifestFileName = "pre-oobe-manifest.json";

private static string GetPreOobeRoot(string targetWindowsPartitionRoot)
{
    return Path.Combine(targetWindowsPartitionRoot, "Windows", "Temp", "Foundry", "PreOobe");
}

private static string GetScriptsRoot(string targetWindowsPartitionRoot)
{
    return Path.Combine(GetPreOobeRoot(targetWindowsPartitionRoot), "Scripts");
}

private static string GetSetupCompletePath(string targetWindowsPartitionRoot)
{
    return Path.Combine(targetWindowsPartitionRoot, "Windows", "Setup", "Scripts", "SetupComplete.cmd");
}
```

- [ ] **Step 3: Implement deterministic script ordering**

Inside `Provision`, normalize and sort:

```csharp
PreOobeScriptDefinition[] orderedScripts = scripts
    .Where(script => !string.IsNullOrWhiteSpace(script.Id))
    .GroupBy(script => script.Id.Trim(), StringComparer.OrdinalIgnoreCase)
    .Select(group => group.Last())
    .OrderBy(script => script.Priority)
    .ThenBy(script => script.Id, StringComparer.OrdinalIgnoreCase)
    .ToArray();
```

If `orderedScripts.Length == 0`, throw `InvalidOperationException("At least one pre-OOBE script is required.")`.

- [ ] **Step 4: Stage embedded PowerShell scripts**

Use the current assembly:

```csharp
Assembly assembly = typeof(PreOobeScriptProvisioningService).Assembly;
using Stream? stream = assembly.GetManifestResourceStream(script.ResourceName);
```

Throw `InvalidOperationException($"Embedded pre-OOBE script resource '{script.ResourceName}' was not found.")` when missing.

Write each script to:

```csharp
Path.Combine(scriptsRoot, script.FileName)
```

- [ ] **Step 5: Generate runner**

Generate `Invoke-FoundryPreOobe.ps1` with this behavior:

```powershell
$ErrorActionPreference = 'Stop'
$preOobeRoot = Join-Path $env:SystemRoot 'Temp\Foundry\PreOobe'
$logRoot = Join-Path $env:SystemRoot 'Temp\Foundry\Logs\PreOobe'
New-Item -Path $logRoot -ItemType Directory -Force | Out-Null

function Invoke-FoundryScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptPath,
        [string[]]$Arguments = @()
    )

    $name = [System.IO.Path]::GetFileNameWithoutExtension($ScriptPath)
    $logPath = Join-Path $logRoot "$name.log"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments *> $logPath
    if ($LASTEXITCODE -ne 0) {
        throw "Pre-OOBE script '$ScriptPath' failed with exit code $LASTEXITCODE. See '$logPath'."
    }
}
```

Append one `Invoke-FoundryScript` call per ordered script, using runtime paths under:

```powershell
%SystemRoot%\Temp\Foundry\PreOobe\Scripts
```

- [ ] **Step 6: Generate manifest**

Write JSON to `pre-oobe-manifest.json` with:

```json
{
  "generatedAtUtc": "2026-05-12T00:00:00+00:00",
  "scripts": [
    {
      "id": "driver-pack",
      "fileName": "Install-DriverPack.ps1",
      "priority": 100,
      "arguments": []
    }
  ]
}
```

Use `DateTimeOffset.UtcNow` for the real timestamp.

- [ ] **Step 7: Generate stable `SetupComplete.cmd` block**

Use existing `ISetupCompleteScriptService.EnsureBlock` with marker key `FOUNDRY PRE-OOBE` and body:

```cmd
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SystemRoot%\Temp\Foundry\PreOobe\Invoke-FoundryPreOobe.ps1"
```

- [ ] **Step 8: Register the service**

Add to `ServiceCollectionExtensions`:

```csharp
services.AddSingleton<IPreOobeScriptProvisioningService, PreOobeScriptProvisioningService>();
```

- [ ] **Step 9: Build**

Run:

```powershell
dotnet build src\Foundry.Deploy\Foundry.Deploy.csproj --no-restore
```

Expected: build succeeds.

---

### Task 3: Add Driver PowerShell Asset

**Files:**
- Create: `src/Foundry.Deploy/Assets/PreOobe/Install-DriverPack.ps1`
- Modify: `src/Foundry.Deploy/Foundry.Deploy.csproj`

- [ ] **Step 1: Create driver script**

Add:

```powershell
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('LenovoExecutable', 'SurfaceMsi')]
    [string]$CommandKind,

    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -Path $PackagePath -PathType Leaf)) {
    throw "Driver package was not found: $PackagePath"
}

switch ($CommandKind) {
    'LenovoExecutable' {
        & $PackagePath /SILENT /SUPPRESSMSGBOXES
        if ($LASTEXITCODE -ne 0) {
            throw "Lenovo driver package failed with exit code $LASTEXITCODE."
        }

        reg add 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\UnattendSettings\PnPUnattend\DriverPaths\1' /v Path /t REG_SZ /d 'C:\Drivers' /f | Out-Null
        pnpunattend.exe AuditSystem /L
        if ($LASTEXITCODE -ne 0) {
            throw "pnpunattend.exe failed with exit code $LASTEXITCODE."
        }

        reg delete 'HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\UnattendSettings\PnPUnattend\DriverPaths\1' /v Path /f | Out-Null
        if (Test-Path -Path 'C:\Drivers') {
            Remove-Item -Path 'C:\Drivers' -Recurse -Force
        }
    }
    'SurfaceMsi' {
        $logPath = Join-Path $env:SystemRoot 'Temp\Foundry\DriverPack\surface-driverpack.log'
        & msiexec.exe /i $PackagePath /qn /norestart /l*v $logPath
        if ($LASTEXITCODE -ne 0) {
            throw "Surface driver package failed with exit code $LASTEXITCODE."
        }
    }
}

Remove-Item -Path $PackagePath -Force
```

- [ ] **Step 2: Embed the script**

Add to `Foundry.Deploy.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Assets\PreOobe\Install-DriverPack.ps1">
    <LogicalName>Foundry.Deploy.PreOobe.InstallDriverPack</LogicalName>
  </EmbeddedResource>
</ItemGroup>
```

- [ ] **Step 3: Build**

Run:

```powershell
dotnet build src\Foundry.Deploy\Foundry.Deploy.csproj --no-restore
```

Expected: build succeeds.

---

### Task 4: Migrate Deferred Driver Hook

**Files:**
- Modify: `src/Foundry.Deploy/Services/Deployment/Steps/ApplyDriverPackStep.cs`
- Modify: `src/Foundry.Deploy/Services/Deployment/DeploymentRuntimeState.cs`
- Modify: `src/Foundry.Deploy/Services/Deployment/Steps/FinalizeDeploymentAndWriteLogsStep.cs`

- [ ] **Step 1: Inject the new provisioning service**

Change `ApplyDriverPackStep` constructor to accept:

```csharp
IPreOobeScriptProvisioningService preOobeScriptProvisioningService
```

Store it in a private field.

- [ ] **Step 2: Replace `BuildDeferredScript` usage**

In `ApplyDeferredAsync`, after copying the driver package, replace:

```csharp
string scriptBody = BuildDeferredScript(runtimePackagePath, executionPlan.DeferredCommandKind);
_setupCompleteScriptService.EnsureBlock(setupCompletePath, "FOUNDRY DRIVERPACK", scriptBody);
```

with:

```csharp
PreOobeScriptProvisioningResult preOobeResult = _preOobeScriptProvisioningService.Provision(
    context.RuntimeState.TargetWindowsPartitionRoot,
    [
        new PreOobeScriptDefinition
        {
            Id = "driver-pack",
            FileName = "Install-DriverPack.ps1",
            ResourceName = "Foundry.Deploy.PreOobe.InstallDriverPack",
            Priority = PreOobeScriptPriority.DriverProvisioning,
            Arguments =
            [
                "-CommandKind",
                executionPlan.DeferredCommandKind.ToString(),
                "-PackagePath",
                runtimePackagePath
            ]
        }
    ]);
```

- [ ] **Step 3: Track pre-OOBE runtime state**

Add to `DeploymentRuntimeState`:

```csharp
public string? PreOobeSetupCompletePath { get; set; }
public string? PreOobeRunnerPath { get; set; }
public string? PreOobeManifestPath { get; set; }
public IReadOnlyList<string> PreOobeScriptPaths { get; set; } = [];
```

Set these fields from `preOobeResult` in `ApplyDeferredAsync`.

- [ ] **Step 4: Preserve existing summary compatibility**

Keep setting:

```csharp
context.RuntimeState.DriverPackSetupCompleteHookPath = preOobeResult.SetupCompletePath;
```

- [ ] **Step 5: Remove obsolete batch builder**

Delete `BuildDeferredScript` and `EscapeForBatch` from `ApplyDriverPackStep` after the PowerShell migration compiles.

- [ ] **Step 6: Add pre-OOBE fields to deployment summary**

Add these properties in `FinalizeDeploymentAndWriteLogsStep.WriteDeploymentSummaryAsync`:

```csharp
preOobeSetupCompletePath = runtimeState.PreOobeSetupCompletePath,
preOobeRunnerPath = runtimeState.PreOobeRunnerPath,
preOobeManifestPath = runtimeState.PreOobeManifestPath,
preOobeScriptPaths = runtimeState.PreOobeScriptPaths,
```

- [ ] **Step 7: Build**

Run:

```powershell
dotnet build src\Foundry.Deploy\Foundry.Deploy.csproj --no-restore
```

Expected: build succeeds.

---

### Task 5: Add Unit Tests

**Files:**
- Create: `src/Foundry.Deploy.Tests/PreOobeScriptProvisioningServiceTests.cs`

- [ ] **Step 1: Test priority and id ordering**

Create a test that provisions three scripts with priorities `Customization`, `DriverProvisioning`, and `Cleanup`. Assert runner content places driver first, customization second, cleanup third.

- [ ] **Step 2: Test same-priority id ordering**

Create two customization scripts with ids `configure-start-menu` and `apply-branding`. Assert runner content places `apply-branding` before `configure-start-menu`.

- [ ] **Step 3: Test duplicate id replacement**

Create two definitions with id `apply-branding`, different arguments, and the same file name/resource. Assert manifest contains one `apply-branding` entry with the later arguments.

- [ ] **Step 4: Test `SetupComplete.cmd` content**

Assert generated `SetupComplete.cmd` contains one `FOUNDRY PRE-OOBE` block and launches:

```cmd
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SystemRoot%\Temp\Foundry\PreOobe\Invoke-FoundryPreOobe.ps1"
```

- [ ] **Step 5: Test idempotency**

Call `Provision` twice with the same script definitions. Assert `SetupComplete.cmd` still contains one `FOUNDRY PRE-OOBE BEGIN` marker.

- [ ] **Step 6: Run tests**

Run:

```powershell
dotnet test src\Foundry.Deploy.Tests\Foundry.Deploy.Tests.csproj --no-restore
```

Expected: all Foundry.Deploy tests pass.

---

### Task 6: Document Technical Deep Dives

**Files:**
- Create: `E:/Github/Foundry Project/foundry-osd.github.io/docs/technical/deployment-orchestrator.md`
- Create: `E:/Github/Foundry Project/foundry-osd.github.io/docs/technical/post-winpe-handoff.md`
- Modify: `E:/Github/Foundry Project/foundry-osd.github.io/sidebars.ts`

- [ ] **Step 1: Create the technical docs directory**

Run:

```powershell
New-Item -ItemType Directory -Force -Path "E:\Github\Foundry Project\foundry-osd.github.io\docs\technical"
```

Expected: `docs\technical` exists in the Docusaurus repo.

- [ ] **Step 2: Add deployment orchestrator documentation**

Create `docs/technical/deployment-orchestrator.md` with:

```md
---
title: Deployment Orchestrator
description: Understand how Foundry Deploy orders and executes deployment steps inside WinPE.
---

# Deployment orchestrator

This page is a technical deep dive. It describes the internal Foundry Deploy execution pipeline for maintainers and advanced readers.

Foundry Deploy runs in WinPE after Foundry Connect completes successfully. Deployment execution is handled by ordered `IDeploymentStep` instances. The orchestrator sorts steps by `IDeploymentStep.Order`, verifies that the registered step names match the expected workflow, and executes each step against a shared runtime state.

## Ordered steps

The deployment pipeline runs in this order:

1. Gather deployment variables
2. Initialize deployment workspace
3. Validate target configuration
4. Resolve cache strategy
5. Prepare target disk layout
6. Download operating system image
7. Apply operating system image
8. Configure target computer name and time zone through `Windows\Panther\unattend.xml`
9. Configure recovery environment
10. Download driver pack
11. Extract driver pack
12. Apply driver pack or stage deferred first-boot execution
13. Download firmware update
14. Apply firmware update
15. Seal recovery partition
16. Stage Autopilot configuration
17. Finalize deployment and write logs

## Runtime state

Each step reads and updates the deployment runtime state. The runtime state carries target partition paths, selected operating system metadata, driver-pack strategy, firmware update paths, Autopilot staging details, pre-OOBE script paths, completed steps, and final artifact locations.

## Offline Windows staging

Foundry Deploy writes several artifacts into the applied Windows image before reboot:

- `Windows\Panther\unattend.xml` for specialize-pass settings such as computer name and time zone.
- `Windows\Setup\Scripts\SetupComplete.cmd` for post-WinPE first-boot execution.
- `Windows\Provisioning\Autopilot\AutopilotConfigurationFile.json` when offline Autopilot profile staging is enabled.
- `Windows\Temp\Foundry` for deployment logs, summaries, staged packages, and pre-OOBE assets.

## Failure behavior

If a step fails, orchestration stops, the failure is logged, and Foundry Deploy reports the failed step to the operator. Logs are rebound to the final Windows target location when the Windows partition is available.
```

- [ ] **Step 3: Add post-WinPE handoff documentation**

Create `docs/technical/post-winpe-handoff.md` with:

```md
---
title: Post-WinPE Handoff
description: Understand what Foundry stages before reboot and what Windows executes before OOBE.
---

# Post-WinPE handoff

This page is a technical deep dive. It explains what happens after Foundry Deploy finishes in WinPE and the target device boots into the applied Windows image.

## Handoff boundary

Foundry Deploy runs in WinPE. Before reboot, it applies Windows, configures boot files, writes offline configuration artifacts, and stages any first-boot assets required by the selected deployment options.

After reboot, Windows starts from the applied image. Foundry Deploy is no longer running. Windows setup consumes the files that were staged while the image was offline.

## Specialize pass

Computer name and time zone are written to:

`Windows\Panther\unattend.xml`

Windows setup applies those values during the specialize pass before the user reaches OOBE.

## SetupComplete

Deferred first-boot execution is staged through:

`Windows\Setup\Scripts\SetupComplete.cmd`

`SetupComplete.cmd` runs after Windows setup completes and before the user reaches the desktop. Foundry uses it only as a stable launcher for post-WinPE work that cannot be completed offline.

## Pre-OOBE PowerShell runner

The pre-OOBE runner model keeps `SetupComplete.cmd` small. `SetupComplete.cmd` launches one generated PowerShell runner under:

`Windows\Temp\Foundry\PreOobe`

The runner executes enabled PowerShell scripts in deterministic order:

1. Script priority
2. Script id

Driver provisioning is priority `100` and runs before customization scripts. Customization scripts are registered only when the corresponding Foundry OSD or Foundry Deploy configuration enables them.

## Deferred driver provisioning

Most driver packs are applied offline with DISM. Some packages, such as selected executable or MSI packages, must run after Windows boots. In those cases, Foundry stages the package under:

`Windows\Temp\Foundry\DriverPack\Packages`

The pre-OOBE runner then invokes the driver PowerShell script during first boot.

## Operational artifacts

Foundry stores logs, deployment summaries, staged packages, and pre-OOBE manifests under:

`Windows\Temp\Foundry`

These files exist to make the deployment handoff auditable after WinPE exits.
```

- [ ] **Step 4: Add the final sidebar category**

Modify `sidebars.ts` and append this category after `Developer`:

```ts
{
  type: 'category',
  label: 'Technical Deep Dives',
  description:
    'Implementation-level details for Foundry runtime behavior, orchestration, and handoff internals.',
  link: {
    type: 'generated-index',
    title: 'Technical Deep Dives',
    slug: '/technical',
    description:
      'Implementation-level details for Foundry runtime behavior, orchestration, and handoff internals.',
  },
  items: [
    'technical/deployment-orchestrator',
    'technical/post-winpe-handoff',
  ],
},
```

Expected: `Technical Deep Dives` is the last top-level sidebar category.

- [ ] **Step 5: Verify Docusaurus builds**

Run:

```powershell
npm run build
```

from:

```text
E:\Github\Foundry Project\foundry-osd.github.io
```

Expected: Docusaurus build succeeds.

- [ ] **Step 6: Commit documentation changes**

Run from `E:\Github\Foundry Project\foundry-osd.github.io`:

```powershell
git status --short
git add docs\technical sidebars.ts
git commit -m "docs: add technical deep dives"
```

Expected: one focused documentation commit in the Docusaurus repo.

---

### Task 7: Verify Full Solution

**Files:**
- No source changes.

- [ ] **Step 1: Run focused tests**

Run:

```powershell
dotnet test src\Foundry.Deploy.Tests\Foundry.Deploy.Tests.csproj --no-restore
```

Expected: all Foundry.Deploy tests pass.

- [ ] **Step 2: Run solution tests**

Run:

```powershell
dotnet test src\Foundry.slnx --no-restore
```

Expected: all tests pass.

- [ ] **Step 3: Review generated behavior manually**

Confirm the new implementation generates:

```text
Windows\Setup\Scripts\SetupComplete.cmd
Windows\Temp\Foundry\PreOobe\Invoke-FoundryPreOobe.ps1
Windows\Temp\Foundry\PreOobe\pre-oobe-manifest.json
Windows\Temp\Foundry\PreOobe\Scripts\Install-DriverPack.ps1
```

- [ ] **Step 4: Commit**

Run:

```powershell
git status --short
git add src\Foundry.Deploy src\Foundry.Deploy.Tests
git commit -m "feat(deploy): add pre-oobe script runner"
```

Expected: one focused implementation commit.

## Self-Review

- Spec coverage: dynamic priority ordering, same-priority tie-breaking, conditional registration, PowerShell-only scripts, `SetupComplete.cmd` launcher behavior, and Docusaurus technical deep-dive documentation are covered.
- Completion scan: no unresolved planning markers remain.
- Type consistency: `PreOobeScriptDefinition`, `PreOobeScriptPriority`, `PreOobeScriptProvisioningResult`, and `IPreOobeScriptProvisioningService` names are consistent across tasks.
