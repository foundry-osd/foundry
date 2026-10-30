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
    [InlineData(false, IpConfigurationKind.None, "Ethernet.NoActiveLink", null, false)]
    [InlineData(true, IpConfigurationKind.None, "Ethernet.WaitingConfiguration", null, false)]
    [InlineData(true, IpConfigurationKind.Apipa, "Ethernet.WaitingConfiguration", null, false)]
    [InlineData(true, IpConfigurationKind.Dhcp, "Common.Connected", "Ethernet.DhcpLeaseDetected", true)]
    [InlineData(true, IpConfigurationKind.Static, "Common.Connected", "Ethernet.StaticConfigurationDetected", true)]
    public void Apply_DescribesLinkAndAddress(bool link, IpConfigurationKind ip, string statusKey, string? configurationKey, bool connected)
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(Ethernet(link, ip), isChecking: false);

        Assert.True(viewModel.ShowCard);
        Assert.Equal(_localization.GetString(statusKey), viewModel.StatusText);
        Assert.Equal(configurationKey is null ? string.Empty : _localization.GetString(configurationKey), viewModel.ConfigurationText);
        Assert.Equal(configurationKey is not null, viewModel.HasConfiguration);
        Assert.Equal(connected, viewModel.IsConnected);
        Assert.Equal("Ethernet 2", viewModel.AdapterName);
    }

    [Theory]
    [InlineData(false, IpConfigurationKind.None, "")]
    [InlineData(true, IpConfigurationKind.None, "")]
    [InlineData(true, IpConfigurationKind.Dhcp, "192.0.2.10")]
    public void SecondaryLine_ShowsOnlyTheAddress(bool link, IpConfigurationKind ip, string expected)
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(Ethernet(link, ip), isChecking: false);

        Assert.Equal(expected, viewModel.SecondaryStatusText);
    }

    [Fact]
    public void SecondaryLine_NamesASelfAssignedAddressOnly()
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(Ethernet(true, IpConfigurationKind.Apipa), isChecking: false);

        Assert.Equal(_localization.GetString("Ethernet.ApipaAddress"), viewModel.SecondaryStatusText);
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
        Assert.Equal(_localization.GetString("Ethernet.UnavailableOnDevice"), viewModel.NoAdapterText);
        Assert.Equal(_localization.GetString("Ethernet.UnavailableOnDevice"), viewModel.StatusText);
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
    [InlineData(Dot1xAuthenticationStatus.ProfileApplied, Dot1xFailureReason.None, "Ethernet.Dot1xProfileApplied", null, InfoBarSeverity.Informational, false)]
    [InlineData(Dot1xAuthenticationStatus.Authenticating, Dot1xFailureReason.None, "Ethernet.Dot1xAuthenticating", null, InfoBarSeverity.Informational, true)]
    [InlineData(Dot1xAuthenticationStatus.Authenticated, Dot1xFailureReason.None, "Ethernet.Dot1xAuthenticated", null, InfoBarSeverity.Success, false)]
    [InlineData(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.AuthenticationFailed, "Ethernet.Dot1xFailed", "Ethernet.Dot1xReasonAuthenticationFailed", InfoBarSeverity.Error, false)]
    [InlineData(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.ProfileImportFailed, "Ethernet.Dot1xFailed", "Ethernet.Dot1xReasonProfileImportFailed", InfoBarSeverity.Error, false)]
    [InlineData(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.ProfileTemplateMissing, "Ethernet.Dot1xFailed", "Ethernet.Dot1xReasonProfileTemplateMissing", InfoBarSeverity.Error, false)]
    [InlineData(Dot1xAuthenticationStatus.Unavailable, Dot1xFailureReason.SecretUnavailable, "Ethernet.Dot1xUnavailable", "Ethernet.Dot1xReasonSecretUnavailable", InfoBarSeverity.Warning, false)]
    [InlineData(Dot1xAuthenticationStatus.Unavailable, Dot1xFailureReason.UnlockCancelled, "Ethernet.Dot1xUnavailable", "Ethernet.Dot1xReasonUnlockCancelled", InfoBarSeverity.Warning, false)]
    public void Dot1xRow_ShowsStatusSeverityAndOneReason(Dot1xAuthenticationStatus status, Dot1xFailureReason reason, string statusKey, string? reasonKey, InfoBarSeverity severity, bool authenticating)
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(Ethernet(true, IpConfigurationKind.None, new Dot1xState(status, reason)), isChecking: false);

        Assert.True(viewModel.HasDot1x);
        Assert.Equal(_localization.GetString(statusKey), viewModel.Dot1xStatusText);
        Assert.Equal(severity, viewModel.Dot1xSeverity);
        Assert.Equal(authenticating, viewModel.IsDot1xAuthenticating);
        Assert.Equal(reasonKey is not null, viewModel.HasDot1xReason);
        Assert.Equal(reasonKey is null ? string.Empty : _localization.GetString(reasonKey), viewModel.Dot1xReasonText);
    }

    [Fact]
    public void Dot1xRow_IsHiddenWhenNotConfigured()
    {
        var viewModel = new EthernetSectionViewModel(_localization);

        viewModel.Apply(Ethernet(true, IpConfigurationKind.Dhcp), isChecking: false);

        Assert.False(viewModel.HasDot1x);
        Assert.False(viewModel.HasDot1xReason);
    }

    [Fact]
    public void Details_StartCollapsedAndOnlyTheTechnicianOpensThem()
    {
        var viewModel = new EthernetSectionViewModel(_localization);
        Assert.False(viewModel.IsDetailsExpanded);

        viewModel.Apply(Ethernet(true, IpConfigurationKind.None), isChecking: false);
        Assert.False(viewModel.IsDetailsExpanded);

        viewModel.IsDetailsExpanded = true;
        viewModel.Apply(Ethernet(true, IpConfigurationKind.Dhcp), isChecking: false);
        viewModel.Apply(Ethernet(false, IpConfigurationKind.None), isChecking: false);
        Assert.True(viewModel.IsDetailsExpanded);

        viewModel.IsDetailsExpanded = false;
        viewModel.Apply(Ethernet(true, IpConfigurationKind.Dhcp), isChecking: false);
        Assert.False(viewModel.IsDetailsExpanded);
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
