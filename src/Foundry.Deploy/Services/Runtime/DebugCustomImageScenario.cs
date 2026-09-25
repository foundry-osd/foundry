// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Runtime;

/// <summary>Identifies metadata-only previews of custom image selection and default-resolution states.</summary>
public enum DebugCustomImageScenario
{
    Configured,
    Disabled,
    CatalogDefault,
    SingleIndex,
    MultipleIndexes,
    PreferredIndex,
    MissingImage,
    MissingIndex,
    Empty,
    InvalidImage,
    AmbiguousDefault
}
