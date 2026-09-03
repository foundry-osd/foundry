// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public static class WinPeFailureKinds
{
    public const string Validation = "validation";
    public const string Tooling = "tooling";
    public const string Process = "process";
    public const string Network = "network";
    public const string FileSystem = "file_system";
    public const string Cancellation = "cancellation";
    public const string Internal = "internal";
}
