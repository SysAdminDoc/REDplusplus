using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using NotBob.Config;
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
        private static extern bool SetFileInformationByHandle(IntPtr hFile, int fileInformationClass, ref FILE_DISPOSITION_INFO_EX info, uint dwBufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice, uint dwIoControlCode,
            IntPtr lpInBuffer, uint nInBufferSize,
            byte[] lpOutBuffer, uint nOutBufferSize,
            out uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        // FILETIME fields must be 4-byte aligned (two DWORDs) — `long` would insert
        // padding after dwFileAttributes and shift every later field by 4 bytes
        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_DISPOSITION_INFO_EX
        {
            public uint Flags;
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
        private const int FileDispositionInfoEx = 21;
        private const uint FILE_DISPOSITION_FLAG_DELETE = 0x00000001;
        private const uint FILE_DISPOSITION_FLAG_POSIX_SEMANTICS = 0x00000002;
        private const uint FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE = 0x00000010;
        private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
        private const uint IO_REPARSE_TAG_CLOUD = 0x9000001A;
        private const uint IO_REPARSE_TAG_CLOUD_MASK = 0xFFFF0FFF;
        private const uint IO_REPARSE_TAG_STORAGE_SYNC_FOLDER = 0x90000027;
        private const int ERROR_INVALID_FUNCTION = 1;
        private const int ERROR_NOT_SUPPORTED = 50;
        private const int ERROR_INVALID_PARAMETER = 87;

        internal static bool IsCloudPlaceholderDirectory(string path)
        {
            uint tag;
            return TryGetReparseTag(path, out tag) && IsCloudReparseTag(tag);
        }

        internal static bool IsCloudReparseTag(uint tag)
        {
            return (tag & IO_REPARSE_TAG_CLOUD_MASK) == IO_REPARSE_TAG_CLOUD ||
                   tag == IO_REPARSE_TAG_STORAGE_SYNC_FOLDER;
        }

        private static bool TryGetReparseTag(string path, out uint tag)
        {
            tag = 0;
            IntPtr hDir = CreateFileW(FastDirectoryEnumerator.ToExtendedLengthPath(path), FILE_READ_ATTRIBUTES,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                IntPtr.Zero);

            if (hDir == INVALID_HANDLE_VALUE)
            {
                return false;
            }

            try
            {
                BY_HANDLE_FILE_INFORMATION info;
                if (!GetFileInformationByHandle(hDir, out info) ||
                    (info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0)
                {
                    return false;
                }

                byte[] buffer = new byte[16 * 1024];
                uint bytesReturned;
                if (!DeviceIoControl(hDir, FSCTL_GET_REPARSE_POINT, IntPtr.Zero, 0, buffer, (uint)buffer.Length, out bytesReturned, IntPtr.Zero) || bytesReturned < 4)
                {
                    return false;
                }

                tag = BitConverter.ToUInt32(buffer, 0);
                return true;
            }
            finally
            {
                CloseHandle(hDir);
            }
        }

        private static void VerifyNotReparsePoint(string path)
        {
            IntPtr hDir = CreateFileW(FastDirectoryEnumerator.ToExtendedLengthPath(path), FILE_READ_ATTRIBUTES,
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
            IntPtr hDir = CreateFileW(FastDirectoryEnumerator.ToExtendedLengthPath(path), DELETE | FILE_READ_ATTRIBUTES,
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

                // Tier 1: POSIX delete semantics (NTFS, Win10 1607+) — removes the name
                // immediately even with open handles, and ignores the read-only attribute.
                var dispositionEx = new FILE_DISPOSITION_INFO_EX
                {
                    Flags = FILE_DISPOSITION_FLAG_DELETE |
                            FILE_DISPOSITION_FLAG_POSIX_SEMANTICS |
                            FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE
                };
                if (SetFileInformationByHandle(hDir, FileDispositionInfoEx, ref dispositionEx, (uint)Marshal.SizeOf(dispositionEx)))
                {
                    return;
                }

                int exErr = Marshal.GetLastWin32Error();
                if (exErr != ERROR_INVALID_PARAMETER && exErr != ERROR_NOT_SUPPORTED && exErr != ERROR_INVALID_FUNCTION)
                {
                    throw new IOException(TXT.Translate("Failed to delete directory by handle (error {0}): {1}", exErr, RedAssist.DQuote(path)));
                }

                // Tier 2: legacy delete-on-close for filesystems without Ex support
                // (FAT32/exFAT, SMB shares, pre-1607 volumes)
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

            // Manual tree-node deletion is intentionally constrained to verified
            // Recycle behavior. It bypasses the batch queue, so it must run the
            // same stale-scan file-free guard and write its own undo manifest.
            List<string> verifiedPaths = GetVerifiedEmptySubtreeDirectories(path);

            RecycleBinOperation.RecycleSingle(path,
                allowConfirmation: !ConfigAssist.SilentMode,
                allowErrorUi: !ConfigAssist.SilentMode);

            var entries = new List<UndoManager.ManifestEntry>();
            foreach (string verifiedPath in verifiedPaths)
            {
                entries.Add(new UndoManager.ManifestEntry
                {
                    Path = verifiedPath,
                    Mode = DeleteModes.RecycleBinWithQuestion.ToString()
                });
            }
            UndoManager.WriteManifest(DeleteModes.RecycleBinWithQuestion.ToString(), entries, null);
        }

        /// <summary>
        /// The full pre-recycle safety gate: handle-based reparse check plus
        /// stale-scan re-verification that the subtree is still file-free.
        /// Must run on every path immediately before it is queued for recycling —
        /// the shell deletes whatever it is handed, recursively.
        /// </summary>
        internal static void VerifyRecycleSafe(string path)
        {
            GetVerifiedEmptySubtreeDirectories(path);
        }

        internal static List<string> GetVerifiedEmptySubtreeDirectories(string path)
        {
            var paths = new List<string>();
            AddVerifiedEmptySubtreeDirectory(path, paths);
            return paths;
        }

        private static void AddVerifiedEmptySubtreeDirectory(string path, List<string> paths)
        {
            VerifyNotReparsePoint(path);

            var listing = FastDirectoryEnumerator.GetFilesAndDirectories(new DirectoryInfo(path));
            if (listing.Files.Length > 0)
            {
                throw new Exception(TXT.Translate("Aborted deletion of the directory because it is no longer empty. This can happen if RED previously failed to delete an empty (trash) file: {0}", RedAssist.DQuote(path)));
            }

            paths.Add(path);
            foreach (DirectoryInfo sub in listing.Directories)
            {
                AddVerifiedEmptySubtreeDirectory(sub.FullName, paths);
            }
        }

        /// <summary>
        /// Walks the whole subtree and throws if any file or reparse point is found.
        /// Called immediately before a recursive delete: the scan that classified the
        /// subtree as empty may be minutes old, and content created since then must
        /// abort the deletion instead of being silently destroyed.
        /// </summary>
        private static void VerifySubtreeHasNoFiles(string path)
        {
            var dir = new DirectoryInfo(path);
            var listing = FastDirectoryEnumerator.GetFilesAndDirectories(dir);

            if (listing.Files.Length > 0)
            {
                throw new Exception(TXT.Translate("Aborted deletion of the directory because it is no longer empty. This can happen if RED previously failed to delete an empty (trash) file: {0}", RedAssist.DQuote(path)));
            }

            foreach (DirectoryInfo sub in listing.Directories)
            {
                // Handle-based check (throws REDPermissionDeniedException on reparse
                // points) — works past MAX_PATH where DirectoryInfo.Attributes cannot
                VerifyNotReparsePoint(sub.FullName);
                VerifySubtreeHasNoFiles(sub.FullName);
            }
        }

        /// <summary>
        /// Bottom-up handle-based delete of a verified-empty subtree. Unlike
        /// DirectoryInfo.Delete(true) this works past MAX_PATH regardless of the
        /// OS LongPathsEnabled policy, and every level gets the same reparse-point
        /// re-check as a single-directory delete.
        /// </summary>
        private static void DeleteEmptySubtreeByHandle(string path)
        {
            foreach (DirectoryInfo sub in FastDirectoryEnumerator.GetDirectories(new DirectoryInfo(path)))
            {
                DeleteEmptySubtreeByHandle(sub.FullName);
            }
            DirectDeleteByHandle(path);
        }

        public static bool IsDirLocked(string path)
        {
            try
            {
                // .NET (Core) removed the static Directory.GetAccessControl; the
                // DirectoryInfo.GetAccessControl() extension is the 1:1 replacement
                // (same default AccessControlSections: Access | Owner | Group).
                var acl = new System.IO.DirectoryInfo(path).GetAccessControl();
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
            string ignored;
            SecureDeleteDirectory(path, deleteMode, out ignored);
        }

        /// <param name="movedToDestination">
        /// The actual destination directory for MoveToFolder mode (may carry a _N
        /// collision suffix), null for every other mode. Recorded in the undo
        /// manifest so a restore can move the directory back.
        /// </param>
        public static void SecureDeleteDirectory(string path, DeleteModes deleteMode, out string movedToDestination)
        {
            movedToDestination = null;

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

                var listing = FastDirectoryEnumerator.GetFilesAndDirectories(di);
                if (listing.Files.Length > 0)
                {
                    throw new Exception(TXT.Translate("Aborted deletion of the directory because it is no longer empty. This can happen if RED previously failed to delete an empty (trash) file: {0}", RedAssist.DQuote(path)));
                }

                if (listing.Directories.Length == 0)
                {
                    DirectDeleteByHandle(path);
                }
                else
                {
                    // Recursive delete of a wholly-empty subtree: re-verify every level
                    // first — direct deletion is unrecoverable and the scan may be stale.
                    VerifySubtreeHasNoFiles(path);
                    DeleteEmptySubtreeByHandle(path);
                }
                return;
            }

            if (deleteMode == DeleteModes.MoveToFolder)
            {
                if (string.IsNullOrWhiteSpace(MoveToFolderTarget))
                    throw new Exception(TXT.Translate("Move-to-folder target has not been set"));
                if (PathContains(path, MoveToFolderTarget))
                    throw new Exception(TXT.Translate("The move-to folder is inside a directory that is being moved: {0}", RedAssist.DQuote(MoveToFolderTarget)));
                var moveListing = FastDirectoryEnumerator.GetFilesAndDirectories(new DirectoryInfo(path));
                if (moveListing.Files.Length > 0)
                {
                    throw new Exception(TXT.Translate("Aborted deletion of the directory because it is no longer empty. This can happen if RED previously failed to delete an empty (trash) file: {0}", RedAssist.DQuote(path)));
                }
                if (moveListing.Directories.Length > 0)
                {
                    VerifySubtreeHasNoFiles(path);
                }
                string relativePath = new DirectoryInfo(path).Name;
                string destPath = Path.Combine(MoveToFolderTarget, relativePath);
                int counter = 1;
                while (Directory.Exists(destPath))
                {
                    destPath = Path.Combine(MoveToFolderTarget, relativePath + "_" + counter++);
                }
                try
                {
                    Directory.Move(path, destPath);
                }
                catch (IOException)
                {
                    // Re-verify emptiness first: the catch also fires for transient IO
                    // errors, and content could have appeared since the pre-move check
                    // (TOCTOU). Never recursively delete an unverified subtree.
                    var refreshed = FastDirectoryEnumerator.GetFilesAndDirectories(new DirectoryInfo(path));
                    if (refreshed.Files.Length > 0)
                    {
                        throw new Exception(TXT.Translate("Aborted move of the directory because it is no longer empty: {0}", RedAssist.DQuote(path)));
                    }
                    if (refreshed.Directories.Length > 0)
                    {
                        VerifySubtreeHasNoFiles(path);
                    }
                    // Directory.Move cannot cross volumes — replicate, then remove the source
                    CopyDirectoryRecursive(path, destPath);
                    Directory.Delete(path, true);
                }
                movedToDestination = destPath;
                return;
            }

            // Last security check before recycle-bin deletion — the subtree must still
            // be free of files at every level (the scan may be stale)
            var recycleListing = FastDirectoryEnumerator.GetFilesAndDirectories(new DirectoryInfo(path));
            if (recycleListing.Files.Length == 0)
            {
                if (recycleListing.Directories.Length > 0)
                {
                    VerifySubtreeHasNoFiles(path);
                }
                bool silent = ConfigAssist.SilentMode;
                if (deleteMode == DeleteModes.RecycleBin)
                {
                    RecycleBinOperation.RecycleSingle(path, allowConfirmation: false, allowErrorUi: false);
                }
                else if (deleteMode == DeleteModes.RecycleBinShowErrors)
                {
                    RecycleBinOperation.RecycleSingle(path, allowConfirmation: false, allowErrorUi: !silent);
                }
                else if (deleteMode == DeleteModes.RecycleBinWithQuestion)
                {
                    RecycleBinOperation.RecycleSingle(path, allowConfirmation: !silent, allowErrorUi: !silent);
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

        private static bool PathContains(string parent, string candidate)
        {
            string p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string c = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            var listing = FastDirectoryEnumerator.GetFilesAndDirectories(new DirectoryInfo(sourceDir));
            foreach (FileInfo file in listing.Files)
            {
                file.CopyTo(Path.Combine(destDir, file.Name), overwrite: false);
            }
            foreach (DirectoryInfo sub in listing.Directories)
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    throw new REDPermissionDeniedException(TXT.Translate("Refused to delete directory because it is a reparse point (junction, symlink, or mount point): {0}", RedAssist.DQuote(sub.FullName)));
                }
                CopyDirectoryRecursive(sub.FullName, Path.Combine(destDir, sub.Name));
            }
        }

        public static void SecureDeleteFile(FileInfo file, DeleteModes deleteMode)
        {
            string ignored;
            SecureDeleteFile(file, deleteMode, out ignored, false);
        }

        public static void SecureDeleteStandaloneFile(FileInfo file, DeleteModes deleteMode, out string movedToDestination)
        {
            SecureDeleteFile(file, deleteMode, out movedToDestination, true);
        }

        private static void SecureDeleteFile(FileInfo file, DeleteModes deleteMode, out string movedToDestination, bool moveFilesToFolder)
        {
            movedToDestination = null;

            if (deleteMode == DeleteModes.Simulate)
            {
                return;
            }

            if (deleteMode == DeleteModes.MoveToFolder)
            {
                if (!moveFilesToFolder)
                {
                    return;
                }
                movedToDestination = MoveFileToFolder(file);
                return;
            }

            if (deleteMode == DeleteModes.RecycleBin || deleteMode == DeleteModes.RecycleBinShowErrors)
            {
                RecycleBinOperation.RecycleSingle(file.FullName, allowConfirmation: false,
                    allowErrorUi: deleteMode == DeleteModes.RecycleBinShowErrors && !ConfigAssist.SilentMode);
            }
            else if (deleteMode == DeleteModes.RecycleBinWithQuestion)
            {
                RecycleBinOperation.RecycleSingle(file.FullName,
                    allowConfirmation: !ConfigAssist.SilentMode,
                    allowErrorUi: !ConfigAssist.SilentMode);
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

        private static string MoveFileToFolder(FileInfo file)
        {
            if (string.IsNullOrWhiteSpace(MoveToFolderTarget))
                throw new Exception(TXT.Translate("Move-to-folder target has not been set"));

            Directory.CreateDirectory(MoveToFolderTarget);
            string baseName = Path.GetFileNameWithoutExtension(file.Name);
            string extension = file.Extension;
            string destPath = Path.Combine(MoveToFolderTarget, file.Name);
            int counter = 1;
            while (File.Exists(destPath) || Directory.Exists(destPath))
            {
                destPath = Path.Combine(MoveToFolderTarget, baseName + "_" + counter++ + extension);
            }

            try
            {
                File.Move(file.FullName, destPath);
            }
            catch (IOException)
            {
                File.Copy(file.FullName, destPath, overwrite: false);
                File.Delete(file.FullName);
            }
            return destPath;
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
