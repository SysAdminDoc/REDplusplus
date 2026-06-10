using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using RED.Helper;
using RED.Match;
using TXT = RED.RedGetText;

namespace RED
{
    internal class MftScanner
    {
        #region P/Invoke

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice, uint dwIoControlCode,
            ref MFT_ENUM_DATA_V0 lpInBuffer, int nInBufferSize,
            byte[] lpOutBuffer, int nOutBufferSize,
            out int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetVolumeInformationW(
            string lpRootPathName,
            StringBuilder lpVolumeNameBuffer, int nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            StringBuilder lpFileSystemNameBuffer, int nFileSystemNameSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct MFT_ENUM_DATA_V0
        {
            public ulong StartFileReferenceNumber;
            public long LowUsn;
            public long HighUsn;
        }

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FSCTL_ENUM_USN_DATA = 0x000900B3;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
        private const uint FILE_ATTRIBUTE_HIDDEN = 0x2;
        private const uint FILE_ATTRIBUTE_SYSTEM = 0x4;

        #endregion

        private struct MftEntry
        {
            public ulong FileReferenceNumber;
            public ulong ParentFileReferenceNumber;
            public string FileName;
            public uint FileAttributes;
            public bool IsDirectory;
        }

        private readonly Dictionary<ulong, MftEntry> entries = new Dictionary<ulong, MftEntry>();
        private readonly Dictionary<ulong, List<ulong>> childrenOf = new Dictionary<ulong, List<ulong>>();

        internal static bool IsNtfsVolume(string path)
        {
            try
            {
                string root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\"))
                    return false;

                var volName = new StringBuilder(256);
                var fsName = new StringBuilder(256);
                uint serial, maxLen, flags;
                if (GetVolumeInformationW(root, volName, volName.Capacity, out serial, out maxLen, out flags, fsName, fsName.Capacity))
                {
                    return string.Equals(fsName.ToString(), "NTFS", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return false;
        }

        internal bool EnumerateMft(string volumeRoot, BackgroundWorker worker)
        {
            string volumePath = @"\\.\" + volumeRoot.TrimEnd('\\');

            IntPtr hVolume = CreateFileW(volumePath, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

            if (hVolume == INVALID_HANDLE_VALUE)
                return false;

            try
            {
                var enumData = new MFT_ENUM_DATA_V0
                {
                    StartFileReferenceNumber = 0,
                    LowUsn = 0,
                    HighUsn = long.MaxValue
                };

                byte[] buffer = new byte[128 * 1024];
                int bytesReturned;
                int recordCount = 0;

                while (DeviceIoControl(hVolume, FSCTL_ENUM_USN_DATA,
                    ref enumData, Marshal.SizeOf(enumData),
                    buffer, buffer.Length, out bytesReturned, IntPtr.Zero))
                {
                    if (bytesReturned <= 8) break;
                    if (worker != null && worker.CancellationPending) return false;

                    enumData.StartFileReferenceNumber = BitConverter.ToUInt64(buffer, 0);

                    int offset = 8;
                    while (offset + 60 < bytesReturned)
                    {
                        int recordLength = BitConverter.ToInt32(buffer, offset);
                        if (recordLength <= 0) break;
                        if (offset + recordLength > bytesReturned) break;

                        ulong frn = BitConverter.ToUInt64(buffer, offset + 8);
                        ulong parentFrn = BitConverter.ToUInt64(buffer, offset + 16);
                        uint fileAttribs = BitConverter.ToUInt32(buffer, offset + 52);
                        int nameLength = BitConverter.ToInt16(buffer, offset + 56);
                        int nameOffset = BitConverter.ToInt16(buffer, offset + 58);

                        if (nameLength > 0 && offset + nameOffset + nameLength <= bytesReturned)
                        {
                            string fileName = Encoding.Unicode.GetString(buffer, offset + nameOffset, nameLength);

                            var entry = new MftEntry
                            {
                                FileReferenceNumber = frn,
                                ParentFileReferenceNumber = parentFrn,
                                FileName = fileName,
                                FileAttributes = fileAttribs,
                                IsDirectory = (fileAttribs & FILE_ATTRIBUTE_DIRECTORY) != 0
                            };

                            entries[frn] = entry;

                            if (!childrenOf.ContainsKey(parentFrn))
                                childrenOf[parentFrn] = new List<ulong>();
                            childrenOf[parentFrn].Add(frn);
                        }

                        offset += recordLength;
                        recordCount++;
                    }

                    if (recordCount % 100000 == 0 && worker != null)
                    {
                        worker.ReportProgress(0, string.Format("MFT: {0:N0} records enumerated...", recordCount));
                    }
                }

                return entries.Count > 0;
            }
            finally
            {
                CloseHandle(hVolume);
            }
        }

        internal ulong? FindFrnByPath(string fullPath)
        {
            string root = Path.GetPathRoot(fullPath);
            string relative = fullPath.Substring(root.Length);
            if (string.IsNullOrEmpty(relative))
                return 5; // NTFS root directory FRN

            string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            ulong currentFrn = 5; // NTFS root
            foreach (string part in parts)
            {
                if (!childrenOf.ContainsKey(currentFrn))
                    return null;

                ulong? found = null;
                foreach (ulong childFrn in childrenOf[currentFrn])
                {
                    if (entries.ContainsKey(childFrn) &&
                        string.Equals(entries[childFrn].FileName, part, StringComparison.OrdinalIgnoreCase))
                    {
                        found = childFrn;
                        break;
                    }
                }

                if (!found.HasValue) return null;
                currentFrn = found.Value;
            }

            return currentFrn;
        }

        internal string ReconstructPath(ulong frn, string volumeRoot)
        {
            var parts = new List<string>();
            ulong current = frn;
            int maxDepth = 512;

            while (current != 5 && maxDepth-- > 0)
            {
                if (!entries.ContainsKey(current)) return null;
                parts.Add(entries[current].FileName);
                current = entries[current].ParentFileReferenceNumber;
            }

            if (maxDepth <= 0) return null;

            parts.Reverse();
            return volumeRoot.TrimEnd('\\') + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar.ToString(), parts);
        }

        internal void FindEmptyDirectories(
            ulong startFrn,
            string volumeRoot,
            RuntimeData runData,
            BackgroundWorker worker,
            ref int folderCount)
        {
            var emptyDirs = new List<ulong>();
            CheckSubtreeEmpty(startFrn, volumeRoot, runData, worker, ref folderCount, emptyDirs, 1);

            foreach (ulong frn in emptyDirs)
            {
                if (worker != null && worker.CancellationPending) return;

                string path = ReconstructPath(frn, volumeRoot);
                if (path == null) continue;

                try
                {
                    var dirInfo = new DirectoryInfo(path);
                    if (dirInfo.Exists)
                    {
                        worker?.ReportProgress(0, new FoundEmptyDirInfoEventArgs(dirInfo, DirectorySearchStatusTypes.Empty));
                    }
                }
                catch { }
            }
        }

        private bool CheckSubtreeEmpty(
            ulong frn, string volumeRoot, RuntimeData runData,
            BackgroundWorker worker, ref int folderCount,
            List<ulong> emptyDirs, int depth)
        {
            if (worker != null && worker.CancellationPending) return false;
            if (runData.MaxDepth != -1 && depth > runData.MaxDepth) return false;

            if (!entries.ContainsKey(frn)) return false;
            var entry = entries[frn];
            if (!entry.IsDirectory) return false;

            folderCount++;
            if (folderCount % 1000 == 0 && worker != null)
            {
                worker.ReportProgress(0, string.Format("Checking: {0}", entry.FileName));
            }

            // Check reparse points
            if ((entry.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
                return false;

            // Check hidden/system filters
            if (runData.IgnoreHiddenFolders && (entry.FileAttributes & FILE_ATTRIBUTE_HIDDEN) != 0)
                return false;
            if (runData.IgnoreSystemFolders && (entry.FileAttributes & FILE_ATTRIBUTE_SYSTEM) != 0)
                return false;

            // Check ignore list
            string fullPath = ReconstructPath(frn, volumeRoot);
            if (fullPath != null)
            {
                var dirInfo = new DirectoryInfo(fullPath);
                if (runData.NeverEmptyDirectoryList.IsOnList(dirInfo))
                {
                    if (!runData.HideIgnoredDirectories && worker != null)
                    {
                        worker.ReportProgress(0, new FoundEmptyDirInfoEventArgs(dirInfo, DirectorySearchStatusTypes.NeverEmpty,
                            TXT.Translate("Directory is on the NeverEmpty list: {0}", RedAssist.DQuote(fullPath))));
                    }
                    return false;
                }
                if (runData.IgnoreDirectoryNameList.IsOnList(dirInfo))
                {
                    if (!runData.HideIgnoredDirectories && worker != null)
                    {
                        worker.ReportProgress(0, new FoundEmptyDirInfoEventArgs(dirInfo, DirectorySearchStatusTypes.Ignore));
                    }
                    return false;
                }
            }

            bool hasFiles = false;
            bool allSubdirsEmpty = true;

            if (childrenOf.ContainsKey(frn))
            {
                foreach (ulong childFrn in childrenOf[frn])
                {
                    if (!entries.ContainsKey(childFrn)) continue;
                    var child = entries[childFrn];

                    if (child.IsDirectory)
                    {
                        if (!CheckSubtreeEmpty(childFrn, volumeRoot, runData, worker, ref folderCount, emptyDirs, depth + 1))
                        {
                            allSubdirsEmpty = false;
                        }
                    }
                    else
                    {
                        // Cloud-only placeholders are real content
                        const uint RECALL_ON_DATA = 0x00400000;
                        const uint RECALL_ON_OPEN = 0x00040000;
                        if ((child.FileAttributes & RECALL_ON_DATA) != 0 || (child.FileAttributes & RECALL_ON_OPEN) != 0)
                        {
                            hasFiles = true;
                            continue;
                        }

                        bool isIgnored = false;
                        try
                        {
                            if (fullPath != null)
                            {
                                var fileInfo = new FileInfo(Path.Combine(fullPath, child.FileName));
                                string dummy;
                                // USN records don't carry file size; pass 1 so only name-based rules match
                                // (zero-size files detected by the standard scanner only)
                                isIgnored = runData.IgnoreFileNameList.IsOnList(fileInfo, 1, false, out dummy);
                            }
                        }
                        catch { }

                        if (!isIgnored)
                        {
                            hasFiles = true;
                        }
                    }
                }
            }

            if (!hasFiles && allSubdirsEmpty)
            {
                emptyDirs.Add(frn);
                return true;
            }

            return false;
        }
    }
}
