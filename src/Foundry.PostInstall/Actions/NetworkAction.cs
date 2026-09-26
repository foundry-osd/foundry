// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Actions;

public sealed class NetworkAction(string root, string windowsRoot, IPreOobeProcessExecutor processes, ICertificateImporter certificates)
{
    private static readonly XNamespace Wlan = "http://www.microsoft.com/networking/WLAN/profile/v1";

    public async Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, CancellationToken cancellationToken)
    {
        NetworkSettings parameters = BuiltInSettings.Read<NetworkSettings>(action);
        string settingsPath = OwnedPaths.Resolve(root, parameters.SettingsPath);
        if (new FileInfo(settingsPath).Length > 1024 * 1024) throw new InvalidDataException("Network settings are too large.");
        using JsonDocument settings = JsonDocument.Parse(await File.ReadAllBytesAsync(settingsPath, cancellationToken).ConfigureAwait(false));
        bool warning = false;
        if (settings.RootElement.TryGetProperty("certificates", out JsonElement certificateList))
        {
            foreach (JsonElement certificate in certificateList.EnumerateArray())
            {
                string path = DataPath(Text(certificate, "relativePath"));
                string? password = Text(certificate, "passwordRelativePath");
                try
                {
                    certificates.Import(path, Text(certificate, "kind") ?? "certificate",
                        Text(certificate, "storeName") ?? "Root", password is null ? null : DataPath(password));
                }
                catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
                { warning = true; }
            }
        }
        string? wired = Text(settings.RootElement, "wiredDot1xProfileRelativePath");
        if (wired is not null)
        {
            ProcessOutcome result = await Netsh(["lan", "add", "profile", "filename=" + DataPath(wired)], cancellationToken).ConfigureAwait(false);
            if (result.TerminationUncertain || result.ExitCode == 1641) return BuiltInSettings.Observe(result);
            warning |= result.ExitCode != 0;
        }
        string? wifi = Text(settings.RootElement, "wifiProfileRelativePath");
        if (wifi is not null)
        {
            string path = DataPath(wifi);
            bool connect = string.Equals(Text(settings.RootElement, "wifiProfileConnectivityExpectation"), "preOobeConnectable", StringComparison.OrdinalIgnoreCase);
            if (connect)
            {
                try { await File.WriteAllTextAsync(path, NormalizeWifi(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) when (ex is XmlException or IOException or InvalidDataException) { warning = true; }
            }
            ProcessOutcome result = await Netsh(["wlan", "add", "profile", "filename=" + path, "user=all"], cancellationToken).ConfigureAwait(false);
            if (result.TerminationUncertain || result.ExitCode == 1641) return BuiltInSettings.Observe(result);
            warning |= result.ExitCode != 0;
            if (connect)
            {
                string? name = GetWifiName(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result = await Netsh(["wlan", "connect", "name=" + name], cancellationToken).ConfigureAwait(false);
                    if (result.TerminationUncertain || result.ExitCode == 1641) return BuiltInSettings.Observe(result);
                    warning |= result.ExitCode != 0;
                }
            }
        }
        return new(true, HasWarnings: warning);
    }

    private string DataPath(string? relative) => OwnedPaths.Resolve(OwnedPaths.Resolve(root, "Payloads"),
        relative ?? throw new InvalidDataException("A network input path is missing."));

    private Task<ProcessOutcome> Netsh(string[] arguments, CancellationToken cancellationToken) => processes.RunAsync(
        new(Path.Combine(windowsRoot, "System32", "netsh.exe"), arguments, root, TimeSpan.FromMinutes(2)), cancellationToken);

    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString()) ? property.GetString() : null;

    private static XDocument ReadXml(string content)
    {
        using var reader = XmlReader.Create(new StringReader(content), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    public static string NormalizeWifi(string content)
    {
        XDocument document = ReadXml(content);
        XElement root = document.Root ?? throw new InvalidDataException("Wi-Fi profile is empty.");
        if (root.Name != Wlan + "WLANProfile") throw new InvalidDataException("Wi-Fi profile namespace is invalid.");
        XElement? mode = root.Element(Wlan + "connectionMode");
        if (mode is null)
        {
            XElement type = root.Element(Wlan + "connectionType") ?? throw new InvalidDataException("Wi-Fi connection type is missing.");
            mode = new XElement(Wlan + "connectionMode");
            type.AddAfterSelf(mode);
        }
        mode.Value = "auto";
        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static string? GetWifiName(string content) => ReadXml(content).Root?.Element(Wlan + "name")?.Value;
}
