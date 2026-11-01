// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows.Markup;
using System.Windows.Media;
using Foundry.AutopilotInteractive.Services.Authentication;
using Foundry.AutopilotInteractive.Services.QrCode;
using Foundry.AutopilotInteractive.Services.Simulation;
using Foundry.AutopilotInteractive.Tests.Support;
using Foundry.AutopilotInteractive.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.AutopilotInteractive.Tests.ViewModels;

public sealed class SignInViewModelTests
{
    private readonly ManualTimeProvider _clock = new();

    public SignInViewModelTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
    }

    [Fact]
    public void SignIn_KeepsTheStaticQrCode_WhenTheCodeIsRenewed()
    {
        using SignInViewModel viewModel = new(TestLocalization.Create(), _clock, new ImmediateUiDispatcher(), NullLogger<SignInFlow>.Instance);
        List<(ImageSource? Qr, bool AddressVisible, bool CodeVisible)> snapshots = [];
        viewModel.PropertyChanged += (_, _) => snapshots.Add((viewModel.QrCodeImage, viewModel.IsAddressVisible, viewModel.IsCodeVisible));

        viewModel.Start(new SimulatedDeviceCodeClient(DebugScenario.CodeRenewal, _clock), infoBannerKey: null);
        string firstCode = viewModel.UserCode;
        ImageSource? firstQr = viewModel.QrCodeImage;
        Assert.NotNull(firstQr);
        snapshots.Clear();

        _clock.Advance(SimulatedDeviceCodeClient.ShortCodeLifetime);

        Assert.NotEqual(firstCode, viewModel.UserCode);
        Assert.True(viewModel.IsCodeVisible);
        Assert.Same(firstQr, viewModel.QrCodeImage);
        Assert.Contains(snapshots, snapshot => !snapshot.CodeVisible);
        Assert.All(snapshots, snapshot =>
        {
            Assert.True(snapshot.AddressVisible);
            Assert.Same(firstQr, snapshot.Qr);
        });
        viewModel.Stop();
    }

    [Fact]
    public void SignIn_EncodesExactlyTheVerificationAddress_InTheQrCode()
    {
        using SignInViewModel viewModel = new(TestLocalization.Create(), _clock, new ImmediateUiDispatcher(), NullLogger<SignInFlow>.Instance);

        viewModel.Start(new SimulatedDeviceCodeClient(DebugScenario.SignInCode, _clock), infoBannerKey: null);

        Assert.Equal("https://microsoft.com/devicelogin", viewModel.VerificationUriText);
        Assert.Equal(
            XamlWriter.Save(QrCodeImageFactory.Create("https://microsoft.com/devicelogin")),
            XamlWriter.Save(viewModel.QrCodeImage));
        viewModel.Stop();
    }

    [Theory]
    [InlineData("access_denied", "SignIn.Error.Declined")]
    [InlineData("authorization_declined", "SignIn.Error.Declined")]
    [InlineData("invalid_response", "SignIn.Error.Generic")]
    [InlineData("unexpected_error", "SignIn.Error.Generic")]
    [InlineData("http_400", "SignIn.Error.Generic")]
    [InlineData("secure_connection", "SignIn.Error.SecureConnection")]
    [InlineData("invalid_grant", "SignIn.Error.Blocked")]
    [InlineData("unauthorized_client", "SignIn.Error.Blocked")]
    public void ErrorResourceKey_SeparatesDeclinedBlockedAndGenericFailures(string errorCode, string expectedKey)
    {
        Assert.Equal(expectedKey, SignInViewModel.ErrorResourceKey(errorCode));
    }
}
