using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using RED.Helper;
using TXT = RED.RedGetText;

namespace RED
{
    public enum DeleteModes
    {
        RecycleBin = 0,
        RecycleBinShowErrors = 1,
        RecycleBinWithQuestion = 2,
        Direct = 3,
        Simulate = 4,
        MoveToFolder = 5
    }

    [Serializable]
    public class REDPermissionDeniedException : Exception
    {
        public REDPermissionDeniedException()
        { }

        public REDPermissionDeniedException(string message) : base(message) { }

        public REDPermissionDeniedException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// A collection of (generic) system functions
    ///
    /// Exception handling should be made by the caller
    /// </summary>
    public class SystemFunctions
    {
        public static string MoveToFolderTarget { get; set; }

        #region Handle-based reparse safety (CVE-2022-21658 mitigation)

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(IntPtr hFile, out BY_HANDLE_FILE_INFORMATION info);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(IntPtr hFile, int fileInformationClass, ref FILE_DISPOSITION_INFO info, uint dwBufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint dwFileAttributes;
            public long ftCreationTime;
            public long ftLastAccessTime;
            public long ftLastWriteTime;
            public uint dwVolumeSerialNumber;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint nNumberOfLinks;
            public uint nFileIndexHigh;
            public uint nFileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_DISPOSITION_INFO
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool DeleteFile;
        }

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const uint FILE_READ_ATTRIBUTES = 0x0080;
        private const uint DELETE = 0x00010000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;
        private const int FileDispositionInfo = 4;

        private static void VerifyNotReparsePoint(string path)
        {
            IntPtr hDir = CreateFileW(path, FILE_READ_ATTRIBUTES,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);

            if (hDir == INVALID_HANDLE_VALUE)
            {
                int err = Marshal.GetLastWin32Error();
                throw new IOException(TXT.Translate("Cannot open directory for verification (error {0}): {1}", err, RedAssist.DQuote(path)));
            }

            try
            {
                BY_HANDLE_FILE_INFORMATION info;
                if (!GetFileInformationByHandle(hDir, out info))
                    throw new IOException(TXT.Translate("Cannot read directory attributes: {0}", RedAssist.DQuote(path)));

                if ((info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
                    throw new REDPermissionDeniedException(TXT.Translate("Refused to delete directory because it is a reparse point (junction, symlink, or mount point): {0}", RedAssist.DQuote(path)));
            }
            finally
            {
                CloseHandle(hDir);
            }
        }

        private static void DirectDeleteByHandle(string path)
        {
            IntPtr hDir = CreateFileW(path, DELETE | FILE_READ_ATTRIBUTES,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);

            if (hDir == INVALID_HANDLE_VALUE)
            {
                int err = Marshal.GetLastWin32Error();
                throw new IOException(TXT.Translate("Cannot open directory for deletion (error {0}): {1}", err, RedAssist.DQuote(path)));
            }

            try
            {
                BY_HANDLE_FILE_INFORMATION info;
                if (!GetFileInformationByHandle(hDir, out info))
                    throw new IOException(TXT.Translate("Cannot read directory attributes: {0}", RedAssist.DQuote(path)));

                if ((info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
                    throw new REDPermissionDeniedException(TXT.Translate("Refused to delete directory because it is a reparse point (junction, symlink, or mount point): {0}", RedAssist.DQuote(path)));

                var disposition = new FILE_DISPOSITION_INFO { DeleteFile = true };
                if (!SetFileInformationByHandle(hDir, FileDispositionInfo, ref disposition, (uint)Marshal.SizeOf(disposition)))
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new IOException(TXT.Translate("Failed to delete directory by handle (error {0}): {1}", err, RedAssist.DQuote(path)));
                }
            }
            finally
            {
                CloseHandle(hDir);
            }
        }

        #endregion Handle-based reparse safety

        public static void ManuallyDeleteDirectory(string path, DeleteModes deleteMode)
        {
            if (deleteMode == DeleteModes.Simulate)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new Exception(TXT.Translate("Could not delete directory because the path was empty"));
            }

            //TODO: Add FileIOPermission code?

            FileSystem.DeleteDirectory(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        }

        public static bool IsDirLocked(string path)
        {
            try
            {
                var acl = System.IO.Directory.GetAccessControl(path);
                var rules = acl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);

                foreach (System.Security.AccessControl.FileSystemAccessRule rule in rules)
                {
                    if (rule.AccessControlType == System.Security.AccessControl.AccessControlType.Deny &&
                        (rule.FileSystemRights & System.Security.AccessControl.FileSystemRights.Delete) != 0)
                    {
                        if (identity.User.Equals(rule.IdentityReference) ||
                            principal.IsInRole((System.Security.Principal.SecurityIdentifier)rule.IdentityReference))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        public static bool IsFileLocked(FileInfo file)
        {
            try
            {
                using (file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch //(IOException)
            {
                // Could not open file -> probably we have no
                // write access to the file
                return true;
            }
        }

        public static void SecureDeleteDirectory(string path, DeleteModes deleteMode)
        {
            if (deleteMode == DeleteModes.Simulate)
            {
                return;
            }

            // Handle-based atomic reparse check (CVE-2022-21658 mitigation)
            VerifyNotReparsePoint(path);

            if (deleteMode == DeleteModes.Direct)
            {
                var di = new DirectoryInfo(path);
                if (di.Attributes.HasFlag(FileAttributes.ReadOnly))
                    di.Attributes &= ~FileAttributes.ReadOnly;
                if (di.GetFiles().Length == 0 && di.GetDirectories().Length == 0)
                {
                    DirectDeleteByHandle(path);
                }
                else if (di.GetFiles().Length == 0)
                {
                    di.Delete(true);
                }
                else
                {
                    throw new Exception(TXT.Translate("Aborted deletion of the directory because it is no longer empty. This can happen if RED previously failed to delete an empty (trash) file: {0}", RedAssist.DQuote(path)));
                }
                return;
            }

            if (deleteMode == DeleteModes.MoveToFolder)
            {
                if (string.IsNullOrWhiteSpace(MoveToFolderTarget))
                    throw new Exception(TXT.Translate("Move-to-folder target has not been set"));
                string relativePath = new DirectoryInfo(path).Name;
                string destPath = Path.Combine(MoveToFolderTarget, relativePath);
                int counter = 1;
                while (Directory.Exists(destPath))
                {
                    destPath = Path.Combine(MoveToFolderTarget, relativePath + "_" + counter++);
                }
                Directory.Move(path, destPath);
                return;
            }

            // Last security check before recycle-bin deletion — allow empty subdirectories
            // (they are part of the same wholly-empty subtree being processed parent-first)
            if (Directory.GetFiles(path).Length == 0)
            {
                if (deleteMode == DeleteModes.RecycleBin || deleteMode == DeleteModes.RecycleBinShowErrors)
                {
                    FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                }
                else if (deleteMode == DeleteModes.RecycleBinWithQuestion)
                {
                    FileSystem.DeleteDirectory(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                }
                else
                {
                    throw new Exception(RedGetText.Words.ErrorUnknownDeleteMode(deleteMode));
                }
            }
            else
            {
                throw new Exception(TXT.Translate("Aborted deletion of the directory because it is no longer empty. This can happen if RED previously failed to delete an empty (trash) file: {0}", RedAssist.DQuote(path)));
            }
        }

        public static void SecureDeleteFile(FileInfo file, DeleteModes deleteMode)
        {
            if (deleteMode == DeleteModes.Simulate)
            {
                return;
            }

            if (deleteMode == DeleteModes.MoveToFolder)
            {
                return;
            }

            if (deleteMode == DeleteModes.RecycleBin || deleteMode == DeleteModes.RecycleBinShowErrors)
            {
                FileSystem.DeleteFile(file.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            }
            else if (deleteMode == DeleteModes.RecycleBinWithQuestion)
            {
                FileSystem.DeleteFile(file.FullName, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            }
            else if (deleteMode == DeleteModes.Direct)
            {
                // Was used for testing the error handling:
                // if (SystemFunctions.random.NextDouble() > 0.5) throw new Exception("Test error");
                if (file.IsReadOnly) file.IsReadOnly = false;
                file.Delete();
            }
            else
            {
                throw new Exception(RedGetText.Words.ErrorUnknownDeleteMode(deleteMode));
            }
        }

        public static string ChooseDirectoryDialog(string path)
        {
            FolderBrowserDialog folderDialog = new FolderBrowserDialog();

            folderDialog.Description = TXT.Translate("Please select the directory that you want to be cleaned");
            folderDialog.ShowNewFolderButton = false;

            if (!string.IsNullOrWhiteSpace(path))
            {
                DirectoryInfo dir = new DirectoryInfo(path);

                if (dir.Exists)
                {
                    folderDialog.SelectedPath = path;
                }
            }

            if (folderDialog.ShowDialog() == DialogResult.OK)
            {
                path = folderDialog.SelectedPath;
            }

            folderDialog.Dispose();

            return path;
        }

        /// <summary>
        /// Opens a folder
        /// </summary>
        public static void OpenDirectoryWithExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string exe = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot"), "explorer.exe");

            Process.Start(exe, string.Format("/e,{0}", RedAssist.DQuote(path)));
        }

        public static bool IsAdmin()
        {
            WindowsPrincipal principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        // HKCU Registry Keys. No Admin Rights Required
        private const string regKeyNameShell = @"Software\Classes\Directory\shell";
        private const string regKeyNameBgShl = @"Software\Classes\Directory\Background\shell";
#if DEBUG
        private const string regSubkeyRed = @"RED++DBUG";
#else
        private const string regSubkeyRed = @"RED++";
#endif
        // HKCR Legacy Registry keys (used by orginal RED)
        private const string regLegacyKeyName = @"Folder\shell";
        private const string regLegacySubKeyRed = "Remove Empty Dirs";

        /// <summary>
        /// Check for the registry key
        /// </summary>
        /// <returns>0 = No, 1 = HKCR (Legacy), 2 = HKCU</returns>
        public static int IsRegKeyIntegratedIntoWindowsExplorer(out string command)
        {
            int integrationMethod = 0;
            command = "";
            try
            {
                using (var reg1 = Registry.ClassesRoot.OpenSubKey(regLegacyKeyName + @"\" + regLegacySubKeyRed, writable: false))
                {
                    integrationMethod = reg1 != null ? 1 : 0;
                    command = GetExplorerIntegrationCommand(reg1);
                }

                if (integrationMethod == 0)
                {
                    using (var reg2 = Registry.CurrentUser.OpenSubKey(regKeyNameShell + @"\" + regSubkeyRed, writable: false))
                    {
                        integrationMethod = reg2 != null ? 2 : 0;
                        command = GetExplorerIntegrationCommand(reg2);
                    }
                }
            }
            catch
            {
                integrationMethod = -1;
            }
            return integrationMethod;
        }

        private static string GetExplorerIntegrationCommand(RegistryKey reg)
        {
            string command = "";
            try
            {
                if (reg != null)
                {
                    if (reg.SubKeyCount > 0)
                    {
                        using (var regcmd = reg.OpenSubKey("command"))
                        {
                            if (regcmd != null)
                            {
                                command = regcmd.GetValue("").ToString();
                            }
                        }
                    }
                }
            }
            catch { }
            return command;
        }

        internal static void ExplorerIntegrationAdd(bool autosearch)
        {
            try
            {
                // Integrate with HKCU method
                ExplorerIntegrationAdd(regKeyNameShell + @"\" + regSubkeyRed, "%1", autosearch);
                ExplorerIntegrationAdd(regKeyNameBgShl + @"\" + regSubkeyRed, "%V", autosearch);
            }
            catch (Exception ex)
            {
                UiAssist.MsgBoxError(TXT.Words.Error + RedGetText.CrLf1 + TXT.Translate("Could not change registry settings:") + RedGetText.CrLf2 + ex.ToString());
            }
        }

        internal static void ExplorerIntegrationRemove()
        {
            try
            {
                string command;
                int isIntegrated = SystemFunctions.IsRegKeyIntegratedIntoWindowsExplorer(out command);
                switch (isIntegrated)
                {
                    case 1:
                        // Integrated with Legacy HKCR method. Requires Admin rights
                        ExplorerIntegrationRemove(Registry.ClassesRoot, regLegacyKeyName, regLegacySubKeyRed);
                        break;
                    case 2:
                        // Integrated with HKCU method
                        ExplorerIntegrationRemove(Registry.CurrentUser, regKeyNameShell, regSubkeyRed);
                        ExplorerIntegrationRemove(Registry.CurrentUser, regKeyNameBgShl, regSubkeyRed);
                        break;
                    default:
                        // Not integrated or unable to determine
                        break;
                }
            }
            catch (Exception ex)
            {
                UiAssist.MsgBoxError(TXT.Words.Error + RedGetText.CrLf1 + TXT.Translate("Could not change registry settings:") + RedGetText.CrLf2 + ex.ToString());
            }
        }

        private static void ExplorerIntegrationAdd(string keyname, string placeholder, bool autosearch)
        {
            using (var reg = Registry.CurrentUser.CreateSubKey(keyname))
            {
                if (reg != null)
                {
                    string muiverb = TXT.Red.Title;
#if DEBUG
                    muiverb += " (DBUG)";
#endif
                    reg.SetValue("MUIVerb", muiverb);
                    reg.SetValue("Icon", Application.ExecutablePath + ",0");
                    //reg.SetValue("Position", "Bottom");
                    using (RegistryKey regcmd = Registry.CurrentUser.CreateSubKey(keyname + @"\command"))
                    {
                        if (regcmd != null)
                        {
                            //string cmd = string.Format("{0} {1} {2}", Application.ExecutablePath, autosearch ? "-autosearch" : "", RedAssist.DQuote(placeholder)));
                            StringBuilder cmd = new StringBuilder();
                            cmd.Append(RedAssist.DQuote(Application.ExecutablePath));
                            cmd.Append(autosearch ? " -autosearch " : " "); //space before and after
                            cmd.Append(RedAssist.DQuote(placeholder));
                            regcmd.SetValue("", cmd.ToString());
                        }
                    }
                }
            }
        }

        private static void ExplorerIntegrationRemove(RegistryKey regKey, string keyname, string subkeyname)
        {
            using (var reg = regKey.OpenSubKey(keyname, writable: true))
            {
                if (reg != null)
                {
                    reg.DeleteSubKeyTree(subkeyname, throwOnMissingSubKey: false);
                }
            }
        }
    }
}