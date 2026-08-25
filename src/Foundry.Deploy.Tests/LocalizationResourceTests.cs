// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.Tests;

public sealed class LocalizationResourceTests
{
    private static readonly string[] DeploymentAccessResourceKeys =
    [
        "Common.Cancel",
        "DeploymentAccess.Title",
        "DeploymentAccess.Heading",
        "DeploymentAccess.Description",
        "DeploymentAccess.PasswordPlaceholder",
        "DeploymentAccess.Continue",
        "DeploymentAccess.TogglePasswordVisibility",
        "DeploymentAccess.InvalidPassword"
    ];

    private static readonly string[] RedesignedInterfaceResourceKeys =
    [
        "Wizard.Step.TargetDevice",
        "Wizard.Step.OperatingSystem",
        "Wizard.Step.Drivers",
        "Wizard.Step.Autopilot",
        "Wizard.Step.Summary",
        "Wizard.StepperAutomationName",
        "Wizard.ReturnToSummary",
        "Common.Edit",
        "Common.Copy",
        "Common.Close",
        "TargetDevice.Title",
        "TargetDevice.Description",
        "TargetDevice.DeploymentSettings",
        "TargetDevice.DeviceInventory",
        "TargetDevice.DeviceIdentity",
        "TargetDevice.Platform",
        "TargetDevice.Manufacturer",
        "TargetDevice.Model",
        "TargetDevice.Product",
        "TargetDevice.SerialNumber",
        "TargetDevice.Architecture",
        "TargetDevice.Tpm",
        "TargetDevice.PowerSource",
        "TargetDevice.FirmwareStatus",
        "TargetDevice.DiskEraseNotice",
        "TargetDevice.Firmware",
        "TargetDevice.FirmwareUnavailableVirtualMachine",
        "OperatingSystem.Title",
        "OperatingSystem.Description",
        "Drivers.Title",
        "Drivers.Description",
        "Autopilot.Title",
        "Autopilot.Description",
        "Autopilot.JsonProfileMethodDescription",
        "Autopilot.HardwareHashUploadMethodDescription",
        "Autopilot.ConfigurationDetails",
        "Summary.Category.TargetDevice",
        "Summary.Category.OperatingSystem",
        "Summary.Category.Drivers",
        "Summary.Category.Autopilot",
        "Summary.Category.WindowsCustomization",
        "Summary.Category.Network",
        "Summary.Category.Completion",
        "Summary.Description",
        "Summary.Status.NotConfigured",
        "Summary.Status.Configured",
        "Summary.Status.NoChanges",
        "Summary.Hardware",
        "Summary.Release",
        "Summary.Edition",
        "Summary.Architecture",
        "Summary.Language",
        "Summary.LicenseChannel",
        "Summary.Build",
        "Summary.Oobe",
        "Summary.AppxRemoval",
        "Summary.AiComponentRemoval",
        "Summary.NetworkProfileRoaming",
        "Summary.AutomaticRestart",
        "Summary.ManualRestart",
        "Summary.RestartDelay",
        "Summary.SecondsFormat",
        "Splash.WelcomeDeploy",
        "Progress.Session",
        "Progress.Timeline",
        "Progress.RingAutomationName",
        "Progress.TimelineAutomationName",
        "Error.FailedStepFormat",
        "Error.ViewTechnicalDetails",
        "Error.TechnicalDetailsTitle"
    ];

    public static TheoryData<string> SatelliteCultures => new()
    {
        "ar-SA",
        "bg-BG",
        "cs-CZ",
        "da-DK",
        "de-DE",
        "el-GR",
        "en-GB",
        "es-ES",
        "es-MX",
        "et-EE",
        "fi-FI",
        "fr-CA",
        "fr-FR",
        "he-IL",
        "hr-HR",
        "hu-HU",
        "it-IT",
        "ja-JP",
        "ko-KR",
        "lt-LT",
        "lv-LV",
        "nb-NO",
        "nl-NL",
        "pl-PL",
        "pt-BR",
        "pt-PT",
        "ro-RO",
        "ru-RU",
        "sk-SK",
        "sl-SI",
        "sr-Latn-RS",
        "sv-SE",
        "th-TH",
        "tr-TR",
        "uk-UA",
        "zh-CN",
        "zh-TW"
    };

    [Theory]
    [MemberData(nameof(SatelliteCultures))]
    public void SatelliteResourceSet_IsAvailableForSupportedCulture(string cultureName)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);

        ResourceSet? resourceSet = LocalizationText.ResourceManager.GetResourceSet(
            culture,
            createIfNotExists: true,
            tryParents: false);

        Assert.NotNull(resourceSet);
        Assert.Equal("Foundry Deploy", resourceSet.GetString("App.Name"));
        foreach (string key in DeploymentAccessResourceKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(resourceSet.GetString(key)), $"Resource '{key}' is missing for '{cultureName}'.");
        }

        foreach (string key in RedesignedInterfaceResourceKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(resourceSet.GetString(key)), $"Resource '{key}' is missing for '{cultureName}'.");
        }

        Assert.Contains("{0}", resourceSet.GetString("Error.FailedStepFormat"));
    }

    [Fact]
    public void ReferenceResources_UseApprovedTerminalStateCopy()
    {
        ResourceSet? resourceSet = LocalizationText.ResourceManager.GetResourceSet(
            CultureInfo.GetCultureInfo("en-US"),
            createIfNotExists: true,
            tryParents: false);

        Assert.NotNull(resourceSet);
        Assert.Equal("Deployment complete", resourceSet.GetString("Success.Completed"));
        Assert.Equal("Failed step: {0}", resourceSet.GetString("Error.FailedStepFormat"));
        Assert.Equal("View error details", resourceSet.GetString("Error.ViewTechnicalDetails"));
        Assert.Equal("Error details", resourceSet.GetString("Error.TechnicalDetailsTitle"));
    }
}
