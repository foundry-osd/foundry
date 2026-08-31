// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Diagnostics;

/// <summary>
/// Defines whether a support bundle applies privacy redaction or preserves source logs verbatim.
/// </summary>
public enum SupportBundlePrivacyMode
{
    Sanitized,
    Raw
}
