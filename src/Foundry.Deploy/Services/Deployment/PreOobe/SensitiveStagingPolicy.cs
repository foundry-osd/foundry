// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Restricts first-boot code and secret inputs before writing any payload bytes.</summary>
public static class SensitiveStagingPolicy
{
    private static readonly string[] AllowedSids = ["S-1-5-18", "S-1-5-32-544"];

    /// <summary>Creates a protected directory and verifies the effective descriptor instead of trusting an existing path.</summary>
    public static void CreateRestrictedDirectory(string path)
    {
        RejectReparsePoints(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (string sid in AllowedSids)
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).Create(security);
        RejectReparsePoints(path);
        Verify(new DirectoryInfo(path).GetAccessControl(), requireProtected: true);
    }

    /// <summary>Rejects any staged file with broad, missing or redirected access rules.</summary>
    public static void VerifyRestrictedFile(string path)
    {
        RejectReparsePoints(path);
        Verify(new FileInfo(path).GetAccessControl(), requireProtected: false);
    }

    internal static void Verify(FileSystemSecurity security, bool requireProtected)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner ||
            !AllowedSids.Contains(owner.Value, StringComparer.Ordinal)) throw Failure();
        if (requireProtected && !security.AreAccessRulesProtected) throw Failure();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            string sid = rule.IdentityReference.Value;
            if (!AllowedSids.Contains(sid, StringComparer.Ordinal) || rule.AccessControlType != AccessControlType.Allow ||
                rule.FileSystemRights != FileSystemRights.FullControl || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0)
                throw Failure();
            found.Add(sid);
        }
        if (found.Count != AllowedSids.Length) throw Failure();
    }

    internal static void RejectReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw Failure();
    }

    private static UnauthorizedAccessException Failure() => new("network_secret_acl_failed");
}
