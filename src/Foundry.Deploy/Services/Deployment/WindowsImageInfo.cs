// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

public sealed record WindowsImageInfo(int Index, string EditionId, string Architecture,
    Version Version, string DefaultLanguage, long ExpandedSizeBytes);
