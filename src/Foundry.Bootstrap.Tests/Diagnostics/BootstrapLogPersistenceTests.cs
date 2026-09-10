// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Bootstrap.Diagnostics;
using Serilog;
using Xunit;

namespace Foundry.Bootstrap.Tests.Diagnostics;

public sealed class BootstrapLogPersistenceTests
{
    [Fact]
    public async Task CopiesOpenSessionLogsAndReplacesExistingSnapshot()
    {
        string root = Path.Combine(Path.GetTempPath(), "FoundryBootstrapTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source");
            string destination = Path.Combine(root, "cache");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            string path = Path.Combine(source, "FoundryBootstrap.log");
            await File.WriteAllTextAsync(path, "current session", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(destination, "FoundryBootstrap.log"), "old", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(source, "other.log"), "unrelated", TestContext.Current.CancellationToken);
            using var openLog = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            using var logger = new LoggerConfiguration().CreateLogger();

            await new BootstrapLogPersistence(source, destination, logger).PersistAsync(TestContext.Current.CancellationToken);

            Assert.Equal("current session", await File.ReadAllTextAsync(Path.Combine(destination, "FoundryBootstrap.log"), TestContext.Current.CancellationToken));
            Assert.False(File.Exists(Path.Combine(destination, "other.log")));
            Assert.Empty(Directory.GetFiles(destination, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DestinationFailureIsBestEffort()
    {
        string file = Path.GetTempFileName();
        try
        {
            using var logger = new LoggerConfiguration().CreateLogger();
            await new BootstrapLogPersistence(Path.GetDirectoryName(file)!, file, logger)
                .PersistAsync(TestContext.Current.CancellationToken);
            Assert.True(File.Exists(file));
        }
        finally { File.Delete(file); }
    }
}
