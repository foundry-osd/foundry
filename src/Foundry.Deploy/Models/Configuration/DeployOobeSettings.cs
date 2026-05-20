namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// Describes Windows OOBE customization consumed by Foundry.Deploy.
/// </summary>
public sealed record DeployOobeSettings
{
    /// <summary>
    /// Gets whether Foundry.Deploy should apply OOBE customization to the offline Windows installation.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets whether the Microsoft Software License Terms page should be skipped during OOBE.
    /// </summary>
    public bool SkipLicenseTerms { get; init; } = true;

    /// <summary>
    /// Gets the Windows diagnostic data level requested for the deployed installation.
    /// </summary>
    public DeployOobeDiagnosticDataLevel DiagnosticDataLevel { get; init; } = DeployOobeDiagnosticDataLevel.Required;

    /// <summary>
    /// Gets whether Windows should suppress the privacy choices page during first sign-in.
    /// </summary>
    public bool HidePrivacySetup { get; init; } = true;

    /// <summary>
    /// Gets whether Windows may use diagnostic data for personalized tips, ads, and recommendations.
    /// </summary>
    public bool AllowTailoredExperiences { get; init; }

    /// <summary>
    /// Gets whether apps may use the Windows advertising ID across apps.
    /// </summary>
    public bool AllowAdvertisingId { get; init; }

    /// <summary>
    /// Gets whether Microsoft cloud-based speech recognition services are allowed.
    /// </summary>
    public bool AllowOnlineSpeechRecognition { get; init; }

    /// <summary>
    /// Gets whether optional inking and typing diagnostic data collection is allowed.
    /// </summary>
    public bool AllowInkingAndTypingDiagnostics { get; init; }

    /// <summary>
    /// Gets how Windows location access should be configured before first sign-in.
    /// </summary>
    public DeployOobeLocationAccessMode LocationAccess { get; init; } = DeployOobeLocationAccessMode.UserControlled;
}
