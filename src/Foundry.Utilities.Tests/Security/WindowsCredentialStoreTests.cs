// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using Foundry.Utilities.Security;

namespace Foundry.Utilities.Tests.Security;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public void CredentialDispose_ErasesOwnedSecretBuffer()
    {
        byte[] secret = [0x11, 0xFF, 0xA3];
        var credential = new WindowsCredential(secret);

        credential.Dispose();
        credential.Dispose();

        Assert.Equal(new byte[3], secret);
    }

    [Fact]
    public void WriteRead_WithBinarySecret_PreservesBytesAndUsernameAcrossStoreInstances()
    {
        var store = new WindowsCredentialStore();
        string target = CreateTarget();
        byte[] secret = [0x00, 0xFF, 0x41, 0x00, 0xFE];
        try
        {
            store.Write(target, secret, @"EXAMPLE\operator");
            using WindowsCredential? result = new WindowsCredentialStore().Read(target);

            Assert.NotNull(result);
            Assert.Equal(new byte[] { 0x00, 0xFF, 0x41, 0x00, 0xFE }, result.Secret);
            Assert.Equal(@"EXAMPLE\operator", result.UserName);
            Assert.Equal(new byte[] { 0x00, 0xFF, 0x41, 0x00, 0xFE }, secret);
        }
        finally
        {
            store.Delete(target);
        }
    }

    [Fact]
    public void Write_WithMaximumSizeAndOversizedReplacement_PreservesStoredSecret()
    {
        var store = new WindowsCredentialStore();
        string target = CreateTarget();
        byte[] secret = Enumerable.Range(0, 2560).Select(value => (byte)value).ToArray();
        try
        {
            store.Write(target, secret);

            Assert.Throws<ArgumentOutOfRangeException>(() => store.Write(target, new byte[2561]));
            using WindowsCredential? result = store.Read(target);
            Assert.NotNull(result);
            Assert.Equal(secret, result.Secret);
        }
        finally
        {
            store.Delete(target);
        }
    }

    [Fact]
    public void Delete_WithTwoTargets_RemovesOnlyRequestedCredentialAndDistinguishesEmptyFromMissing()
    {
        var store = new WindowsCredentialStore();
        string firstTarget = CreateTarget();
        string secondTarget = CreateTarget();
        try
        {
            Assert.Null(store.Read(firstTarget));
            store.Write(firstTarget, [0x19]);
            store.Write(secondTarget, []);

            store.Delete(firstTarget);
            store.Delete(firstTarget);

            Assert.Null(store.Read(firstTarget));
            using WindowsCredential? second = store.Read(secondTarget);
            Assert.NotNull(second);
            Assert.Empty(second.Secret);
        }
        finally
        {
            store.Delete(firstTarget);
            store.Delete(secondTarget);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("target\0suffix")]
    public void Operations_WithInvalidTarget_RejectBeforeNativeAccess(string target)
    {
        var store = new WindowsCredentialStore();

        Assert.Throws<ArgumentException>(() => store.Read(target));
        Assert.Throws<ArgumentException>(() => store.Write(target, []));
        Assert.Throws<ArgumentException>(() => store.Delete(target));
    }

    [Fact]
    public void Write_WhenWindowsRejectsUsername_ReportsNativeErrorAndPreservesExistingValue()
    {
        var store = new WindowsCredentialStore();
        string target = CreateTarget();
        try
        {
            store.Write(target, [0x31]);

            Win32Exception error = Assert.Throws<Win32Exception>(() =>
                store.Write(target, [0x42], new string('x', 514)));

            Assert.NotEqual(0, error.NativeErrorCode);
            using WindowsCredential? result = store.Read(target);
            Assert.NotNull(result);
            Assert.Equal(new byte[] { 0x31 }, result.Secret);
        }
        finally
        {
            store.Delete(target);
        }
    }

    private static string CreateTarget() => $"Foundry.Utilities.Tests/{Guid.NewGuid():N}";
}
