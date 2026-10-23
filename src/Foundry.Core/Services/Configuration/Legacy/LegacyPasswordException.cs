// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration.Legacy;

/// <summary>The passphrase does not open the legacy package.</summary>
public sealed class LegacyPasswordException() : Exception("The passphrase does not open this legacy profile.");
