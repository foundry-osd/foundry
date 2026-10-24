// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Tests.Sharing;

/// <summary>An application host over an in-memory configuration and vault.</summary>
internal sealed class FakeSharingHost : ISharingHost
{
    public FoundryConfigurationDocument CurrentConfiguration { get; set; } = new();

    public bool IsReadyToBuild { get; set; } = true;

    public bool IsVaultAvailable { get; set; } = true;

    public Dictionary<string, string> Vault { get; } = new(StringComparer.Ordinal);

    public Func<FoundryConfigurationDocument, bool> ContentAvailable { get; set; } = _ => true;

    public List<FoundryConfigurationDocument> Applied { get; } = [];

    public string? ManagedBy { get; private set; }

    public int ClearManagedCount { get; private set; }

    public IReadOnlyDictionary<string, string> ReadVault() => new Dictionary<string, string>(Vault, StringComparer.Ordinal);

    public void WriteVault(IReadOnlyDictionary<string, string> entries, IReadOnlyCollection<string>? removeIds = null)
    {
        foreach (string id in removeIds ?? [])
        {
            Vault.Remove(id);
        }

        foreach ((string id, string value) in entries)
        {
            Vault[id] = value;
        }
    }

    public Task ApplyConfigurationAsync(FoundryConfigurationDocument configuration, IReadOnlyDictionary<string, string> vaultEntries, string managedBy,
        CancellationToken cancellationToken)
    {
        CurrentConfiguration = configuration;
        WriteVault(vaultEntries);
        ManagedBy = managedBy;
        Applied.Add(configuration);
        return Task.CompletedTask;
    }

    public void ClearManaged()
    {
        ManagedBy = null;
        ClearManagedCount++;
    }

    public bool AllContentAvailable(FoundryConfigurationDocument configuration) => ContentAvailable(configuration);
}

/// <summary>Serves authoring file content from memory; can hold every open until <see cref="Gate"/> completes.</summary>
internal sealed class InMemoryContentSource : IContentSource
{
    private readonly Dictionary<string, byte[]> contents = new(StringComparer.Ordinal);
    private int openCount;

    public TaskCompletionSource? Gate { get; set; }

    public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int OpenCount => openCount;

    public AuthoringFileReference Add(string text, string fileName)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        contents[hash] = bytes;
        return new AuthoringFileReference { ContentHash = hash, FileName = fileName, Length = bytes.Length };
    }

    public async Task<Stream> OpenAsync(PackageContent content, CancellationToken ct)
    {
        Interlocked.Increment(ref openCount);
        Opened.TrySetResult();
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        return new MemoryStream(contents[content.Hash], writable: false);
    }
}

/// <summary>A clock that never moves.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    public static DateTimeOffset Now { get; } = new(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>The four content libraries of one PC under <paramref name="root"/>.</summary>
internal sealed class SharingLibraries(string root)
{
    public AuthoringFileLibrary Files { get; } = new(Path.Combine(root, "files"));

    public ContentSync Sync() => new(Files, new ContentPackageLibraryService(Path.Combine(root, "packages"), ContentPackageKind.PostInstallation),
        new CustomDriverSetLibrary(Path.Combine(root, "drivers")), new CustomImageLibraryService(Path.Combine(root, "images")),
        Path.Combine(root, "sync-temp"));
}
