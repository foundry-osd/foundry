// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;
using Foundry.Wpf.Controls;
using Foundry.Wpf.Localization;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class EthernetSectionViewModelTests
{
    private readonly ResourceLocalizationService _localization = TestLocalization.Create();

    private static EthernetState Ethernet(bool link, IpConfigurationKind ip, Dot1xState? dot1x = null) =>
        new([new EthernetInterfaceState("eth0", "Ethernet 2", link, ip, ip is IpConfigurationKind.None ? null : "192.0.2.10", ip is IpConfigurationKind.Dhcp ? "192.0.2.1" : null)],
            dot1x ?? Dot1xState.NotConfigured);

    [Theory]
    [InlineData(false, IpConfigurationKind.None, "Ethernet.NoActiveLink", "Ethernet.CheckCable", false)]
    [InlineData(true, IpConfigurationKind.None, "Ethernet.WaitingConfiguration", "Ethernet.WaitingDhcp", false)]
    [InlineData(true, IpConfigurationKind.Apipa, "Ethernet.WaitingConfiguration", "Ethernet.ApipaAddress", false)]
    [InlineData(true, IpConfigurationKind.Dhcp, "Common.Connected", "Ethernet.DhcpLeaseDetected", true)]
    [InlineData(true, IpConfigurationKind.Static, "Common.Connected", "Ethernet.StaticConfigurationDetected", true)]
    public void Apply_DescribesLinkAndAddress(bool link, IpConfigurationKind ip, string statusKey, string secondaryKey, bool connected)
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(Ethernet(link, ip), isChecking: false);

        Assert.True(viewModel.ShowCard);
        Assert.Equal(_localization.GetString(statusKey), viewModel.StatusText);
        Assert.Equal(_localization.GetString(secondaryKey), viewModel.SecondaryStatusText);
        Assert.Equal(connected, viewModel.IsConnected);
        Assert.Equal("Ethernet 2", viewModel.AdapterName);
    }

    [Fact]
    public void Apply_ShowsAddressAndGatewayOrUnavailable()
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(Ethernet(true, IpConfigurationKind.Dhcp), isChecking: false);
        Assert.Equal("192.0.2.10", viewModel.IpAddress);
        Assert.Equal("192.0.2.1", viewModel.Gateway);

        viewModel.Apply(Ethernet(false, IpConfigurationKind.None), isChecking: false);
        Assert.Equal(_localization.GetString("Common.Unavailable"), viewModel.IpAddress);
        Assert.Equal(_localization.GetString("Common.Unavailable"), viewModel.Gateway);
    }

    [Theory]
    [InlineData(true, IpConfigurationKind.Dhcp, "\uE839")]
    [InlineData(true, IpConfigurationKind.Apipa, "\uEB56")]
    [InlineData(false, IpConfigurationKind.None, "\uEB56")]
    public void Glyph_FollowsTheUsableAddress(bool link, IpConfigurationKind ip, string glyph)
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(Ethernet(link, ip), isChecking: false);

        Assert.Equal(glyph, viewModel.Glyph);
    }

    [Fact]
    public void NoAdapter_UsesTheErrorGlyphAndTheNoAdapterTexts()
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(EthernetState.Empty, isChecking: false);

        Assert.Equal("\uEB55", viewModel.Glyph);
        Assert.Equal(_localization.GetString("Ethernet.NoAdapterDetected"), viewModel.NoAdapterText);
        Assert.Equal(_localization.GetString("Ethernet.NoAdapterDetected"), viewModel.StatusText);
        Assert.Equal(string.Empty, viewModel.SecondaryStatusText);
        Assert.Equal(_localization.GetString("Common.Unavailable"), viewModel.AdapterName);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void NoAdapterNotice_IsHiddenWhileChecking(bool isChecking, bool expectedNotice)
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(EthernetState.Empty, isChecking);

        Assert.False(viewModel.ShowCard);
        Assert.Equal(expectedNotice, viewModel.ShowNoAdapterNotice);
    }

    [Theory]
    [InlineData(Dot1xAuthenticationStatus.ProfileApplied, Dot1xFailureReason.None, "Ethernet.Dot1xProfileApplied", null, false, InfoBarSeverity.Informational)]
    [InlineData(Dot1xAuthenticationStatus.Authenticating, Dot1xFailureReason.None, "Ethernet.Dot1xAuthenticating", null, false, InfoBarSeverity.Informational)]
    [InlineData(Dot1xAuthenticationStatus.Authenticated, Dot1xFailureReason.None, "Ethernet.Dot1xAuthenticated", null, false, InfoBarSeverity.Informational)]
    [InlineData(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.AuthenticationFailed, "Ethernet.Dot1xFailed", "Ethernet.Dot1xReasonAuthenticationFailed", true, InfoBarSeverity.Error)]
    [InlineData(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.ProfileImportFailed, "Ethernet.Dot1xFailed", "Ethernet.Dot1xReasonProfileImportFailed", true, InfoBarSeverity.Error)]
    [InlineData(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.ProfileTemplateMissing, "Ethernet.Dot1xFailed", "Ethernet.Dot1xReasonProfileTemplateMissing", true, InfoBarSeverity.Error)]
    [InlineData(Dot1xAuthenticationStatus.Unavailable, Dot1xFailureReason.SecretUnavailable, "Ethernet.Dot1xUnavailable", "Ethernet.Dot1xReasonSecretUnavailable", true, InfoBarSeverity.Warning)]
    public void Dot1xRow_ShowsStatusReasonAndNotice(Dot1xAuthenticationStatus status, Dot1xFailureReason reason, string statusKey, string? reasonKey, bool notice, InfoBarSeverity severity)
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(Ethernet(true, IpConfigurationKind.None, new Dot1xState(status, reason)), isChecking: false);

        Assert.True(viewModel.HasDot1x);
        Assert.Equal(_localization.GetString(statusKey), viewModel.Dot1xStatusText);
        Assert.Equal(reasonKey is null ? string.Empty : _localization.GetString(reasonKey), viewModel.Dot1xReasonText);
        Assert.Equal(notice, viewModel.ShowDot1xNotice);
        if (notice)
        {
            Assert.Equal(severity, viewModel.Dot1xNoticeSeverity);
        }
    }

    [Fact]
    public void Dot1xRow_IsHiddenWhenNotConfigured()
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(Ethernet(true, IpConfigurationKind.Dhcp), isChecking: false);

        Assert.False(viewModel.HasDot1x);
        Assert.False(viewModel.ShowDot1xNotice);
    }

    [Fact]
    public void Details_CollapseOnConnectAndKeepTheTechniciansChoice()
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(Ethernet(true, IpConfigurationKind.None), isChecking: false);
        Assert.True(viewModel.IsDetailsExpanded);
        viewModel.Apply(Ethernet(true, IpConfigurationKind.Dhcp), isChecking: false);
        Assert.False(viewModel.IsDetailsExpanded);

        viewModel.IsDetailsExpanded = true;
        viewModel.Apply(Ethernet(true, IpConfigurationKind.Dhcp), isChecking: false);
        Assert.True(viewModel.IsDetailsExpanded);

        viewModel.Apply(Ethernet(false, IpConfigurationKind.None), isChecking: false);
        viewModel.IsDetailsExpanded = false;
        viewModel.Apply(Ethernet(false, IpConfigurationKind.None), isChecking: false);
        Assert.False(viewModel.IsDetailsExpanded);
    }

    [Fact]
    public void Details_FirstApplyWhileConnectedStartsCollapsed()
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(Ethernet(true, IpConfigurationKind.Static), isChecking: false);

        Assert.False(viewModel.IsDetailsExpanded);
    }

    [Fact]
    public void Details_LostAddressExpandsAgain()
    {
        var viewModel = new EthernetSectionViewModel(_localization);
        viewModel.Apply(Ethernet(true, IpConfigurationKind.Dhcp), isChecking: false);

        viewModel.Apply(Ethernet(false, IpConfigurationKind.None), isChecking: false);

        Assert.True(viewModel.IsDetailsExpanded);
    }

    [Fact]
    public void RefreshTexts_RaisesTheTextsWithoutTouchingExpansion()
    {
        var viewModel = new EthernetSectionViewModel(_localization);
        viewModel.Apply(Ethernet(true, IpConfigurationKind.Dhcp), isChecking: false);
        viewModel.IsDetailsExpanded = true;
        List<string?> raised = [];
        viewModel.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        viewModel.RefreshTexts();

        Assert.Contains(nameof(EthernetSectionViewModel.StatusText), raised);
        Assert.Contains(nameof(EthernetSectionViewModel.Dot1xStatusText), raised);
        Assert.True(viewModel.IsDetailsExpanded);
    }
}
