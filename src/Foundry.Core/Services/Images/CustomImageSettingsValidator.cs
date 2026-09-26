// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Images;

/// <summary>Validates identity and selection only; Windows and customization compatibility remain user decisions.</summary>
public static class CustomImageSettingsValidator
{
    public const int MaximumImages = 256;
    public const int MaximumIndexes = 1024;
    public const int MaximumDisplayNameLength = 200;

    /// <summary>Validates profile settings, optionally requiring included content for media creation.</summary>
    /// <param name="settings">The custom image settings to validate.</param>
    /// <param name="requireIncludedImage">Requires an included image when enabled; defaults to false so incomplete profiles can be saved.</param>
    public static IReadOnlyList<string> Validate(CustomImagesSettings settings, bool requireIncludedImage = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<string>();
        if (settings.Images is null || settings.Images.Count > MaximumImages)
            return ["The custom image list is invalid or exceeds the image limit."];
        if (!Enum.IsDefined(settings.DefaultSource)) errors.Add("The default image source is invalid.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CustomImageReference image in settings.Images)
        {
            if (image is null || !IsValidReference(image) || !ids.Add(image.Id) || !hashes.Add(image.ContentHash))
                errors.Add("A custom image reference is invalid or duplicated.");
        }
        if (!settings.IsEnabled || errors.Count > 0) return errors;
        if (requireIncludedImage && !settings.Images.Any(image => image.IsIncluded))
            errors.Add("At least one custom image must be included.");
        CustomImageReference? selected = settings.Images.FirstOrDefault(image => image?.Id == settings.DefaultImageId);
        if (settings.DefaultImageId is not null && (selected is null || !selected.IsIncluded))
            errors.Add("The default custom image must be included.");
        if (settings.DefaultImageIndex is not null && (selected is null || !selected.Indexes.Any(index => index.Index == settings.DefaultImageIndex)))
            errors.Add("The default image index is unavailable.");
        return errors;
    }

    /// <summary>Rejects invalid profile settings, optionally requiring included content for media creation.</summary>
    /// <param name="settings">The custom image settings to validate.</param>
    /// <param name="requireIncludedImage">Requires an included image when enabled; defaults to false so incomplete profiles can be saved.</param>
    public static void ThrowIfInvalid(CustomImagesSettings settings, bool requireIncludedImage = false)
    {
        IReadOnlyList<string> errors = Validate(settings, requireIncludedImage);
        if (errors.Count != 0) throw new InvalidDataException(string.Join(" ", errors));
    }

    public static bool IsValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    public static bool IsValidReference(CustomImageReference image) =>
        image.Id is { Length: > 0 and <= 128 } && !image.Id.Any(char.IsControl) &&
        IsValidHash(image.ContentHash) &&
        image.DisplayName is { Length: > 0 and <= MaximumDisplayNameLength } && !image.DisplayName.Any(char.IsControl) &&
        image.Length > 0 &&
        image.Indexes is { Count: > 0 and <= MaximumIndexes } &&
        image.Indexes.All(index => index is not null && index.Index > 0 && index.ExpandedSizeBytes >= 0 &&
            index.Name is { Length: <= 4096 } && index.Description is { Length: <= 16384 } && index.Architecture is { Length: <= 128 } &&
            index.EditionId is { Length: <= 256 } && index.ProductType is { Length: <= 256 } &&
            (index.Version is null || index.Version.Length <= 128) &&
            (index.DefaultLanguage is null || index.DefaultLanguage.Length <= 128) &&
            index.Languages is { Count: <= 256 } && index.Languages.All(language => language is { Length: <= 128 })) &&
        image.Indexes.Select(index => index.Index).Distinct().Count() == image.Indexes.Count;

    public static string NormalizeDisplayName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string result = name.Trim();
        if (result.Length > MaximumDisplayNameLength || result.Any(char.IsControl))
            throw new InvalidDataException("The image display name is too long or contains control characters.");
        return result;
    }
}
