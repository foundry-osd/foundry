using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Foundry.Deploy.Services.Autopilot;

internal static class AutopilotDiagnosticsDirectory
{
    public static void CreateRestricted(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var directory = new DirectoryInfo(path);
            var security = new DirectorySecurity();
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            SecurityIdentifier? currentUser = WindowsIdentity.GetCurrent().User;
            const InheritanceFlags inheritanceFlags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                system,
                FileSystemRights.FullControl,
                inheritanceFlags,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                administrators,
                FileSystemRights.FullControl,
                inheritanceFlags,
                PropagationFlags.None,
                AccessControlType.Allow));
            if (currentUser is not null)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    inheritanceFlags,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            directory.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
        {
            // Diagnostics must not block deployment when ACL hardening is unavailable in WinPE.
        }
    }
}
