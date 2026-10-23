// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Host paths read from the legacy path members of an authoring document before typed deserialization.
/// </summary>
/// <param name="Dot1xProfileTemplate">The former <c>network.dot1x.profileTemplatePath</c>.</param>
/// <param name="Dot1xCertificate">The former <c>network.dot1x.certificatePath</c>.</param>
/// <param name="WifiEnterpriseProfileTemplate">The former <c>network.wifi.enterpriseProfileTemplatePath</c>.</param>
/// <param name="WifiCertificate">The former <c>network.wifi.certificatePath</c>.</param>
/// <param name="AutopilotPfx">The former <c>autopilot.hardwareHashUpload.bootMediaCertificate.pfxPath</c>.</param>
/// <param name="UnattendSourcePaths">The former <c>unattend.files[].sourcePath</c> values, keyed by answer-file id.</param>
public sealed record LegacyFilePaths(
    string? Dot1xProfileTemplate,
    string? Dot1xCertificate,
    string? WifiEnterpriseProfileTemplate,
    string? WifiCertificate,
    string? AutopilotPfx,
    IReadOnlyDictionary<string, string> UnattendSourcePaths);
