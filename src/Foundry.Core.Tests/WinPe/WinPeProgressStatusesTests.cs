// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed partial class WinPeProgressStatusesTests
{
    [Fact]
    public void UsbProvisioningScript_UsesOnlyDeclaredStatuses()
    {
        string script = WinPeEmbeddedAssetService.ReadEmbeddedText(WinPeEmbeddedAssetService.UsbProvisioningScriptResourceName);

        string[] statuses = ScriptProgressPattern().Matches(script).Select(match => match.Groups["status"].Value).ToArray();

        Assert.NotEmpty(statuses);
        Assert.All(statuses, status => Assert.Contains(status, WinPeProgressStatuses.All));
    }

    [GeneratedRegex(@"Write-FoundryUsbProgress\s+\d+\s+'(?<status>[^']+)'")]
    private static partial Regex ScriptProgressPattern();
}
