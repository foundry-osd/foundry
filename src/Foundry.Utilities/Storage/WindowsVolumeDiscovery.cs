// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security;

namespace Foundry.Utilities.Storage;

/// <summary>
/// Discovers volumes through the Windows drive information APIs.
/// </summary>
public sealed class WindowsVolumeDiscovery : IVolumeDiscovery
{
    private readonly Func<IEnumerable<IWindowsDriveInfo>> _driveSource;

    /// <summary>
    /// Initializes a new instance that reads the drives visible to the current process.
    /// </summary>
    public WindowsVolumeDiscovery()
        : this(GetWindowsDrives)
    {
    }

    internal WindowsVolumeDiscovery(Func<IEnumerable<IWindowsDriveInfo>> driveSource)
    {
        ArgumentNullException.ThrowIfNull(driveSource);
        _driveSource = driveSource;
    }

    /// <inheritdoc />
    public IReadOnlyList<VolumeInfo> GetVolumes()
    {
        var volumes = new List<VolumeInfo>();
        foreach (IWindowsDriveInfo drive in _driveSource())
        {
            if (TryReadVolume(drive, out VolumeInfo? volume))
            {
                volumes.Add(volume);
            }
        }

        return volumes;
    }

    private static bool TryReadVolume(
        IWindowsDriveInfo drive,
        [NotNullWhen(true)] out VolumeInfo? volume)
    {
        string rootPath;
        try
        {
            rootPath = drive.RootPath;
        }
        catch (Exception exception) when (IsDriveAccessException(exception))
        {
            volume = null;
            return false;
        }

        DriveType driveType = ReadOrDefault(() => drive.DriveType, DriveType.Unknown);
        bool isReady = drive.IsReady;
        string volumeLabel = isReady
            ? ReadOrDefault(() => drive.VolumeLabel, string.Empty)
            : string.Empty;
        long availableFreeSpace = isReady
            ? ReadOrDefault(() => drive.AvailableFreeSpace, 0L)
            : 0L;

        volume = new VolumeInfo(rootPath, volumeLabel, driveType, isReady, availableFreeSpace);
        return true;
    }

    private static T ReadOrDefault<T>(Func<T> readValue, T defaultValue)
    {
        try
        {
            return readValue();
        }
        catch (Exception exception) when (IsDriveAccessException(exception))
        {
            return defaultValue;
        }
    }

    private static bool IsDriveAccessException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or SecurityException;
    }

    private static IEnumerable<IWindowsDriveInfo> GetWindowsDrives()
    {
        return DriveInfo.GetDrives().Select(static drive => new WindowsDriveInfo(drive));
    }

    private sealed class WindowsDriveInfo(DriveInfo drive) : IWindowsDriveInfo
    {
        public string RootPath => drive.RootDirectory.FullName;

        public string VolumeLabel => drive.VolumeLabel;

        public DriveType DriveType => drive.DriveType;

        public bool IsReady => drive.IsReady;

        public long AvailableFreeSpace => drive.AvailableFreeSpace;
    }
}

internal interface IWindowsDriveInfo
{
    string RootPath { get; }

    string VolumeLabel { get; }

    DriveType DriveType { get; }

    bool IsReady { get; }

    long AvailableFreeSpace { get; }
}
