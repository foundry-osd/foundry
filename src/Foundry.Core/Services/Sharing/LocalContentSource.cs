// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Packages;
using Foundry.Utilities.IO;

namespace Foundry.Core.Services.Sharing;

/// <summary>
/// Opens the plaintext of publication content from the local libraries of the reference PC, for
/// <see cref="PublicationWriter"/>. Every stream it returns is seekable and read-only.
/// </summary>
/// <remarks>
/// <para>Plaintext layouts, which <see cref="ContentSync"/> reads back on followers:</para>
/// <list type="bullet">
/// <item><see cref="SharedContentKind.File"/>: the stored file bytes, read and hash-verified under an <see cref="AuthoringFileLibrary"/>
/// lease; they hash to the content hash.</item>
/// <item><see cref="SharedContentKind.Package"/> and <see cref="SharedContentKind.DriverSet"/>: the deterministic archive of
/// <see cref="ContentPackageArchive.WriteAsync"/> (read under a library lease), written to an owner-only temporary file
/// (<see cref="PrivateDirectory"/>) that is deleted when the stream is disposed.</item>
/// <item><see cref="SharedContentKind.Image"/>: a 12-byte header (ASCII <c>FIMG</c>, int32 little-endian version 1, int32 little-endian
/// record length), the metadata record (the UTF-8 JSON <see cref="CustomImageReference"/> of the library entry, with the indexes the
/// library reads from the image), then the <c>image.wim</c> bytes, streamed from the library under a verified lease, never copied.</item>
/// </list>
/// <para>
/// <see cref="PackageContent.Length"/> is the library length of the content for every kind: the file length, the sum of the library
/// file lengths of a package or driver set, the WIM length of an image (<see cref="CustomImageReference.Length"/>). The plaintext of
/// packages, driver sets and images is longer (archive entries and manifest, image header and record). The length only sizes progress
/// and the download size, so it never has to match the plaintext.
/// </para>
/// </remarks>
public sealed class LocalContentSource : IContentSource
{
    /// <summary>Largest image metadata record; the same bound as the image library index.</summary>
    internal const int MaximumImageRecordBytes = 8 * 1024 * 1024;

    private const int ImageHeaderVersion = 1;
    private const int ImageHeaderSize = 12;
    private const int BufferSize = 128 * 1024;
    private const int ImageBufferSize = 1024 * 1024;
    private static ReadOnlySpan<byte> ImageMagic => "FIMG"u8;

    private readonly AuthoringFileLibrary files;
    private readonly ContentPackageLibraryService packages;
    private readonly CustomDriverSetLibrary driverSets;
    private readonly CustomImageLibraryService images;
    private readonly string temporaryRoot;

    /// <summary>Creates a source over the four libraries of this PC.</summary>
    /// <param name="files">Authoring file library.</param>
    /// <param name="packages">Post-installation package library.</param>
    /// <param name="driverSets">Custom driver set library.</param>
    /// <param name="images">Custom image library.</param>
    /// <param name="temporaryRoot">
    /// Dedicated root of the owner-only directories that hold package archives while they are encrypted; defaults to
    /// <c>%TEMP%\Foundry\ContentSource</c>.
    /// </param>
    public LocalContentSource(AuthoringFileLibrary files, ContentPackageLibraryService packages, CustomDriverSetLibrary driverSets,
        CustomImageLibraryService images, string? temporaryRoot = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(driverSets);
        ArgumentNullException.ThrowIfNull(images);
        this.files = files;
        this.packages = packages;
        this.driverSets = driverSets;
        this.images = images;
        this.temporaryRoot = temporaryRoot ?? Path.Combine(Path.GetTempPath(), "Foundry", "ContentSource");
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">The content is missing or damaged in its library.</exception>
    /// <exception cref="IOException">The library or the temporary directory cannot be read or written.</exception>
    public Task<Stream> OpenAsync(PackageContent content, CancellationToken ct) => OpenAsync(content, null, ct);

    /// <summary>
    /// Returns the bytes read before the stream is ready: the WIM hash verification of an image (its length), the verification and
    /// archive copy of a package or driver set (twice its length), nothing for a file, which is small and read in memory.
    /// </summary>
    public long PreparationLength(PackageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        long length = Math.Max(content.Length, 0);
        return content.Kind switch
        {
            SharedContentKind.Image => length,
            SharedContentKind.Package or SharedContentKind.DriverSet => 2 * length,
            _ => 0
        };
    }

    /// <inheritdoc cref="IContentSource.OpenAsync(PackageContent, IProgress{long}?, CancellationToken)" />
    /// <exception cref="InvalidDataException">The content is missing or damaged in its library.</exception>
    /// <exception cref="IOException">The library or the temporary directory cannot be read or written.</exception>
    public async Task<Stream> OpenAsync(PackageContent content, IProgress<long>? preparation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        ct.ThrowIfCancellationRequested();
        return content.Kind switch
        {
            SharedContentKind.File => OpenFile(content),
            SharedContentKind.Package => await OpenArchiveAsync(packages, content.Hash, preparation, ct).ConfigureAwait(false),
            SharedContentKind.DriverSet => await OpenArchiveAsync(driverSets.Library, content.Hash, preparation, ct).ConfigureAwait(false),
            SharedContentKind.Image => await OpenImageAsync(content.Hash, preparation, ct).ConfigureAwait(false),
            _ => throw new ArgumentException("The content kind is not supported.", nameof(content))
        };
    }

    /// <summary>
    /// Reads the header and metadata record at the current position of an image plaintext and leaves the stream at the first byte of
    /// the WIM.
    /// </summary>
    /// <exception cref="InvalidDataException">The header or record is malformed.</exception>
    internal static async Task<CustomImageReference> ReadImageRecordAsync(Stream plaintext, CancellationToken cancellationToken)
    {
        if (plaintext.Length - plaintext.Position < ImageHeaderSize)
        {
            throw InvalidImageContent();
        }

        byte[] header = new byte[ImageHeaderSize];
        await plaintext.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        if (!header.AsSpan(0, 4).SequenceEqual(ImageMagic) || BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4)) != ImageHeaderVersion
            || length is <= 0 or > MaximumImageRecordBytes || length > plaintext.Length - plaintext.Position)
        {
            throw InvalidImageContent();
        }

        byte[] record = new byte[length];
        await plaintext.ReadExactlyAsync(record, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<CustomImageReference>(record, ConfigurationJsonDefaults.SerializerOptions) ?? throw InvalidImageContent();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("LocalContentSource.InvalidImageContent", exception);
        }
    }

    /// <summary>Returns the header and metadata record that precede the WIM bytes of an image plaintext.</summary>
    internal static byte[] CreateImageHeader(CustomImageReference record)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(record, ConfigurationJsonDefaults.SerializerOptions);
        if (json.Length > MaximumImageRecordBytes)
        {
            throw InvalidImageContent();
        }

        byte[] header = new byte[ImageHeaderSize + json.Length];
        ImageMagic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), ImageHeaderVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), json.Length);
        json.CopyTo(header, ImageHeaderSize);
        return header;
    }

    private MemoryStream OpenFile(PackageContent content)
    {
        using IDisposable lease = files.Lease([content.Hash]);
        byte[] bytes = files.ReadVerified(new AuthoringFileReference { ContentHash = content.Hash, FileName = content.Name, Length = content.Length });
        return new MemoryStream(bytes, writable: false);
    }

    private async Task<Stream> OpenArchiveAsync(ContentPackageLibraryService library, string hash, IProgress<long>? preparation,
        CancellationToken cancellationToken)
    {
        PrivateDirectoryLease directory = PrivateDirectory.CreateLeased(temporaryRoot);
        FileStream? archive = null;
        try
        {
            archive = new FileStream(Path.Combine(directory.Path, "archive.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            await ContentPackageArchive.WriteAsync(library, hash, archive, cancellationToken, preparation).ConfigureAwait(false);
            archive.Position = 0;
            return new ConcatenatedStream([archive], directory);
        }
        catch
        {
            if (archive is not null)
            {
                await archive.DisposeAsync().ConfigureAwait(false);
            }

            ConcatenatedStream.DisposeOwner(directory);
            throw;
        }
    }

    private async Task<Stream> OpenImageAsync(string hash, IProgress<long>? preparation, CancellationToken cancellationToken)
    {
        IReadOnlyList<CustomImageReference> entries = await images.ListAsync(cancellationToken).ConfigureAwait(false);
        CustomImageReference entry = entries.FirstOrDefault(image => image.ContentHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("LocalContentSource.ImageMissing");
        CustomImageSourceLease lease = await images.AcquireAsync(entry, cancellationToken, preparation).ConfigureAwait(false);
        FileStream? image = null;
        try
        {
            // The lease denies writes and deletion, so this second read handle sees the bytes the lease verified.
            image = new FileStream(lease.ImagePath, FileMode.Open, FileAccess.Read, FileShare.Read, ImageBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return new ConcatenatedStream([new MemoryStream(CreateImageHeader(lease.Reference), writable: false), image], lease);
        }
        catch
        {
            if (image is not null)
            {
                await image.DisposeAsync().ConfigureAwait(false);
            }

            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static InvalidDataException InvalidImageContent() => new("LocalContentSource.InvalidImageContent");

    /// <summary>
    /// Read-only seekable view of seekable parts read one after the other, whose lengths are fixed when it is created. Disposing it
    /// disposes the parts, then the lease that keeps their source in place.
    /// </summary>
    private sealed class ConcatenatedStream : Stream
    {
        private readonly Stream[] parts;
        private readonly long[] starts;
        private readonly long length;
        private readonly IDisposable owner;
        private long position;
        private bool disposed;

        public ConcatenatedStream(Stream[] parts, IDisposable owner)
        {
            this.parts = parts;
            this.owner = owner;
            starts = new long[parts.Length];
            for (int index = 0; index < parts.Length; index++)
            {
                starts[index] = length;
                length += parts[index].Length;
            }
        }

        public override bool CanRead => !disposed;

        public override bool CanSeek => !disposed;

        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return length;
            }
        }

        public override long Position
        {
            get => position;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                position = value;
            }
        }

        /// <summary>Disposes a lease without letting a cleanup failure replace the caller's outcome; abandoned cleanup removes leftovers.</summary>
        public static void DisposeOwner(IDisposable owner)
        {
            try
            {
                owner.Dispose();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The next PrivateDirectory lease removes the abandoned directory.
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (!TryLocate(buffer.Length, out Stream? part, out int count))
            {
                return 0;
            }

            return Advance(part.Read(buffer[..count]));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!TryLocate(buffer.Length, out Stream? part, out int count))
            {
                return 0;
            }

            return Advance(await part.ReadAsync(buffer[..count], cancellationToken).ConfigureAwait(false));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            return position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                foreach (Stream part in parts)
                {
                    part.Dispose();
                }

                DisposeOwner(owner);
            }

            base.Dispose(disposing);
        }

        /// <summary>Finds the part that holds <see cref="position"/> and seeks it there; false at the end of the stream.</summary>
        private bool TryLocate(int requested, [NotNullWhen(true)] out Stream? part, out int count)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            part = null;
            count = 0;
            if (requested == 0 || position >= length)
            {
                return false;
            }

            // The last part starting at or before the position is the non-empty one that holds it.
            long current = position;
            int index = Array.FindLastIndex(starts, start => start <= current);
            part = parts[index];
            long offset = position - starts[index];
            part.Position = offset;
            count = (int)Math.Min(requested, part.Length - offset);
            return true;
        }

        private int Advance(int read)
        {
            if (read == 0)
            {
                // A part ended before the length fixed at creation: its source changed.
                throw new InvalidDataException("LocalContentSource.ContentChanged");
            }

            position += read;
            return read;
        }
    }
}
