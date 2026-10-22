// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeContentPackagePruneTests : IDisposable
{
    private const string MediaRoot = "Cache/DriverPacks/Custom";
    private readonly string root = Path.Combine(Path.GetTempPath(), "Fprune", Guid.NewGuid().ToString("N"));
    private readonly WinPeContentPackageMediaService service = new(ContentPackageKind.Drivers, _ => long.MaxValue);
    private CustomDriverSetLibrary? library;
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private string Media => Path.Combine(root, "media");

    [Fact]
    public async Task PruneRemovesUnreferencedPackagesThenTheirManifests()
    {
        ContentPackageReference old = await ImportAsync("old");
        ContentPackageReference current = await ImportAsync("current");
        string oldManifest = await PublishAsync(old);
        using WinPeContentPackageMediaLease lease = await PrepareAsync(current);
        await service.PublishAsync(lease, Media, Cancellation);

        ContentPackagePruneResult result = await service.PruneAsync(lease, Media, Cancellation);

        Assert.Equal(1, result.RemovedPackages);
        Assert.Equal(1, result.RemovedManifests);
        Assert.Empty(result.RetainedUnknownEntries);
        Assert.False(Directory.Exists(PackagePath(old)));
        Assert.False(File.Exists(oldManifest));
        Assert.True(Directory.Exists(PackagePath(current)));
        Assert.True(File.Exists(Path.Combine(Media, lease.ManifestRelativePath)));
    }

    [Fact]
    public async Task PruneKeepsAPackageReusedByTheCurrentGeneration()
    {
        ContentPackageReference shared = await ImportAsync("shared");
        ContentPackageReference old = await ImportAsync("old");
        ContentPackageReference added = await ImportAsync("added");
        string oldManifest = await PublishAsync(shared, old);
        using WinPeContentPackageMediaLease lease = await PrepareAsync(shared, added);
        await service.PublishAsync(lease, Media, Cancellation);

        ContentPackagePruneResult result = await service.PruneAsync(lease, Media, Cancellation);

        Assert.Equal(1, result.RemovedPackages);
        Assert.Equal(1, result.RemovedManifests);
        Assert.False(Directory.Exists(PackagePath(old)));
        Assert.False(File.Exists(oldManifest));
        Assert.All(lease.Files, file => Assert.True(File.Exists(Path.Combine(Media, file.RelativePath)), file.RelativePath));
    }

    [Fact]
    public async Task PruneKeepsUnknownFilesAndReportsThem()
    {
        ContentPackageReference old = await ImportAsync("old");
        ContentPackageReference current = await ImportAsync("current");
        string oldManifest = await PublishAsync(old);
        using WinPeContentPackageMediaLease lease = await PrepareAsync(current);
        await service.PublishAsync(lease, Media, Cancellation);
        string extra = Path.Combine(PackagePath(old), "files", "sub", "extra.txt");
        await File.WriteAllTextAsync(extra, "user", Cancellation);
        string orphan = Path.Combine(Media, MediaRoot, "Packages", new string('b', 64));
        Directory.CreateDirectory(orphan);
        string notes = Path.Combine(Media, MediaRoot, "Packages", "notes.txt");
        await File.WriteAllTextAsync(notes, "user", Cancellation);
        string readme = Path.Combine(Media, MediaRoot, "manifests", "readme.json");
        await File.WriteAllTextAsync(readme, "{}", Cancellation);

        ContentPackagePruneResult result = await service.PruneAsync(lease, Media, Cancellation);

        Assert.Equal(0, result.RemovedPackages);
        Assert.Equal(0, result.RemovedManifests);
        Assert.True(File.Exists(extra));
        Assert.False(File.Exists(Path.Combine(PackagePath(old), "files", "old.inf")));
        Assert.True(Directory.Exists(orphan));
        Assert.True(File.Exists(notes));
        Assert.True(File.Exists(readme));
        Assert.True(File.Exists(oldManifest));
        string oldPackage = $"{MediaRoot}/Packages/{old.ContentHash}";
        Assert.Contains($"{oldPackage}/files/sub/extra.txt", result.RetainedUnknownEntries);
        Assert.Contains($"{MediaRoot}/Packages/{new string('b', 64)}", result.RetainedUnknownEntries);
        Assert.Contains($"{MediaRoot}/Packages/notes.txt", result.RetainedUnknownEntries);
        Assert.Contains($"{MediaRoot}/manifests/readme.json", result.RetainedUnknownEntries);
    }

    [Fact]
    public async Task PruneRefusesReparsePoints()
    {
        ContentPackageReference old = await ImportAsync("old");
        ContentPackageReference current = await ImportAsync("current");
        string oldManifest = await PublishAsync(old);
        using WinPeContentPackageMediaLease lease = await PrepareAsync(current);
        await service.PublishAsync(lease, Media, Cancellation);
        string outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        string victim = Path.Combine(outside, "old.inf");
        await File.WriteAllTextAsync(victim, "outside", Cancellation);
        string link = Path.Combine(PackagePath(old), "files", "link");
        if (!TryCreateJunction(link, outside, out string failure)) Assert.Skip($"Junction creation is not available here: {failure}");

        ContentPackagePruneResult result = await service.PruneAsync(lease, Media, Cancellation);

        Assert.Equal(0, result.RemovedPackages);
        Assert.Equal(0, result.RemovedManifests);
        Assert.True(File.Exists(victim));
        Assert.True(Directory.Exists(PackagePath(old)));
        Assert.True(File.Exists(Path.Combine(PackagePath(old), "files", "old.inf")));
        Assert.True(File.Exists(oldManifest));
        Assert.Contains($"{MediaRoot}/Packages/{old.ContentHash}", result.RetainedUnknownEntries);
        Directory.Delete(link);
    }

    [Fact]
    public async Task PruneKeepsAManifestWhoseFolderCouldNotBeRemoved()
    {
        ContentPackageReference old = await ImportAsync("old");
        ContentPackageReference current = await ImportAsync("current");
        string oldManifest = await PublishAsync(old);
        using WinPeContentPackageMediaLease lease = await PrepareAsync(current);
        await service.PublishAsync(lease, Media, Cancellation);
        string changed = Path.Combine(PackagePath(old), "files", "sub", "old.sys");
        await File.WriteAllTextAsync(changed, "a different length than the recorded file", Cancellation);

        ContentPackagePruneResult result = await service.PruneAsync(lease, Media, Cancellation);

        Assert.Equal(0, result.RemovedPackages);
        Assert.Equal(0, result.RemovedManifests);
        Assert.True(File.Exists(changed));
        Assert.False(File.Exists(Path.Combine(PackagePath(old), "files", "old.inf")));
        Assert.True(File.Exists(oldManifest));
        Assert.Contains($"{MediaRoot}/Packages/{old.ContentHash}/files/sub/old.sys", result.RetainedUnknownEntries);
    }

    [Fact]
    public async Task PruneThrowsForPostInstallationKind()
    {
        using var lease = new WinPeContentPackageMediaLease(ContentPackageKind.PostInstallation,
            new() { Id = Guid.NewGuid().ToString("N") }, "{}"u8.ToArray(), [], [], []);
        var postInstallation = new WinPeContentPackageMediaService(ContentPackageKind.PostInstallation, _ => long.MaxValue);

        await Assert.ThrowsAsync<InvalidOperationException>(() => postInstallation.PruneAsync(lease, Media, Cancellation));
    }

    private CustomDriverSetLibrary Library => library ??= new CustomDriverSetLibrary(Path.Combine(root, "library"));

    private string PackagePath(ContentPackageReference reference) =>
        Path.Combine(Media, MediaRoot.Replace('/', Path.DirectorySeparatorChar), "Packages", reference.ContentHash);

    private Task<WinPeContentPackageMediaLease> PrepareAsync(params ContentPackageReference[] references) =>
        service.PrepareAsync(Library.Library, references, Cancellation);

    /// <summary>Publishes one older generation and returns the full path of its manifest.</summary>
    private async Task<string> PublishAsync(params ContentPackageReference[] references)
    {
        using WinPeContentPackageMediaLease lease = await PrepareAsync(references);
        await service.PublishAsync(lease, Media, Cancellation);
        return Path.Combine(Media, lease.ManifestRelativePath);
    }

    private async Task<ContentPackageReference> ImportAsync(string name)
    {
        string source = Path.Combine(root, "sources", name);
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        await File.WriteAllTextAsync(Path.Combine(source, name + ".inf"), "[Version]\r\n" + name, Cancellation);
        await File.WriteAllTextAsync(Path.Combine(source, "sub", name + ".sys"), "driver " + name, Cancellation);
        return await Library.ImportAsync(source, Cancellation);
    }

    private static bool TryCreateJunction(string link, string target, out string failure)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "/c", "mklink", "/J", link, target }) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        failure = $"{output} {error}".Trim();
        return process.ExitCode == 0;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
