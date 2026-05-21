using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Core.Tests.Autopilot;

public sealed class AutopilotPfxCertificateValidatorTests
{
    [Fact]
    public void Validate_WhenPfxThumbprintMatches_ReturnsCertificateMetadata()
    {
        using X509Certificate2 certificate = CreateCertificate();
        byte[] pfxBytes = certificate.Export(X509ContentType.Pfx, "correct-password");

        AutopilotPfxValidationResult result = AutopilotPfxCertificateValidator.Validate(
            pfxBytes,
            "correct-password",
            certificate.Thumbprint);

        Assert.True(result.IsValid);
        Assert.Equal(certificate.Thumbprint, result.Thumbprint);
        Assert.Equal(certificate.NotAfter.ToUniversalTime(), result.ExpiresOnUtc?.UtcDateTime);
    }

    [Fact]
    public void Validate_WhenExpectedThumbprintIsNotProvided_ReturnsCertificateMetadata()
    {
        using X509Certificate2 certificate = CreateCertificate();
        byte[] pfxBytes = certificate.Export(X509ContentType.Pfx, "correct-password");

        AutopilotPfxValidationResult result = AutopilotPfxCertificateValidator.Validate(
            pfxBytes,
            "correct-password");

        Assert.True(result.IsValid);
        Assert.Equal(certificate.Thumbprint, result.Thumbprint);
        Assert.Equal(certificate.NotAfter.ToUniversalTime(), result.ExpiresOnUtc?.UtcDateTime);
    }

    [Fact]
    public void Validate_WhenPasswordIsEmpty_ReturnsPasswordRequired()
    {
        using X509Certificate2 certificate = CreateCertificate();
        byte[] pfxBytes = certificate.Export(X509ContentType.Pfx, string.Empty);

        AutopilotPfxValidationResult result = AutopilotPfxCertificateValidator.Validate(
            pfxBytes,
            string.Empty,
            certificate.Thumbprint);

        Assert.False(result.IsValid);
        Assert.Equal(AutopilotPfxValidationCode.PasswordRequired, result.Code);
    }

    [Fact]
    public void Validate_WhenThumbprintDoesNotMatch_ReturnsThumbprintMismatch()
    {
        using X509Certificate2 certificate = CreateCertificate();
        byte[] pfxBytes = certificate.Export(X509ContentType.Pfx, "correct-password");

        AutopilotPfxValidationResult result = AutopilotPfxCertificateValidator.Validate(
            pfxBytes,
            "correct-password",
            "ABCDEF123456");

        Assert.False(result.IsValid);
        Assert.Equal(AutopilotPfxValidationCode.ThumbprintMismatch, result.Code);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Foundry OSD Autopilot Registration",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(12));
    }
}
