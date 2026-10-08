// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

/// <summary>Classifies raw Windows edition IDs without expanding image support or blocking unknown editions.</summary>
public static class DomainJoinEditionRules
{
    /// <summary>Defers missing or unfamiliar IDs to image inspection and native capability reporting.</summary>
    public static DomainJoinEditionSupport Evaluate(string? editionId) => editionId?.Trim().ToUpperInvariant() switch
    {
        "CORE" or "COREN" or "CORESINGLELANGUAGE" or "CORECOUNTRYSPECIFIC" => DomainJoinEditionSupport.Unsupported,
        "PROFESSIONAL" or "PROFESSIONALN" or "EDUCATION" or "EDUCATIONN" or "ENTERPRISE" or "ENTERPRISEN" => DomainJoinEditionSupport.Supported,
        _ => DomainJoinEditionSupport.Unknown
    };
}

/// <summary>Separates positively identified client capabilities from deferred inspection.</summary>
public enum DomainJoinEditionSupport { Supported, Unsupported, Unknown }
