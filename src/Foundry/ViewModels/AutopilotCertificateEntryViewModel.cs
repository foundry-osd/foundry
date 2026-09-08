// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Services.Autopilot;

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

    public Style ValidityStyle => (Style)Application.Current.Resources[ResolveValidityStyleKey()];

    public static AutopilotCertificateEntryViewModel FromGraphCredential(AutopilotGraphKeyCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return new AutopilotCertificateEntryViewModel(
            credential.KeyId,
            credential.Thumbprint,
            credential.StartsOnUtc,
            credential.ExpiresOnUtc);
    }

    private string ResolveValidityStyleKey()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (ExpiresOnUtc <= now)
        {
            return "FoundryCriticalTextBlockStyle";
        }

        return ExpiresOnUtc - now <= ExpirationWarningThreshold
            ? "FoundryCautionTextBlockStyle"
            : "FoundrySuccessTextBlockStyle";
    }
}
