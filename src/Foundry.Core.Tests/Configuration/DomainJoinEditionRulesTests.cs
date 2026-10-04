// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DomainJoinEditionRulesTests
{
    [Theory]
    [InlineData("Core", DomainJoinEditionSupport.Unsupported)]
    [InlineData("CoreN", DomainJoinEditionSupport.Unsupported)]
    [InlineData("CoreSingleLanguage", DomainJoinEditionSupport.Unsupported)]
    [InlineData("CoreCountrySpecific", DomainJoinEditionSupport.Unsupported)]
    [InlineData("Professional", DomainJoinEditionSupport.Supported)]
    [InlineData("ProfessionalN", DomainJoinEditionSupport.Supported)]
    [InlineData("Education", DomainJoinEditionSupport.Supported)]
    [InlineData("EducationN", DomainJoinEditionSupport.Supported)]
    [InlineData("Enterprise", DomainJoinEditionSupport.Supported)]
    [InlineData("EnterpriseN", DomainJoinEditionSupport.Supported)]
    [InlineData("enterprise", DomainJoinEditionSupport.Supported)]
    [InlineData(null, DomainJoinEditionSupport.Unknown)]
    [InlineData("", DomainJoinEditionSupport.Unknown)]
    [InlineData("ServerStandard", DomainJoinEditionSupport.Unknown)]
    [InlineData("Home", DomainJoinEditionSupport.Unknown)]
    public void Evaluate_DefersUnknownIds(string? edition, DomainJoinEditionSupport expected) =>
        Assert.Equal(expected, DomainJoinEditionRules.Evaluate(edition));

    [Theory]
    [InlineData("Home", DomainJoinEditionSupport.Unsupported)]
    [InlineData("Home N", DomainJoinEditionSupport.Unsupported)]
    [InlineData("Home Single Language", DomainJoinEditionSupport.Unsupported)]
    [InlineData("Home China", DomainJoinEditionSupport.Unsupported)]
    [InlineData("Pro", DomainJoinEditionSupport.Supported)]
    [InlineData("Pro N", DomainJoinEditionSupport.Supported)]
    public void CatalogLabels_AreResolvedBeforeEditionPolicy(string label, DomainJoinEditionSupport expected) =>
        Assert.Equal(expected, DomainJoinEditionRules.Evaluate(WindowsEditionCatalog.Find(label)?.EditionId));
}
