// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Tests.Runtime;

public sealed class DeploymentKeyTransferTests
{
    [Fact]
    public void TryTake_SucceedsOnceWithTheExactKeyAndZeroesTheBuffer()
    {
        byte[] key = RandomNumberGenerator.GetBytes(DeploymentKeyTransfer.KeyLength);
        using DeploymentKeyTransfer transfer = DeploymentKeyTransfer.FromKey(key);
        byte[] first = new byte[DeploymentKeyTransfer.KeyLength];
        byte[] second = new byte[DeploymentKeyTransfer.KeyLength];

        Assert.True(transfer.TryTake(first));
        Assert.Equal(key, first);
        Assert.True(transfer.IsCleared);
        Assert.False(transfer.TryTake(second));
        Assert.All(second, static value => Assert.Equal(0, value));
    }

    [Fact]
    public void FromKey_CopiesTheKey()
    {
        byte[] key = RandomNumberGenerator.GetBytes(DeploymentKeyTransfer.KeyLength);
        byte[] expected = key.ToArray();
        using DeploymentKeyTransfer transfer = DeploymentKeyTransfer.FromKey(key);
        CryptographicOperations.ZeroMemory(key);
        byte[] taken = new byte[DeploymentKeyTransfer.KeyLength];

        Assert.True(transfer.TryTake(taken));
        Assert.Equal(expected, taken);
    }

    [Fact]
    public void Dispose_ZeroesAnUntakenKeyAndPreventsTheTake()
    {
        DeploymentKeyTransfer transfer = DeploymentKeyTransfer.FromKey(RandomNumberGenerator.GetBytes(DeploymentKeyTransfer.KeyLength));

        transfer.Dispose();

        Assert.True(transfer.IsCleared);
        Assert.False(transfer.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    }

    [Fact]
    public void FromKey_RejectsAKeyOfTheWrongLength()
    {
        Assert.Throws<ArgumentException>(() => DeploymentKeyTransfer.FromKey(new byte[16]));
    }

    [Fact]
    public void TryTake_RejectsADestinationOfTheWrongLength()
    {
        using DeploymentKeyTransfer transfer = DeploymentKeyTransfer.FromKey(new byte[DeploymentKeyTransfer.KeyLength]);

        Assert.Throws<ArgumentException>(() => transfer.TryTake(new byte[16]));
        Assert.True(transfer.TryTake(new byte[DeploymentKeyTransfer.KeyLength]));
    }
}
