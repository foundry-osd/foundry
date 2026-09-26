// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;

namespace Foundry.PostInstall.Actions;

public sealed record LicensingProduct(string Id, string Edition, int? LicenseStatus, string Channel,
    string PartialKey, string Description, string KmsMachine);
public sealed record FirmwareLicense(string Key, string Description, string KmsMachine, bool IsKmsMachine);
public sealed record ActivationResult(bool Failed, string Stage, int? HResult = null);

public interface IWindowsLicensing
{
    bool IsClient { get; }
    string Edition { get; }
    FirmwareLicense Firmware { get; }
    IReadOnlyList<LicensingProduct> GetProducts();
    void InstallKey(string key);
    void Refresh();
    void Activate(string id);
}

public sealed class OemActivation(IWindowsLicensing licensing)
{
    private static readonly IReadOnlyDictionary<string, string> SetupKeySuffixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Core"] = "8HVX7",
        ["CoreN"] = "WXCHW",
        ["CoreSingleLanguage"] = "6F4BT",
        ["CoreCountrySpecific"] = "8TYMD",
        ["Professional"] = "3V66T",
        ["ProfessionalN"] = "PKCKT"
    };

    public ActivationResult Run()
    {
        string stage = "discovery";
        try
        {
            if (!licensing.IsClient || !SetupKeySuffixes.TryGetValue(licensing.Edition, out string? defaultSuffix)) return new(false, "skipped");
            LicensingProduct? product = ResolveProduct();
            if (product is null || product.LicenseStatus == 1) return new(false, "skipped");
            FirmwareLicense firmware = licensing.Firmware;
            if (product.Channel.StartsWith("Volume", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(product.Description, @"\bVOLUME\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                !string.IsNullOrWhiteSpace(product.KmsMachine) || !string.IsNullOrWhiteSpace(firmware.KmsMachine) || firmware.IsKmsMachine)
                return new(false, "skipped");
            if (!Regex.IsMatch(firmware.Key, "^[A-Z0-9]{5}(-[A-Z0-9]{5}){4}$", RegexOptions.CultureInvariant) ||
                !Regex.IsMatch(firmware.Description, @"(?:^|\s)" + Regex.Escape(licensing.Edition) + @"\s+OEM:DM\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return new(false, "skipped");
            string suffix = firmware.Key[^5..];
            bool installed = product.Channel.Equals("OEM:DM", StringComparison.OrdinalIgnoreCase) && product.PartialKey == suffix;
            bool replaceable = product.Channel.Equals("Retail", StringComparison.OrdinalIgnoreCase) && product.PartialKey == defaultSuffix;
            if (!installed && !replaceable) return new(false, "skipped");
            if (!installed)
            {
                stage = "install";
                licensing.InstallKey(firmware.Key);
                stage = "refresh";
                licensing.Refresh();
                product = ResolveProduct();
                if (product is null || product.LicenseStatus != 1 &&
                    (!product.Channel.Equals("OEM:DM", StringComparison.OrdinalIgnoreCase) || product.PartialKey != suffix))
                    return new(true, "verification");
            }
            if (product.LicenseStatus != 1)
            {
                stage = "activate";
                licensing.Activate(product.Id);
                stage = "refresh";
                licensing.Refresh();
                if (ResolveProduct()?.LicenseStatus != 1) return new(true, "verification");
            }
            return new(false, "completed");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Licensing providers may include keys in exception messages and arguments.
            return new(true, stage, exception.HResult);
        }
    }

    private LicensingProduct? ResolveProduct()
    {
        LicensingProduct[] products = licensing.GetProducts().Where(product =>
            product.Edition.Equals(licensing.Edition, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(product.PartialKey)).ToArray();
        return products.Length == 1 && products[0].LicenseStatus is >= 0 and <= 6 && !string.IsNullOrWhiteSpace(products[0].Id)
            ? products[0] : null;
    }
}
