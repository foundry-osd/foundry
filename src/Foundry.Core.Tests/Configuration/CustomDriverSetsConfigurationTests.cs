// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class CustomDriverSetsConfigurationTests
{
    [Fact]
    public void ValidatorRejectsDefaultThatIsNotIncluded()
    {
        CustomDriverSetsSettings settings = Enabled([Set("a", 'a'), Set("b", 'b', included: false)]) with { DefaultSetId = "b" };

        Assert.NotEmpty(CustomDriverSetsSettingsValidator.Validate(settings));
        Assert.Empty(CustomDriverSetsSettingsValidator.Validate(settings with { DefaultSetId = "a" }));
        Assert.Throws<InvalidDataException>(() => CustomDriverSetsSettingsValidator.ThrowIfInvalid(settings));
    }

    [Fact]
    public void ValidatorRejectsDuplicateIdsAndDuplicateIncludedHashes()
    {
        Assert.NotEmpty(CustomDriverSetsSettingsValidator.Validate(Enabled([Set("a", 'a'), Set("a", 'b')])));
        Assert.NotEmpty(CustomDriverSetsSettingsValidator.Validate(Enabled([Set("a", 'a'), Set("b", 'a')])));
        Assert.Empty(CustomDriverSetsSettingsValidator.Validate(Enabled([Set("a", 'a'), Set("b", 'a', included: false)])));
    }

    [Fact]
    public void ValidatorRejects257Sets()
    {
        CustomDriverSetReference[] sets = Enumerable.Range(0, 257).Select(index => Set(index.ToString("x32"), 'a', hash: index.ToString("x64"))).ToArray();

        Assert.NotEmpty(CustomDriverSetsSettingsValidator.Validate(Enabled(sets)));
        Assert.Empty(CustomDriverSetsSettingsValidator.Validate(Enabled(sets.Take(256).ToArray())));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidatorRejectsEmptyNames(string name) =>
        Assert.NotEmpty(CustomDriverSetsSettingsValidator.Validate(Enabled([Set("a", 'a') with { DisplayName = name }])));

    [Fact]
    public void ValidatorRejects201CharacterNames()
    {
        Assert.NotEmpty(CustomDriverSetsSettingsValidator.Validate(Enabled([Set("a", 'a') with { DisplayName = new string('x', 201) }])));
        Assert.Empty(CustomDriverSetsSettingsValidator.Validate(Enabled([Set("a", 'a') with { DisplayName = new string('x', 200) }])));
    }

    [Fact]
    public void ValidatorUsesDriverPackageLimits()
    {
        CustomDriverSetReference large = Set("a", 'a');
        large = large with { Package = large.Package with { FileCount = 20_000 } };
        Assert.Empty(CustomDriverSetsSettingsValidator.Validate(Enabled([large])));
        Assert.NotEmpty(CustomDriverSetsSettingsValidator.Validate(Enabled([large with { Package = large.Package with { FileCount = 20_001 } }])));
    }

    [Fact]
    public void EnabledWithoutIncludedSetIsReadinessIssueNotSchemaError()
    {
        CustomDriverSetsSettings settings = Enabled([Set("a", 'a', included: false)]);

        Assert.Empty(CustomDriverSetsSettingsValidator.Validate(settings));
        Assert.NotEmpty(CustomDriverSetsSettingsValidator.Validate(settings, requireIncludedSet: true));
        Assert.False(CustomDriverSetsSettingsValidator.IsReady(settings, _ => true));
        Assert.True(CustomDriverSetsSettingsValidator.IsReady(new(), _ => false));
        Assert.False(CustomDriverSetsSettingsValidator.IsReady(Enabled([Set("a", 'a')]), _ => false));
        Assert.True(CustomDriverSetsSettingsValidator.IsReady(Enabled([Set("a", 'a')]), _ => true));
    }

    [Fact]
    public void GeneratorMapsIncludedSetsAndDefault()
    {
        CustomDriverSetReference included = Set("a", 'a');
        included = included with { Package = included.Package with { Length = 1234, FileCount = 42 } };
        var document = new FoundryConfigurationDocument
        {
            CustomDrivers = Enabled([included, Set("b", 'b', included: false)]) with { DefaultSetId = "a" }
        };

        var generated = new DeployConfigurationGenerator().Generate(document, null, null, null, null);

        Assert.True(generated.CustomDrivers.IsEnabled);
        var set = Assert.Single(generated.CustomDrivers.Sets);
        Assert.Equal("a", set.Id);
        Assert.Equal(new string('a', 64), set.ContentHash);
        Assert.Equal(1234, set.Length);
        Assert.Equal(42, set.FileCount);
        Assert.Equal("a", generated.CustomDrivers.DefaultSetId);
    }

    [Fact]
    public void GeneratorOmitsSetsWhenDisabled()
    {
        var document = new FoundryConfigurationDocument { CustomDrivers = Enabled([Set("a", 'a')]) with { IsEnabled = false } };

        var generated = new DeployConfigurationGenerator().Generate(document, null, null, null, null);

        Assert.False(generated.CustomDrivers.IsEnabled);
        Assert.Empty(generated.CustomDrivers.Sets);
    }

    [Fact]
    public void MissingSectionDeserializesDisabled()
    {
        var document = JsonSerializer.Deserialize<FoundryConfigurationDocument>("{\"SchemaVersion\":18}")!;

        Assert.False(document.CustomDrivers.IsEnabled);
        Assert.Empty(document.CustomDrivers.Sets);
        Assert.Null(document.CustomDrivers.DefaultSetId);
    }

    [Fact]
    public void Schema18DocumentMigratesToSchema19WithCustomDriversDisabled()
    {
        var old = JsonSerializer.Deserialize<FoundryConfigurationDocument>("{\"SchemaVersion\":18}")!;

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplySchemaMigrations(old);

        Assert.False(migrated.CustomDrivers.IsEnabled);
        Assert.Equal(19, migrated.SchemaVersion);
    }

    private static CustomDriverSetsSettings Enabled(IReadOnlyList<CustomDriverSetReference> sets) => new() { IsEnabled = true, Sets = sets };

    private static CustomDriverSetReference Set(string id, char hashChar, bool included = true, string? hash = null) => new()
    {
        Id = id,
        DisplayName = "Set " + id,
        IsIncluded = included,
        Package = new() { ContentHash = hash ?? new string(hashChar, 64), DisplayName = "Package", Length = 10, FileCount = 3 }
    };
}
