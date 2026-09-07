// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Tests.TestUtilities;

internal static class AutopilotOfflineProfileTestData
{
    public const string ValidJson = """
        {
          "CloudAssignedTenantId": "11111111-1111-4111-8111-111111111111",
          "CloudAssignedForcedEnrollment": 1,
          "Version": 2049,
          "Comment_File": "Profile Synthetic",
          "CloudAssignedAadServerData": "{\"ZeroTouchConfig\":{\"CloudAssignedTenantUpn\":\"\",\"ForcedEnrollment\":1,\"CloudAssignedTenantDomain\":\"example.onmicrosoft.com\"}}",
          "CloudAssignedTenantDomain": "example.onmicrosoft.com",
          "CloudAssignedDomainJoinMethod": 0,
          "CloudAssignedOobeConfig": 28,
          "ZtdCorrelationId": "22222222-2222-4222-8222-222222222222"
        }
        """;
}
