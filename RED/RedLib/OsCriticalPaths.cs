using System;
using System.Collections.Generic;
using System.IO;

namespace RED
{
    /// <summary>
    /// Non-overridable protection for OS-critical empty directories.
    /// These paths are intentionally empty on a stock Windows install and
    /// must never be deleted regardless of user filter configuration.
    /// Rooted to the system drive so <c>D:\Projects\inetpub</c> is not blocked.
    /// </summary>
    internal static class OsCriticalPaths
    {
        private static readonly HashSet<string> ProtectedPaths;

        static OsCriticalPaths()
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(winDir))
                winDir = @"C:\Windows";
            string root = Path.GetPathRoot(winDir);
            if (string.IsNullOrEmpty(root))
                root = @"C:\";
            string drive = root.TrimEnd(Path.DirectorySeparatorChar);

            ProtectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                drive + @"\inetpub",      // CVE-2025-21204 security mitigation
                drive + @"\PerfLogs",     // Windows Performance Monitor
                drive + @"\Config.Msi",   // MSI rollback (privilege-escalation vector if recreated with weak DACLs)
                drive + @"\Recovery"      // Windows Recovery Environment
            };
        }

        internal static bool IsProtected(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return false;
            return ProtectedPaths.Contains(fullPath.TrimEnd(Path.DirectorySeparatorChar));
        }

        internal static bool IsProtected(DirectoryInfo dir)
        {
            if (dir == null) return false;
            return IsProtected(dir.FullName);
        }
    }
}
