// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace Foundry.Core.Services.Catalog;

public static class VerifiedCatalogContent
{
    public static XDocument Parse(VerifiedCatalogDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SourceUri is null || !document.SourceUri.IsAbsoluteUri || document.SourceUri.AbsoluteUri != VerifiedCatalogSources.GetUri(document.Id).AbsoluteUri || document.Content is null ||
            document.Content.Length is < 1 or > VerifiedCatalogSnapshotAcquirer.MaximumBytes || document.RetrievedUtc == default)
            throw new InvalidDataException("The catalog identity is invalid.");
        string hash = Convert.ToHexString(SHA256.HashData(document.Content));
        if (!string.Equals(hash, document.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals("sha256:" + hash, document.Revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The catalog bytes do not match their acquired identity.");
        using var stream = new MemoryStream(document.Content, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = VerifiedCatalogSnapshotAcquirer.MaximumBytes
        });
        XDocument xml = XDocument.Load(reader);
        if (xml.Root is null || (document.Id == VerifiedCatalogSources.OperatingSystems &&
            xml.Root.Attribute("schemaVersion")?.Value != "4"))
            throw new InvalidDataException("The catalog schema is incompatible.");
        return xml;
    }
}
