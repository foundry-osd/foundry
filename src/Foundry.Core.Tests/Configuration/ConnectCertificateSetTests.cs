// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConnectCertificateSetTests
{
    [Fact]
    public void From_ClientCertificateAndRoots_KeepsTheNonBlankRootsInOrder()
    {
        var password = new SecretEnvelope { KeyId = "deployment" };

        ConnectCertificateSet set = ConnectCertificateSet.From(new ConnectWifiSettings
        {
            ClientCertificate = new ConnectClientCertificateSettings { Path = @"Network\client.pfx.encrypted", IsContentProtected = true, PasswordSecret = password },
            TrustedRootCertificates = [@"Network\root1.cer", " ", @"Network\root2.crt"]
        });

        Assert.Equal(@"Network\client.pfx.encrypted", set.ClientCertificatePath);
        Assert.True(set.IsClientCertificateContentProtected);
        Assert.Same(password, set.ClientCertificatePasswordSecret);
        Assert.Equal([@"Network\root1.cer", @"Network\root2.crt"], set.TrustedRootCertificatePaths);
    }

    [Fact]
    public void From_ClientCertificateWithABlankPath_IsAbsent()
    {
        ConnectCertificateSet set = ConnectCertificateSet.From(new ConnectDot1xSettings
        {
            ClientCertificate = new ConnectClientCertificateSettings { Path = " ", IsContentProtected = true, PasswordSecret = new SecretEnvelope() },
            TrustedRootCertificates = [@"Network\Certificates\Wired\root.cer"]
        });

        Assert.Null(set.ClientCertificatePath);
        Assert.False(set.IsClientCertificateContentProtected);
        Assert.Null(set.ClientCertificatePasswordSecret);
        Assert.Equal([@"Network\Certificates\Wired\root.cer"], set.TrustedRootCertificatePaths);
    }

    [Fact]
    public void From_NoCertificate_IsEmpty()
    {
        Assert.Equal(ConnectCertificateSet.Empty, ConnectCertificateSet.From(new ConnectDot1xSettings()));
        Assert.Equal(ConnectCertificateSet.Empty, ConnectCertificateSet.From(new ConnectWifiSettings { TrustedRootCertificates = [" "] }));
    }
}
