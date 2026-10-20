// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Security;
using Foundry.Core.Services.DomainJoin;

namespace Foundry.Core.Tests.DomainJoin;

public sealed class DomainCredentialCheckClassifierTests
{
    [Theory]
    [InlineData(null, 49, "8009030C: LdapErr: DSID-0C09071F, comment: AcceptSecurityContext error, data 52e, v65f4", DomainCredentialCheckOutcome.InvalidCredentials, 0x52e)]
    [InlineData(null, 49, "... data 525, v65f4", DomainCredentialCheckOutcome.InvalidCredentials, 0x525)]
    [InlineData(null, 49, "... data 775, v65f4", DomainCredentialCheckOutcome.AccountLocked, 0x775)]
    [InlineData(null, 49, "... data 532, v65f4", DomainCredentialCheckOutcome.PasswordExpired, 0x532)]
    [InlineData(null, 49, "... data 773, v65f4", DomainCredentialCheckOutcome.PasswordExpired, 0x773)]
    [InlineData(null, 49, "... data 533, v65f4", DomainCredentialCheckOutcome.AccountDisabled, 0x533)]
    [InlineData(null, 49, "... data 701, v65f4", DomainCredentialCheckOutcome.AccountDisabled, 0x701)]
    [InlineData(null, 49, "... DATA 52E, v65f4", DomainCredentialCheckOutcome.InvalidCredentials, 0x52e)]
    [InlineData(null, 49, "... data 531, v65f4", DomainCredentialCheckOutcome.Inconclusive, 0x531)]
    [InlineData(null, 49, null, DomainCredentialCheckOutcome.Inconclusive, null)]
    [InlineData(null, 49, "", DomainCredentialCheckOutcome.Inconclusive, null)]
    [InlineData(1355, null, null, DomainCredentialCheckOutcome.ControllerUnavailable, null)]
    [InlineData(0, null, null, DomainCredentialCheckOutcome.Inconclusive, null)]
    [InlineData(null, 81, null, DomainCredentialCheckOutcome.ControllerUnavailable, null)]
    [InlineData(null, 91, null, DomainCredentialCheckOutcome.ControllerUnavailable, null)]
    [InlineData(null, 85, null, DomainCredentialCheckOutcome.Inconclusive, null)]
    [InlineData(null, 8, null, DomainCredentialCheckOutcome.Inconclusive, null)]
    public void ClassifiesFailures(int? native, int? ldap, string? message, DomainCredentialCheckOutcome expected, int? dataCode)
    {
        DomainCredentialCheckResult result = DomainCredentialCheckClassifier.Classify(native, ldap, message);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(dataCode, result.DirectoryDataCode);
    }

    [Theory]
    [InlineData(DomainCredentialCheckOutcome.InvalidCredentials, true)]
    [InlineData(DomainCredentialCheckOutcome.AccountLocked, true)]
    [InlineData(DomainCredentialCheckOutcome.PasswordExpired, true)]
    [InlineData(DomainCredentialCheckOutcome.AccountDisabled, true)]
    [InlineData(DomainCredentialCheckOutcome.ControllerUnavailable, false)]
    [InlineData(DomainCredentialCheckOutcome.Inconclusive, false)]
    [InlineData(DomainCredentialCheckOutcome.Valid, false)]
    public void OnlyDirectoryRejectionsBlock(DomainCredentialCheckOutcome outcome, bool expected) =>
        Assert.Equal(expected, new DomainCredentialCheckResult(outcome).IsRejection);

    [Theory]
    [InlineData(@"CORP\joiner", "joiner", "CORP")]
    [InlineData("joiner@corp.test", "joiner@corp.test", "")]
    public void CredentialFromAccountSplitsDownLevelNames(string account, string user, string domain)
    {
        using var secret = new SecureString();

        NetworkCredential credential = DomainCredentialCheckClassifier.CreateCredential(account, secret);

        Assert.Equal(user, credential.UserName);
        Assert.Equal(domain, credential.Domain);
    }
}
