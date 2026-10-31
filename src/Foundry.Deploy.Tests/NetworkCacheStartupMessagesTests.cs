// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Services.NetworkCache;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Localization;
using Foundry.Localization;
using Foundry.Wpf.Localization;

namespace Foundry.Deploy.Tests;

public sealed class NetworkCacheStartupMessagesTests
{
    private static readonly ILocalizationService English = new FixedCultureLocalization("en-US");

    [Theory]
    [InlineData(NetworkCacheStartupFailureKind.NoScope, false,
        "No IP scope contains this device (10.20.5.14). In Foundry OSD, on the Network cache page, add a scope for this network or a 0.0.0.0/0 scope to a backup share.")]
    [InlineData(NetworkCacheStartupFailureKind.Unreachable, true,
        @"\\srv\cache is not responding. On the server, check that the share is online and that port 445 is open.")]
    [InlineData(NetworkCacheStartupFailureKind.AccessDenied, false,
        @"CONTOSO\svc-cache cannot open \\srv\cache. In Foundry OSD, on the Network cache page, check the account password; on the server, check its permissions.")]
    [InlineData(NetworkCacheStartupFailureKind.NotPublished, true,
        @"The published content on \\srv\cache is missing or incomplete. In Foundry OSD, on the Network cache page, select Publish.")]
    [InlineData(NetworkCacheStartupFailureKind.ContentMissing, true,
        @"The published content on \\srv\cache is missing or incomplete. In Foundry OSD, on the Network cache page, select Publish.")]
    public void Format_NamesTheShareAccountOrAddressAndOffersRetryOnlyForTransientCauses(
        NetworkCacheStartupFailureKind kind, bool canRetry, string expectedMessage)
    {
        var failure = new NetworkCacheStartupFailure(kind, @"\\srv\cache", @"CONTOSO\svc-cache", "10.20.5.14");

        (string title, string message, bool retry) = NetworkCacheStartupMessages.Format(failure, English);

        Assert.Equal("Network cache not available", title);
        Assert.Equal(expectedMessage, message);
        Assert.Equal(canRetry, retry);
        Assert.Equal(kind.IsRetryable(), retry);
    }

    [Theory]
    [InlineData(NetworkCacheStartupFailureKind.NoScope,
        "No IP scope contains this device. In Foundry OSD, on the Network cache page, add a scope for this network or a 0.0.0.0/0 scope to a backup share.")]
    [InlineData(NetworkCacheStartupFailureKind.Unreachable,
        "The network cache share is not responding. On the server, check that the share is online and that port 445 is open.")]
    [InlineData(NetworkCacheStartupFailureKind.AccessDenied,
        "The network cache share refused this device. In Foundry OSD, on the Network cache page, check the account of the share and its password; on the server, check the share permissions.")]
    [InlineData(NetworkCacheStartupFailureKind.NotPublished,
        "The published content on the network cache share is missing or incomplete. In Foundry OSD, on the Network cache page, select Publish.")]
    public void Format_WithoutShareAccountOrAddress_UsesGenericWording(NetworkCacheStartupFailureKind kind, string expectedMessage)
    {
        (_, string message, _) = NetworkCacheStartupMessages.Format(new NetworkCacheStartupFailure(kind), English);

        Assert.Equal(expectedMessage, message);
    }

    [Fact]
    public void Format_AccessDeniedAfterConnection_KeepsTheMessageAndOffersRetry()
    {
        var failure = new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.AccessDenied, @"\\srv\cache", @"CONTOSO\svc-cache")
        {
            DeniedAfterConnection = true
        };

        (_, string message, bool retry) = NetworkCacheStartupMessages.Format(failure, English);

        Assert.Equal(
            @"CONTOSO\svc-cache cannot open \\srv\cache. In Foundry OSD, on the Network cache page, check the account password; on the server, check its permissions.",
            message);
        Assert.True(retry);
    }

    [Fact]
    public void Format_AccessDeniedWithoutAccount_NamesTheShareAndAsksForAnAccount()
    {
        var failure = new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.AccessDenied, @"\\srv\cache");

        (_, string message, _) = NetworkCacheStartupMessages.Format(failure, English);

        Assert.Equal(
            @"\\srv\cache refused this device. In Foundry OSD, on the Network cache page, set an account for this share; on the server, check the share permissions.",
            message);
    }

    [Fact]
    public void Format_ContentMissing_ListsFiveLocalizedItemsThenAnEllipsis()
    {
        var failure = new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.ContentMissing, @"\\srv\cache")
        {
            MissingItems =
            [
                NetworkCacheContentPresence.CustomImagesItem, "Windows 11 Sales", NetworkCacheContentPresence.CustomDriverSetsItem,
                "Install agent", "Map drives", "Rename device", "Join domain"
            ]
        };

        (_, string message, bool retry) = NetworkCacheStartupMessages.Format(failure, English);

        Assert.Equal(
            @"The published content on \\srv\cache is missing or incomplete. In Foundry OSD, on the Network cache page, select Publish."
            + Environment.NewLine + Environment.NewLine
            + "Missing: Custom Windows images, Windows 11 Sales, Driver sets, Install agent, Map drives, …",
            message);
        Assert.True(retry);
    }

    [Fact]
    public void Format_ContentMissing_WithFewItems_ListsThemWithoutEllipsis()
    {
        var failure = new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.ContentMissing, @"\\srv\cache")
        {
            MissingItems = ["Windows 11 Sales", "Install agent"]
        };

        (_, string message, _) = NetworkCacheStartupMessages.Format(failure, English);

        Assert.EndsWith(Environment.NewLine + "Missing: Windows 11 Sales, Install agent", message);
    }

    /// <summary>Resolves the Deploy resources in one culture without changing the culture of the test thread.</summary>
    private sealed class FixedCultureLocalization(string cultureName)
        : ResourceManagerLocalizationService(LocalizationText.ResourceManager, CultureInfo.GetCultureInfo(cultureName), FoundrySupportedCultures.CreateCatalog()),
            ILocalizationService;
}
