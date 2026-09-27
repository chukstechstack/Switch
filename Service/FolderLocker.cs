using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Switch.Service
{
    public static class FolderLocker
    {
        private static string LocksDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Switch", "locks");

        // Save original ACL and apply restrictive ACL that allows only SYSTEM and Administrators
        public static bool TryLockFolder(string folderPath, string id, out string error)
        {
            error = string.Empty;
            try
            {
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    error = "Folder not found.";
                    return false;
                }

                Directory.CreateDirectory(LocksDir);
                var backupFile = Path.Combine(LocksDir, GetSafeFileName(id) + ".sddl");

                try
                {
                    var dirInfo = new DirectoryInfo(folderPath);
                    var current = dirInfo.GetAccessControl(AccessControlSections.All);
                    var sddl = current.GetSecurityDescriptorSddlForm(AccessControlSections.All);
                    File.WriteAllText(backupFile, sddl);
                }
                catch (Exception ex)
                {
                    // Continue even if backup fails, but warn
                    error = "Failed to backup ACL: " + ex.Message;
                    // still attempt to apply lock
                }

                // Build restrictive ACL
                var ds = new DirectorySecurity();

                // Allow Administrators and SYSTEM full control
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

                var ruleAdmins = new FileSystemAccessRule(admins, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);
                var ruleSystem = new FileSystemAccessRule(system, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);

                ds.AddAccessRule(ruleAdmins);
                ds.AddAccessRule(ruleSystem);

                // Apply restrictive ACL
                var di = new DirectoryInfo(folderPath);
                di.SetAccessControl(ds);

                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryRestoreFolder(string folderPath, string id, out string error)
        {
            error = string.Empty;
            try
            {
                var backupFile = Path.Combine(LocksDir, GetSafeFileName(id) + ".sddl");
                if (!File.Exists(backupFile))
                {
                    error = "Backup ACL not found";
                    return false;
                }

                var sddl = File.ReadAllText(backupFile);
                var ds = new DirectorySecurity();
                ds.SetSecurityDescriptorSddlForm(sddl);
                var di = new DirectoryInfo(folderPath);
                di.SetAccessControl(ds);

                try
                {
                    File.Delete(backupFile);
                }
                catch { }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string GetSafeFileName(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
            return id;
        }
    }
}
