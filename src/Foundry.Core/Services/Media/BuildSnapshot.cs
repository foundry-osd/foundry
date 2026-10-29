// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.WinPe;
using Foundry.Telemetry;
using Foundry.Utilities.IO;
using Serilog;

namespace Foundry.Core.Services.Media;

/// <summary>Owns one media build's frozen configuration, session secrets and private source copies.</summary>
public sealed class BuildSnapshot : IDisposable
{
    private const long MaximumDriverBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumDriverEntries = 10_000;
    // Copied drivers plus materialized dependencies; a private folder above this bound is refused instead of deleted.
    private const int MaximumPrivateEntries = MaximumDriverEntries + 1024;
    private const int MaximumSourceBytes = 4 * 1024 * 1024;
    private const int MaximumTotalSourceBytes = 8 * 1024 * 1024;

    /// <summary>Message of the <see cref="FileNotFoundException"/> raised when a referenced media source is missing or unavailable.</summary>
    internal const string DependencyUnavailableMessage = "A referenced media source file is unavailable. Select the file again before building media.";

    private readonly OobeAccountSecretState accountSecrets = new();
    private readonly DomainJoinSecretState domainSecrets = new();
    private readonly char[] deploymentPassword;
    private readonly char[]? wifiPassphrase;
    private readonly char[]? wiredCertificatePassword;
    private readonly char[]? wifiCertificatePassword;
    private readonly char[]? autopilotCertificatePassword;
    private readonly Dictionary<string, char[]> networkCachePasswords = new(StringComparer.Ordinal);
    private readonly NetworkCacheIndexBinding? networkCacheIndex;
    private readonly string privateRootDirectory;
    private readonly string? authoringVersion;
    private readonly AuthoringFileLibrary? files;
    // Private copies of the captured authoring files, keyed by content hash.
    private readonly Dictionary<string, string> sourceCopies = new(StringComparer.OrdinalIgnoreCase);
    private FoundryConfigurationDocument configuration;
    private bool isDisposed;
    private PrivateDirectoryLease? privateDirectory;
    // Keeps the referenced library files from cleanup from the moment the configuration is captured until they are copied.
    private IDisposable? sourceFilesLease;

    private BuildSnapshot(FoundryConfigurationDocument source, BuildSnapshotSources? sources, ReadOnlySpan<char> password, string root, string? authoringVersion)
    {
        configuration = CloneConfiguration(source);
        this.authoringVersion = authoringVersion;
        files = sources?.Files;
        sourceFilesLease = files?.Lease(ConfigurationContentReferences.AuthoringFileHashes(source));
        deploymentPassword = password.ToArray();
        wifiPassphrase = sources?.Network.WifiPassphrase?.ToCharArray();
        wiredCertificatePassword = sources?.Network.WiredCertificatePfxPassword?.ToCharArray();
        wifiCertificatePassword = sources?.Network.WifiCertificatePfxPassword?.ToCharArray();
        autopilotCertificatePassword = sources?.AutopilotPfxPassword?.ToCharArray();
        // Only the passwords of accounts a share uses are kept, so no other secret stays in memory for the build.
        IReadOnlyCollection<string> usedAccounts = NetworkCacheConfigurationValidator.UsedAccountIds(source.NetworkCache);
        foreach ((string accountId, string value) in sources?.NetworkCachePasswords ?? new Dictionary<string, string>())
            if (usedAccounts.Contains(accountId)) networkCachePasswords[accountId] = value.ToCharArray();
        networkCacheIndex = sources?.NetworkCacheIndex;
        privateRootDirectory = Path.GetFullPath(root);
    }

    /// <summary>Returns a detached configuration copy without inactive sources or password envelopes.</summary>
    public FoundryConfigurationDocument Configuration
    {
        get
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            return CloneConfiguration(configuration);
        }
    }

    /// <summary>Gets the private copy of each captured answer file, keyed by answer-file id.</summary>
    public IReadOnlyDictionary<string, string> UnattendSourcePaths
    {
        get
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            return configuration.Unattend.Files
                .Where(file => file.File is not null && sourceCopies.ContainsKey(file.File.ContentHash))
                .ToDictionary(file => file.Id, file => sourceCopies[file.File!.ContentHash], StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Copies configuration and secrets synchronously, then captures bounded source files off-thread before returning a usable snapshot.</summary>
    public static Task<BuildSnapshot> CaptureAsync(
        FoundryConfigurationDocument configuration,
        OobeAccountSecretState accountSecrets,
        ReadOnlySpan<char> deploymentPassword,
        string privateRootDirectory,
        string? authoringVersion,
        CancellationToken cancellationToken = default) =>
        CaptureAsync(configuration, null, accountSecrets, null, deploymentPassword, privateRootDirectory, authoringVersion, cancellationToken);

    /// <summary>Copies active domain credentials synchronously before source preparation can await or authoring can change.</summary>
    public static Task<BuildSnapshot> CaptureAsync(
        FoundryConfigurationDocument configuration,
        OobeAccountSecretState accountSecrets,
        DomainJoinSecretState? domainSecrets,
        ReadOnlySpan<char> deploymentPassword,
        string privateRootDirectory,
        string? authoringVersion,
        CancellationToken cancellationToken = default) =>
        CaptureAsync(configuration, null, accountSecrets, domainSecrets, deploymentPassword, privateRootDirectory, authoringVersion, cancellationToken);

    /// <summary>
    /// Captures like the other overloads, copying the session passwords and verified copies of the referenced files from <paramref name="sources"/>.
    /// </summary>
    public static Task<BuildSnapshot> CaptureAsync(
        FoundryConfigurationDocument configuration,
        BuildSnapshotSources? sources,
        OobeAccountSecretState accountSecrets,
        DomainJoinSecretState? domainSecrets,
        ReadOnlySpan<char> deploymentPassword,
        string privateRootDirectory,
        string? authoringVersion,
        CancellationToken cancellationToken = default) =>
        CaptureAsync(configuration, sources, accountSecrets, domainSecrets, deploymentPassword, privateRootDirectory, authoringVersion, driverFilesCopied: null, cancellationToken);

    /// <summary>Captures like the public overload and reports the running count of copied custom driver files synchronously on the copy thread.</summary>
    internal static Task<BuildSnapshot> CaptureAsync(
        FoundryConfigurationDocument configuration,
        BuildSnapshotSources? sources,
        OobeAccountSecretState accountSecrets,
        DomainJoinSecretState? domainSecrets,
        ReadOnlySpan<char> deploymentPassword,
        string privateRootDirectory,
        string? authoringVersion,
        IProgress<int>? driverFilesCopied,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(accountSecrets);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateRootDirectory);
        var snapshot = new BuildSnapshot(configuration, sources, deploymentPassword, privateRootDirectory, authoringVersion);
        try
        {
            snapshot.CopyAccountSecrets(accountSecrets);
            if (domainSecrets is not null)
            {
                foreach (string account in configuration.DomainJoin.GetReferencedAccountNames())
                {
                    char[]? password = domainSecrets.GetPasswordCopy(account);
                    if (password is not null) Copy(password, value => snapshot.domainSecrets.SetPassword(account, value));
                }
            }
            return snapshot.PrepareAsync(driverFilesCopied, cancellationToken);
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    /// <summary>Generates Connect only from captured credentials and private dependency copies.</summary>
    public FoundryConnectProvisioningBundle CreateConnectProvisioningBundle(string stagingDirectoryPath, TelemetrySettings? telemetryOverride = null)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return new ConnectConfigurationGenerator().CreateProvisioningBundle(
            CreateGenerationDocument(telemetryOverride), CreateGenerationInputs(), stagingDirectoryPath, authoringVersion);
    }

    /// <summary>Retains existing Deploy protection and account validation rules while using captured session material.</summary>
    public string GenerateDeployConfigurationJson(TelemetrySettings? telemetryOverride = null, byte[]? deploymentSecretsKey = null, DeployProtectionSettings? protectionSettings = null)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (!accountSecrets.Validate(configuration.Customization.Oobe).IsValid)
        {
            throw new InvalidOperationException("OOBE local account password confirmation is invalid.");
        }
        var generator = new DeployConfigurationGenerator();
        return generator.Serialize(generator.Generate(
            CreateGenerationDocument(telemetryOverride), deploymentSecretsKey, protectionSettings, accountSecrets, domainSecrets, CreateGenerationInputs(), authoringVersion));
    }

    /// <summary>Creates fresh per-media protection using the confirmed password captured before preparation began.</summary>
    public DeploymentMediaProtectionMaterial CreateDeploymentProtectionMaterial()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return configuration.General.DeploymentProtection.IsEnabled
            ? DeploymentMediaProtectionService.CreateProtected(deploymentPassword)
            : DeploymentMediaProtectionService.CreateUnprotected();
    }

    /// <summary>
    /// Returns an owned copy of the captured password of a network cache account, or <see langword="null"/> when none was
    /// captured. Foundry OSD uses it to open shares while it publishes imported content; the caller must clear the copy.
    /// </summary>
    public char[]? GetNetworkCachePasswordCopy(string accountId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(accountId);
        return networkCachePasswords.TryGetValue(accountId, out char[]? password) ? password.ToArray() : null;
    }

    /// <summary>Clears owned password buffers and removes the private staging directory; cleanup errors are surfaced to the caller.</summary>
    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            ReleaseSourceFiles();
            accountSecrets.Dispose();
            domainSecrets.Dispose();
            Clear(deploymentPassword);
            Clear(wifiPassphrase);
            Clear(wiredCertificatePassword);
            Clear(wifiCertificatePassword);
            Clear(autopilotCertificatePassword);
            foreach (char[] value in networkCachePasswords.Values) Clear(value);
        }
        if (privateDirectory is null) return;
        try
        {
            // A failed removal keeps the lease object so a later Dispose call retries it.
            privateDirectory.Dispose();
            privateDirectory = null;
            Log.ForContext<BuildSnapshot>().Debug("Private media snapshot inputs removed.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.ForContext<BuildSnapshot>().Warning(exception, "Private media snapshot cleanup failed; the inputs remain eligible for a later cleanup.");
            throw;
        }
    }

    /// <summary>Removes abandoned private media inputs without disturbing leased builds or following redirected paths.</summary>
    public static void CleanupAbandoned(string privateRootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateRootDirectory);
        try
        {
            LogAbandonedCleanup(PrivateDirectory.CleanupAbandoned(privateRootDirectory, MaximumPrivateEntries));
        }
        catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
        {
            Log.ForContext<BuildSnapshot>().Debug("Abandoned media input cleanup deferred while another snapshot owns the cleanup lock.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.ForContext<BuildSnapshot>().Warning(exception, "Abandoned media input cleanup could not complete.");
        }
    }

    private static void LogAbandonedCleanup(PrivateDirectoryCleanupResult result)
    {
        if (result.Removed > 0)
            Log.ForContext<BuildSnapshot>().Information("Abandoned private media snapshot inputs removed. DirectoryCount={DirectoryCount}", result.Removed);
        if (result.Failed > 0)
            Log.ForContext<BuildSnapshot>().Warning("Abandoned private media snapshot inputs could not be removed and will be retried later. DirectoryCount={DirectoryCount}", result.Failed);
    }

    private async Task<BuildSnapshot> PrepareAsync(IProgress<int>? driverFilesCopied, CancellationToken cancellationToken)
    {
        Log.ForContext<BuildSnapshot>().Information("Media snapshot preparation started.");
        try
        {
            await Task.Run(() => PrepareFiles(driverFilesCopied, cancellationToken), cancellationToken).ConfigureAwait(false);
            Log.ForContext<BuildSnapshot>().Information("Media snapshot preparation completed.");
            return this;
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
                Log.ForContext<BuildSnapshot>().Information("Media snapshot preparation canceled.");
            else if (exception is CustomDriverSizeLimitException sizeException)
                Log.ForContext<BuildSnapshot>().Warning(
                    "Media snapshot preparation rejected custom driver sources. ActualBytes={ActualBytes}, LimitBytes={LimitBytes}",
                    sizeException.ActualBytes, sizeException.LimitBytes);
            else
                Log.ForContext<BuildSnapshot>().Error(exception, "Media snapshot preparation failed.");
            try
            {
                Dispose();
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
                // Dispose records the cleanup failure; preserve the preparation error for the caller.
            }
            throw;
        }
    }

    private void PrepareFiles(IProgress<int>? driverFilesCopied, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FoundryConfigurationDocument sources = SelectActiveSources(configuration);
        if (!string.IsNullOrWhiteSpace(sources.General.CustomDriverDirectoryPath))
            ValidateDriverSources(sources.General.CustomDriverDirectoryPath, cancellationToken);
        privateDirectory = PrivateDirectory.CreateLeased(privateRootDirectory, MaximumPrivateEntries);
        LogAbandonedCleanup(privateDirectory.AbandonedCleanup);
        configuration = sources;
        try
        {
            CaptureSources(sources, privateDirectory.Path);
        }
        finally
        {
            ReleaseSourceFiles();
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(configuration.General.CustomDriverDirectoryPath))
        {
            string destination = Path.Combine(privateDirectory.Path, "Drivers");
            CopyDrivers(configuration.General.CustomDriverDirectoryPath, destination, driverFilesCopied, cancellationToken);
            configuration = configuration with { General = configuration.General with { CustomDriverDirectoryPath = destination } };
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Copies every active referenced authoring file (802.1X and Wi-Fi profiles and certificates, the Autopilot boot media
    /// certificate and answer files) from the authoring file library under a generated name in <paramref name="directory"/>;
    /// generation reads only these copies.
    /// </summary>
    /// <remarks>
    /// Each file is bounded to <see cref="MaximumSourceBytes"/> and all of them to <see cref="MaximumTotalSourceBytes"/>; the
    /// library verifies every read against the reference hash, and an answer file must also match its recorded digest. A missing
    /// required file or an unavailable library file fails with <see cref="DependencyUnavailableMessage"/>; a size or hash violation
    /// fails too, and the caller removes the private directory. Bytes are cleared after each copy; a file referenced twice is
    /// copied once.
    /// </remarks>
    private void CaptureSources(FoundryConfigurationDocument sources, string directory)
    {
        long total = 0;
        NetworkSettings network = sources.Network;
        bool wiredEnabled = network.Dot1x.IsEnabled;
        bool wifiEnabled = network.WifiProvisioned && network.Wifi.IsEnabled;
        bool wifiProfileEnabled = wifiEnabled && network.Wifi.HasEnterpriseProfile;
        bool hardwareHashEnabled = sources.Autopilot.IsEnabled && sources.Autopilot.ProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload;
        Copy(network.Dot1x.ProfileTemplate, required: wiredEnabled);
        Copy(network.Dot1x.Certificate, required: wiredEnabled && network.Dot1x.RequiresCertificate);
        Copy(network.Wifi.EnterpriseProfileTemplate, required: wifiProfileEnabled);
        Copy(network.Wifi.Certificate, required: wifiEnabled && network.Wifi.RequiresCertificate);
        Copy(sources.Autopilot.HardwareHashUpload.BootMediaCertificate.Pfx, required: hardwareHashEnabled);
        foreach (UnattendFileSettings file in sources.Unattend.Files)
        {
            if (file.File is not null && !string.Equals(file.File.ContentHash, file.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(UnattendFileService.ContentMismatchMessage);
            Copy(file.File, required: true);
        }

        void Copy(AuthoringFileReference? reference, bool required)
        {
            if (reference is null)
            {
                if (required) throw new FileNotFoundException(DependencyUnavailableMessage);
                return;
            }
            if (sourceCopies.ContainsKey(reference.ContentHash)) return;
            if (files is null || !files.IsAvailable(reference)) throw new FileNotFoundException(DependencyUnavailableMessage);
            if (reference.Length > MaximumSourceBytes || total + reference.Length > MaximumTotalSourceBytes)
                throw new InvalidDataException("Media source files exceed the supported size.");

            byte[]? content = null;
            try
            {
                try
                {
                    content = files.ReadVerified(reference);
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    throw new FileNotFoundException(DependencyUnavailableMessage, exception);
                }
                total += content.Length;

                string target = Path.Combine(directory, Guid.NewGuid().ToString("N") + SafeExtension(reference.FileName));
                using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                output.Write(content);
                sourceCopies.Add(reference.ContentHash, target);
            }
            finally
            {
                if (content is not null) CryptographicOperations.ZeroMemory(content);
            }
        }
    }

    private void ReleaseSourceFiles()
    {
        sourceFilesLease?.Dispose();
        sourceFilesLease = null;
    }

    private static string SafeExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".xml" => ".xml",
        ".pfx" => ".pfx",
        ".p12" => ".p12",
        ".cer" => ".cer",
        ".crt" => ".crt",
        ".json" => ".json",
        _ => ".bin"
    };

    private static void ValidateDriverSources(string source, CancellationToken cancellationToken)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(source));
        long totalBytes = 0;
        int entries = 0;
        while (pending.TryPop(out DirectoryInfo? current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Custom driver snapshots do not follow reparse points.");
            foreach (FileSystemInfo entry in current.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entries > MaximumDriverEntries || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Custom driver snapshots exceed the entry limit or contain a reparse point.");
                if (entry is DirectoryInfo directory)
                {
                    pending.Push(directory);
                    continue;
                }
                totalBytes += ((FileInfo)entry).Length;
                if (totalBytes > MaximumDriverBytes)
                    throw new CustomDriverSizeLimitException(totalBytes, MaximumDriverBytes);
            }
        }
    }

    private static void CopyDrivers(string source, string destination, IProgress<int>? filesCopied, CancellationToken cancellationToken)
    {
        var pending = new Stack<(DirectoryInfo Source, string Destination)>();
        pending.Push((new DirectoryInfo(source), destination));
        long totalBytes = 0;
        int entries = 0;
        int copiedFiles = 0;
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (pending.TryPop(out (DirectoryInfo Source, string Destination) current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((current.Source.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Custom driver snapshots do not follow reparse points.");
                }
                Directory.CreateDirectory(current.Destination);
                foreach (FileSystemInfo entry in current.Source.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++entries > MaximumDriverEntries || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException("Custom driver snapshots exceed the entry limit or contain a reparse point.");
                    }
                    string target = Path.Combine(current.Destination, entry.Name);
                    if (entry is DirectoryInfo directory)
                    {
                        pending.Push((directory, target));
                        continue;
                    }
                    using FileStream input = new(entry.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    // Source contents can change after metadata validation; enforce the bound again while copying.
                    if (totalBytes + input.Length > MaximumDriverBytes)
                    {
                        throw new CustomDriverSizeLimitException(totalBytes + input.Length, MaximumDriverBytes);
                    }
                    using FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    int count;
                    while ((count = input.Read(buffer)) != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        totalBytes += count;
                        if (totalBytes > MaximumDriverBytes)
                            throw new CustomDriverSizeLimitException(totalBytes, MaximumDriverBytes);
                        output.Write(buffer, 0, count);
                    }
                    filesCopied?.Report(++copiedFiles);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private void CopyAccountSecrets(OobeAccountSecretState source)
    {
        Copy(source.GetAdministratorPasswordCopy(), accountSecrets.SetAdministratorPassword);
        Copy(source.GetAdministratorConfirmationCopy(), accountSecrets.SetAdministratorConfirmation);
        foreach (OobeAdditionalAccountSettings account in configuration.Customization.Oobe.AdditionalAccounts)
        {
            Copy(source.GetAdditionalAccountPasswordCopy(account.Id), value => accountSecrets.SetAdditionalAccountPassword(account.Id, value));
            Copy(source.GetAdditionalAccountConfirmationCopy(account.Id), value => accountSecrets.SetAdditionalAccountConfirmation(account.Id, value));
        }
    }

    private delegate void SetSecret(ReadOnlySpan<char> value);

    private static void Copy(char[] buffer, SetSecret setter)
    {
        try
        {
            setter(buffer);
        }
        finally
        {
            Clear(buffer);
        }
    }

    private FoundryConfigurationDocument CreateGenerationDocument(TelemetrySettings? telemetryOverride) =>
        configuration with { Telemetry = telemetryOverride ?? configuration.Telemetry };

    /// <summary>Gives generators the captured passwords and only the private copies, never the authoring sources.</summary>
    private MediaInputs CreateGenerationInputs() => new()
    {
        ResolveFile = ResolveCapturedFile,
        Network = new NetworkSecrets
        {
            WifiPassphrase = AsString(wifiPassphrase),
            WiredCertificatePfxPassword = AsString(wiredCertificatePassword),
            WifiCertificatePfxPassword = AsString(wifiCertificatePassword)
        },
        AutopilotPfxPassword = AsString(autopilotCertificatePassword),
        NetworkCachePasswords = networkCachePasswords.ToDictionary(pair => pair.Key, pair => new string(pair.Value), StringComparer.Ordinal),
        NetworkCacheIndex = networkCacheIndex
    };

    /// <summary>Returns the private copy captured for <paramref name="reference"/>, or <see langword="null"/> when it was not captured.</summary>
    internal string? ResolveCapturedFile(AuthoringFileReference reference) => sourceCopies.GetValueOrDefault(reference.ContentHash);

    private static FoundryConfigurationDocument CloneConfiguration(FoundryConfigurationDocument source)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(source, ConfigurationJsonDefaults.SerializerOptions);
        try
        {
            return JsonSerializer.Deserialize<FoundryConfigurationDocument>(bytes, ConfigurationJsonDefaults.SerializerOptions)!;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static FoundryConfigurationDocument SelectActiveSources(FoundryConfigurationDocument source)
    {
        bool wifiEnabled = source.Network.WifiProvisioned && source.Network.Wifi.IsEnabled;
        bool hardwareHashEnabled = source.Autopilot.IsEnabled && source.Autopilot.ProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload;
        return source with
        {
            Network = source.Network with
            {
                Dot1x = source.Network.Dot1x.IsEnabled ? source.Network.Dot1x : source.Network.Dot1x with { ProfileTemplate = null, Certificate = null },
                Wifi = source.Network.Wifi with
                {
                    EnterpriseProfileTemplate = wifiEnabled && source.Network.Wifi.HasEnterpriseProfile ? source.Network.Wifi.EnterpriseProfileTemplate : null,
                    Certificate = wifiEnabled ? source.Network.Wifi.Certificate : null
                }
            },
            Unattend = source.Unattend.IsEnabled ? source.Unattend : source.Unattend with { Files = [] },
            Autopilot = source.Autopilot with
            {
                HardwareHashUpload = source.Autopilot.HardwareHashUpload with
                {
                    BootMediaCertificate = hardwareHashEnabled ? source.Autopilot.HardwareHashUpload.BootMediaCertificate : new()
                }
            }
        };
    }

    private static string? AsString(char[]? value) => value is null ? null : new string(value);
    private static void Clear(char[]? value)
    {
        if (value is not null)
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(value.AsSpan()));
        }
    }
}
