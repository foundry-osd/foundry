// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Download;

public readonly record struct DownloadProgress(long BytesDownloaded, long? TotalBytes);
