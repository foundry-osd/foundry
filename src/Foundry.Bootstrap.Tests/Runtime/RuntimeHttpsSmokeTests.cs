// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Foundry.Bootstrap.Runtime;
using Serilog;
using Xunit;

namespace Foundry.Bootstrap.Tests.Runtime;

public sealed class RuntimeHttpsSmokeTests
{
    [Theory(Explicit = true)]
    [InlineData("Foundry.Connect")]
    [InlineData("Foundry.Deploy")]
    public async Task PublishedRuntimeIsAuthenticatedAndPreparedWithoutExecutingIt(string application)
    {
        string root = Directory.CreateTempSubdirectory("FoundryRuntimeHttps-").FullName;
        try
        {
            string boot = Path.Combine(root, "Boot");
            string cache = Path.Combine(root, "Cache", "Runtime");
            string rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var logger = new LoggerConfiguration().CreateLogger();
            var progress = new List<RuntimeDownloadProgress>();
            var resolver = new RuntimeResolver(boot, cache, rid, client, logger, progress.Add, getEnvironmentVariable: _ => null);

            string executable = await resolver.ResolveAsync(application, false, TestContext.Current.CancellationToken);

            Assert.StartsWith(Path.Combine(boot, "Execution") + Path.DirectorySeparatorChar, executable);
            Assert.False(string.IsNullOrWhiteSpace(FileVersionInfo.GetVersionInfo(executable).FileVersion));
            Assert.True(File.Exists(Path.Combine(cache, application, rid, "current.zip")));
            RuntimeDownloadProgress verification = progress.Last(item => item.Phase == RuntimeProgressPhase.Verification);
            Assert.Equal(verification.TotalBytes, verification.BytesReceived);
            RuntimeDownloadProgress extraction = progress.Last(item => item.Phase == RuntimeProgressPhase.Extraction);
            Assert.Equal(extraction.TotalBytes, extraction.BytesReceived);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
