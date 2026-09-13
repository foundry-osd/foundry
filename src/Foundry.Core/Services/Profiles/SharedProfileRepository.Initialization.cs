// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog;

namespace Foundry.Core.Services.Profiles;

public sealed partial class SharedProfileRepository
{
    private void InitializeStorage(SharedProfileRepositoryFiles files, CancellationToken token)
    {
        string manifestPath = Path.Combine(rootPath, "repository.json");
        bool hasManifest = files.Exists(manifestPath);
        if (hasManifest)
        {
            ValidateManifest(files);
            if (Directory.Exists(profilePath)) return;
        }
        else
        {
            // Only this enrollment's private staging namespace can survive an unpublished first attempt.
            ValidateDirectoryContents(rootPath, ["repository.lock"], [Path.GetFileName(initializationPath)]);
        }

        ValidateInitializationStaging();
        Directory.CreateDirectory(initializationPath);
        string stagedManifest = Path.Combine(initializationPath, "repository.json");
        if (!hasManifest)
            files.WriteStagedSigned(stagedManifest, "repository", new RepositoryManifest(1, repositoryId, keyEpoch));

        string stagedProfile = Path.Combine(initializationPath, "profile");
        Directory.CreateDirectory(Path.Combine(stagedProfile, "revisions"));
        using (FileStream stagedJournal = files.AcquireLock(Path.Combine(stagedProfile, "head.journal")))
            files.AppendJournal(stagedJournal, 0, CreateHead(null));

        token.ThrowIfCancellationRequested();
        if (!hasManifest)
        {
            SharedProfileRepositoryFiles.ValidatePath(manifestPath);
            File.Move(stagedManifest, manifestPath);
        }
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
        SharedProfileRepositoryFiles.ValidatePath(profilePath);
        Directory.Move(stagedProfile, profilePath);
    }

    private void ValidateInitializationStaging()
    {
        ValidateDirectoryContents(initializationPath, ["repository.json"], ["profile"]);
        string stagedProfile = Path.Combine(initializationPath, "profile");
        ValidateDirectoryContents(stagedProfile, ["head.journal"], ["revisions"]);
        ValidateDirectoryContents(Path.Combine(stagedProfile, "revisions"), [], []);
    }

    private static void ValidateDirectoryContents(string directory, string[] files, string[] directories)
    {
        SharedProfileRepositoryFiles.ValidatePath(directory);
        if (!Directory.Exists(directory))
        {
            if (File.Exists(directory)) throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.FolderNotEmpty);
            return;
        }
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            SharedProfileRepositoryFiles.ValidatePath(path);
            string[] allowed = (File.GetAttributes(path) & FileAttributes.Directory) != 0 ? directories : files;
            if (!allowed.Contains(Path.GetFileName(path), StringComparer.Ordinal))
                throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.FolderNotEmpty);
        }
    }

    private void CleanupInitialization()
    {
        try
        {
            ValidateInitializationStaging();
            if (!Directory.Exists(initializationPath)) return;
            string stagedProfile = Path.Combine(initializationPath, "profile");
            if (Directory.Exists(stagedProfile))
            {
                string revisions = Path.Combine(stagedProfile, "revisions");
                if (Directory.Exists(revisions)) Directory.Delete(revisions);
                File.Delete(Path.Combine(stagedProfile, "head.journal"));
                Directory.Delete(stagedProfile);
            }
            File.Delete(Path.Combine(initializationPath, "repository.json"));
            Directory.Delete(initializationPath);
        }
        catch (Exception exception) when (IsCleanupFailure(exception))
        {
            Log.ForContext<SharedProfileRepository>().Warning(exception,
                "Shared profile initialization staging cleanup remains pending. RepositoryId={RepositoryId}, ProfileId={ProfileId}", repositoryId, profileId);
        }
    }
}
