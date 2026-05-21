using System.Globalization;
using Foundry.Core.Services.Autopilot;
using Microsoft.UI.Xaml.Media;

namespace Foundry.ViewModels;

/// <summary>
/// Represents an app registration certificate credential displayed in the Autopilot page.
/// </summary>
public sealed record AutopilotCertificateEntryViewModel(
    string KeyId,
    string Thumbprint,
    DateTimeOffset StartsOnUtc,
    DateTimeOffset ExpiresOnUtc)
{
    private static readonly TimeSpan ExpirationWarningThreshold = TimeSpan.FromDays(30);

    public string StartsOnDisplay => StartsOnUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string ExpiresOnDisplay => ExpiresOnUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public Brush ValidityForeground => (Brush)Application.Current.Resources[ResolveValidityBrushKey()];

    public static AutopilotCertificateEntryViewModel FromGraphCredential(AutopilotGraphKeyCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return new AutopilotCertificateEntryViewModel(
            credential.KeyId,
            credential.Thumbprint,
            credential.StartsOnUtc,
            credential.ExpiresOnUtc);
    }

    private string ResolveValidityBrushKey()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (ExpiresOnUtc <= now)
        {
            return "SystemFillColorCriticalBrush";
        }

        return ExpiresOnUtc - now <= ExpirationWarningThreshold
            ? "SystemFillColorCautionBrush"
            : "SystemFillColorSuccessBrush";
    }
}
