// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.PostInstall.Actions;

public sealed record ProvisionedPackage(string DisplayName, string PackageName);

public static class DismInventory
{
    public static IReadOnlyList<ProvisionedPackage> Parse(string output)
    {
        List<ProvisionedPackage> result = [];
        string? display = null;
        foreach (string line in output.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (key.Equals("DisplayName", StringComparison.OrdinalIgnoreCase))
            {
                if (display is not null || value.Length == 0) throw new InvalidDataException("Ambiguous DISM inventory.");
                display = value;
            }
            else if (key.Equals("PackageName", StringComparison.OrdinalIgnoreCase))
            {
                if (display is null || value.Length == 0 || value.Any(char.IsControl) ||
                    result.Any(package => package.PackageName.Equals(value, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Ambiguous DISM package identity.");
                result.Add(new(display, value));
                display = null;
            }
        }
        if (display is not null || !output.Contains("The operation completed successfully.", StringComparison.OrdinalIgnoreCase) ||
            result.Count == 0 && !(output.Contains("No provisioned", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Deployment Image Servicing and Management tool", StringComparison.Ordinal) && output.Contains("Image Version:", StringComparison.Ordinal)))
            throw new InvalidDataException("DISM did not return a recognized inventory.");
        return result;
    }

    public static IReadOnlyList<ProvisionedPackage> Select(IReadOnlyList<ProvisionedPackage> packages,
        IReadOnlyList<string> selected, bool prefix) => packages.Where(package => selected.Any(name =>
            package.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            prefix && package.PackageName.StartsWith(name, StringComparison.OrdinalIgnoreCase))).ToArray();
}
