// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows.Media;
using Foundry.AutopilotInteractive.Services.Authentication;
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
    public void SignIn_ReplacesCodeAndQrTogether_OnRenewal()
    {
        using SignInViewModel viewModel = new(TestLocalization.Create(), _clock, new ImmediateUiDispatcher(), NullLogger<SignInFlow>.Instance);
        List<(string Code, ImageSource? Qr, bool Visible)> snapshots = [];
        viewModel.PropertyChanged += (_, _) => snapshots.Add((viewModel.UserCode, viewModel.QrCodeImage, viewModel.IsCodeVisible));

        viewModel.Start(new SimulatedDeviceCodeClient(DebugScenario.CodeRenewal, _clock), infoBannerKey: null);
        Assert.True(viewModel.IsCodeVisible);
        string firstCode = viewModel.UserCode;
        ImageSource? firstQr = viewModel.QrCodeImage;

        _clock.Advance(SimulatedDeviceCodeClient.ShortCodeLifetime);

        Assert.True(viewModel.IsCodeVisible);
        string secondCode = viewModel.UserCode;
        ImageSource? secondQr = viewModel.QrCodeImage;
        Assert.NotEqual(firstCode, secondCode);
        Assert.NotNull(secondQr);
        Assert.NotSame(firstQr, secondQr);

        int lastFirst = snapshots.FindLastIndex(snapshot => snapshot.Visible && snapshot.Code == firstCode);
        int firstSecond = snapshots.FindIndex(snapshot => snapshot.Visible && snapshot.Code == secondCode);
        Assert.Contains(snapshots[lastFirst..firstSecond], snapshot => !snapshot.Visible);
        Assert.All(
            snapshots.Where(snapshot => snapshot.Visible),
            snapshot => Assert.True(
                (snapshot.Code == firstCode && ReferenceEquals(snapshot.Qr, firstQr))
                || (snapshot.Code == secondCode && ReferenceEquals(snapshot.Qr, secondQr))));
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
