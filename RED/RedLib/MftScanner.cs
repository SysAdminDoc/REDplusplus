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
        private static extern bool DeviceIoControl(
            IntPtr hDevice, uint dwIoControlCode,
            ref MFT_ENUM_DATA_V1 lpInBuffer, int nInBufferSize,
            byte[] lpOutBuffer, int nOutBufferSize,
            out int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(IntPtr hFile, out BY_HANDLE_FILE_INFORMATION info);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandleEx(
            IntPtr hFile, int fileInformationClass, out FILE_ID_INFO info, uint dwBufferSize);

        // FILE_ID_INFO (FileIdInfo = 18) carries the 128-bit FileId needed for ReFS.
        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_ID_INFO
        {
            public ulong VolumeSerialNumber;
            public ulong FileIdLow;
            public ulong FileIdHigh;
        }
        private const int FileIdInfo = 18;

        // FILETIME fields must be 4-byte aligned (two DWORDs) — `long` would insert
        // padding after dwFileAttributes and corrupt the file-index fields we rely on
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

        // V1 input is required for ReFS; MinMajorVersion=2/MaxMajorVersion=3 lets the
        // volume return V2 records on NTFS and V3 (128-bit FRNs) on ReFS.
        [StructLayout(LayoutKind.Sequential)]
        private struct MFT_ENUM_DATA_V1
        {
            public ulong StartFileReferenceNumber;
            public long LowUsn;
            public long HighUsn;
            public ushort MinMajorVersion;
            public ushort MaxMajorVersion;
        }

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_READ_ATTRIBUTES = 0x0080;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FSCTL_ENUM_USN_DATA = 0x000900B3;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
        private const uint FILE_ATTRIBUTE_HIDDEN = 0x2;
        private const uint FILE_ATTRIBUTE_SYSTEM = 0x4;

        #endregion

        /// <summary>
        /// A file reference number. NTFS (USN_RECORD_V2) uses 64 bits, stored in
        /// <see cref="Low"/> with <see cref="High"/> = 0. ReFS / Dev Drive
        /// (USN_RECORD_V3) uses a 128-bit FILE_ID_128, needing both halves.
        /// </summary>
        internal struct Frn : IEquatable<Frn>
        {
            public readonly ulong Low;
            public readonly ulong High;

            public Frn(ulong low, ulong high) { Low = low; High = high; }

            public static Frn From64(ulong value) { return new Frn(value, 0); }

            public bool Equals(Frn other) { return Low == other.Low && High == other.High; }
            public override bool Equals(object obj) { return obj is Frn && Equals((Frn)obj); }
            public override int GetHashCode() { return Low.GetHashCode() ^ (High.GetHashCode() * 397); }
        }

        private struct MftEntry
        {
            public Frn FileReferenceNumber;
            public Frn ParentFileReferenceNumber;
            public string FileName;
            public uint FileAttributes;
            public bool IsDirectory;
        }

        private readonly Dictionary<Frn, MftEntry> entries = new Dictionary<Frn, MftEntry>();
        private readonly Dictionary<Frn, List<Frn>> childrenOf = new Dictionary<Frn, List<Frn>>();

        // The real file reference number of the volume root (includes the sequence
        // number in the upper bits, so it is NOT the literal MFT index 5).
        private Frn rootFrn;

        // ReFS / Dev Drive volumes need the 128-bit USN_RECORD_V3 path.
        private bool isReFs;

        /// <summary>
        /// Resolves the true FRN of the volume root by opening it and reading its file index.
        /// USN records reference parents by full 64-bit FRN (with sequence bits), so a
        /// hardcoded constant would never match and every lookup would fail.
        /// </summary>
        private static bool TryGetRootFrn(string volumeRoot, bool refs, out Frn frn)
        {
            frn = default(Frn);
            IntPtr hDir = CreateFileW(volumeRoot, FILE_READ_ATTRIBUTES,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);

            if (hDir == INVALID_HANDLE_VALUE)
                return false;

            try
            {
                if (refs)
                {
                    // ReFS file IDs are 128-bit — nFileIndexHigh/Low from the legacy
                    // call would be truncated, so use FILE_ID_INFO
                    FILE_ID_INFO idInfo;
                    if (!GetFileInformationByHandleEx(hDir, FileIdInfo, out idInfo, (uint)Marshal.SizeOf(typeof(FILE_ID_INFO))))
                        return false;
                    frn = new Frn(idInfo.FileIdLow, idInfo.FileIdHigh);
                    return true;
                }

                BY_HANDLE_FILE_INFORMATION info;
                if (!GetFileInformationByHandle(hDir, out info))
                    return false;

                frn = Frn.From64(((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow);
                return true;
            }
            finally
            {
                CloseHandle(hDir);
            }
        }

        /// <summary>Returns NTFS / REFS / null for the volume hosting <paramref name="path"/>.</summary>
        private static string GetFileSystemName(string path)
        {
            try
            {
                string root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\"))
                    return null;

                var volName = new StringBuilder(256);
                var fsName = new StringBuilder(256);
                uint serial, maxLen, flags;
                if (GetVolumeInformationW(root, volName, volName.Capacity, out serial, out maxLen, out flags, fsName, fsName.Capacity))
                {
                    return fsName.ToString();
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// True if the MFT turbo scan can run here — NTFS or ReFS (Dev Drive).
        /// FAT/exFAT/network volumes have no USN journal and fall back to the walk.
        /// </summary>
        internal static bool IsSupportedVolume(string path)
        {
            string fs = GetFileSystemName(path);
            return string.Equals(fs, "NTFS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fs, "ReFS", StringComparison.OrdinalIgnoreCase);
        }

        internal bool EnumerateMft(string volumeRoot, BackgroundWorker worker)
        {
            isReFs = string.Equals(GetFileSystemName(volumeRoot), "ReFS", StringComparison.OrdinalIgnoreCase);

            if (!TryGetRootFrn(volumeRoot, isReFs, out rootFrn))
                return false;

            string volumePath = @"\\.\" + volumeRoot.TrimEnd('\\');

            IntPtr hVolume = CreateFileW(volumePath, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

            if (hVolume == INVALID_HANDLE_VALUE)
                return false;

            try
            {
                byte[] buffer = new byte[128 * 1024];
                int recordCount = 0;
                bool enumerationComplete = false;
                int bytesReturned;

                if (isReFs)
                {
                    // V1 input, V3 (128-bit FRN) records
                    var enumData = new MFT_ENUM_DATA_V1
                    {
                        StartFileReferenceNumber = 0,
                        LowUsn = 0,
                        HighUsn = long.MaxValue,
                        MinMajorVersion = 2,
                        MaxMajorVersion = 3
                    };
                    while (true)
                    {
                        bool ok = DeviceIoControl(hVolume, FSCTL_ENUM_USN_DATA,
                            ref enumData, Marshal.SizeOf(enumData),
                            buffer, buffer.Length, out bytesReturned, IntPtr.Zero);
                        if (!ok) { enumerationComplete = IsEnumerationEofError(); break; }
                        if (bytesReturned <= 8) { enumerationComplete = true; break; }
                        if (worker != null && worker.CancellationPending) return false;
                        enumData.StartFileReferenceNumber = BitConverter.ToUInt64(buffer, 0);
                        ParseRecords(buffer, bytesReturned, ref recordCount, worker);
                    }
                }
                else
                {
                    // V0 input, V2 (64-bit FRN) records — the original NTFS path
                    var enumData = new MFT_ENUM_DATA_V0
                    {
                        StartFileReferenceNumber = 0,
                        LowUsn = 0,
                        HighUsn = long.MaxValue
                    };
                    while (true)
                    {
                        bool ok = DeviceIoControl(hVolume, FSCTL_ENUM_USN_DATA,
                            ref enumData, Marshal.SizeOf(enumData),
                            buffer, buffer.Length, out bytesReturned, IntPtr.Zero);
                        if (!ok) { enumerationComplete = IsEnumerationEofError(); break; }
                        if (bytesReturned <= 8) { enumerationComplete = true; break; }
                        if (worker != null && worker.CancellationPending) return false;
                        enumData.StartFileReferenceNumber = BitConverter.ToUInt64(buffer, 0);
                        ParseRecords(buffer, bytesReturned, ref recordCount, worker);
                    }
                }

                // Fail closed: an aborted/truncated enumeration (any termination
                // other than the EOF sentinel) or a buffer that yielded no records
                // means the entry set is unreliable. A directory whose children
                // were dropped would look empty, so the caller must fall back to
                // the standard recursive walker rather than risk a false-empty.
                if (!enumerationComplete || entries.Count == 0)
                    return false;

                // Integrity check: every directory referenced as a parent must have
                // its own record. A missing parent record is the signature of a
                // dropped USN record, which can hide real children. Treat it as an
                // incomplete enumeration and fall back.
                if (!IsEnumerationConsistent())
                    return false;

                return true;
            }
            finally
            {
                CloseHandle(hVolume);
            }
        }

        // True only when the USN walk ended at the EOF sentinel. Must be called
        // immediately after DeviceIoControl returns false, before any other
        // managed call can overwrite the thread's last Win32 error.
        private static bool IsEnumerationEofError()
        {
            const int ERROR_HANDLE_EOF = 38;
            return Marshal.GetLastWin32Error() == ERROR_HANDLE_EOF;
        }

        // Returns false if any parent FRN referenced by an enumerated record lacks
        // its own record (excluding the volume root, whose record the enumeration
        // may legitimately omit). A dangling parent means records were dropped.
        internal bool IsEnumerationConsistent()
        {
            foreach (Frn parent in childrenOf.Keys)
            {
                if (parent.Equals(rootFrn)) continue;
                if (!entries.ContainsKey(parent)) return false;
            }
            return true;
        }

        /// <summary>
        /// Parses USN records from a returned buffer (skipping the leading 8-byte
        /// next-FRN cursor). Handles both V2 (NTFS, 64-bit FRN) and V3 (ReFS,
        /// 128-bit FRN) records, dispatched by each record's MajorVersion field.
        /// </summary>
        // Number of MFT records parsed into the entry table (test seam).
        internal int ParsedEntryCount { get { return entries.Count; } }

        internal void ParseRecords(byte[] buffer, int bytesReturned, ref int recordCount, BackgroundWorker worker)
        {
            // The buffer is the ground truth: never trust a caller-reported length larger
            // than it (a corrupt/hostile FSCTL result) or a field read would run off the
            // end. Clamp to the real buffer size; a non-positive count means nothing to do.
            if (buffer == null) return;
            if (bytesReturned > buffer.Length) bytesReturned = buffer.Length;
            if (bytesReturned < 0) bytesReturned = 0;

            int offset = 8;
            // Smallest record we will read fields from: the RecordLength (4) +
            // MajorVersion (2) need at least 6 bytes to dispatch on version.
            while (offset + 6 <= bytesReturned)
            {
                int recordLength = BitConverter.ToInt32(buffer, offset);
                if (recordLength <= 0) break;
                // Widen to long so a corrupt huge RecordLength cannot wrap past
                // int.MaxValue and slip through the in-buffer bounds check.
                if ((long)offset + recordLength > bytesReturned) break;

                ushort majorVersion = BitConverter.ToUInt16(buffer, offset + 4);

                // Each version has a fixed-field header that must fit entirely
                // within this record before we read FRNs/attributes/name fields.
                int headerSize = majorVersion >= 3 ? 76 : 60;
                if (recordLength < headerSize) break;

                Frn frn, parentFrn;
                uint fileAttribs;
                int nameLength, nameOffset;

                if (majorVersion >= 3)
                {
                    // USN_RECORD_V3: 16-byte FILE_ID_128 fields
                    frn = new Frn(BitConverter.ToUInt64(buffer, offset + 8), BitConverter.ToUInt64(buffer, offset + 16));
                    parentFrn = new Frn(BitConverter.ToUInt64(buffer, offset + 24), BitConverter.ToUInt64(buffer, offset + 32));
                    fileAttribs = BitConverter.ToUInt32(buffer, offset + 68);
                    nameLength = BitConverter.ToUInt16(buffer, offset + 72);
                    nameOffset = BitConverter.ToUInt16(buffer, offset + 74);
                }
                else
                {
                    // USN_RECORD_V2: 8-byte FRN fields
                    frn = Frn.From64(BitConverter.ToUInt64(buffer, offset + 8));
                    parentFrn = Frn.From64(BitConverter.ToUInt64(buffer, offset + 16));
                    fileAttribs = BitConverter.ToUInt32(buffer, offset + 52);
                    nameLength = BitConverter.ToUInt16(buffer, offset + 56);
                    nameOffset = BitConverter.ToUInt16(buffer, offset + 58);
                }

                // The name must lie within this record (not merely within the
                // buffer): validate against recordLength to reject a crafted
                // nameOffset/nameLength that points into an adjacent record.
                if (nameLength > 0 && nameOffset >= headerSize
                    && (long)nameOffset + nameLength <= recordLength)
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
                        childrenOf[parentFrn] = new List<Frn>();
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

        internal Frn? FindFrnByPath(string fullPath)
        {
            string root = Path.GetPathRoot(fullPath);
            string relative = fullPath.Substring(root.Length);
            if (string.IsNullOrEmpty(relative))
                return rootFrn;

            string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            Frn currentFrn = rootFrn;
            foreach (string part in parts)
            {
                if (!childrenOf.ContainsKey(currentFrn))
                    return null;

                Frn? found = null;
                foreach (Frn childFrn in childrenOf[currentFrn])
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

        internal string ReconstructPath(Frn frn, string volumeRoot)
        {
            var parts = new List<string>();
            Frn current = frn;
            int maxDepth = 512;

            while (!current.Equals(rootFrn) && maxDepth-- > 0)
            {
                if (!entries.ContainsKey(current)) return null;
                parts.Add(entries[current].FileName);
                current = entries[current].ParentFileReferenceNumber;
            }

            if (!current.Equals(rootFrn)) return null;

            parts.Reverse();
            return volumeRoot.TrimEnd('\\') + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar.ToString(), parts);
        }

        internal void FindEmptyDirectories(
            Frn startFrn,
            string volumeRoot,
            RuntimeData runData,
            FindEmptyDirectoryWorker worker,
            ref int folderCount,
            GitIgnoreParser gitIgnoreParser,
            string startFolderPath)
        {
            var emptyDirs = new List<Frn>();
            CheckSubtreeEmpty(startFrn, volumeRoot, runData, worker, ref folderCount, emptyDirs, 1, gitIgnoreParser, startFolderPath);

            foreach (Frn frn in emptyDirs)
            {
                if (worker != null && worker.CancellationPending) return;

                string path = ReconstructPath(frn, volumeRoot);
                if (path == null) continue;

                try
                {
                    var dirInfo = new DirectoryInfo(path);
                    if (!dirInfo.Exists) continue;

                    worker?.ReportDirectoryStatus(dirInfo, DirectorySearchStatusTypes.Empty);
                }
                catch { }
            }
        }

        private bool CheckSubtreeEmpty(
            Frn frn, string volumeRoot, RuntimeData runData,
            FindEmptyDirectoryWorker worker, ref int folderCount,
            List<Frn> emptyDirs, int depth,
            GitIgnoreParser gitIgnore, string scanRootPath)
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
                if (OsCriticalPaths.IsProtected(dirInfo))
                {
                    if (!runData.HideIgnoredDirectories && worker != null)
                    {
                        worker.ReportProgress(0, new FoundEmptyDirInfoEventArgs(dirInfo, DirectorySearchStatusTypes.NeverEmpty,
                            TXT.Translate("Directory is a protected OS-critical path: {0}", RedAssist.DQuote(fullPath))));
                    }
                    return false;
                }
                if (RedKeepMarker.HasMarker(dirInfo))
                {
                    if (!runData.HideIgnoredDirectories && worker != null)
                    {
                        worker.ReportProgress(0, new FoundEmptyDirInfoEventArgs(dirInfo, DirectorySearchStatusTypes.NeverEmpty,
                            TXT.Translate("Directory is protected by a {0} marker file: {1}", RedKeepMarker.MarkerFileName, RedAssist.DQuote(fullPath))));
                    }
                    return false;
                }
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

                // .gitignore rules — check with parent's parser before extending
                if (gitIgnore != null && scanRootPath != null &&
                    fullPath.Length > scanRootPath.Length &&
                    fullPath.StartsWith(scanRootPath, StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = fullPath.Substring(scanRootPath.Length);
                    if (gitIgnore.IsIgnored(entry.FileName, relativePath))
                    {
                        if (!runData.HideIgnoredDirectories && worker != null)
                        {
                            worker.ReportProgress(0, new FoundEmptyDirInfoEventArgs(dirInfo, DirectorySearchStatusTypes.Ignore));
                        }
                        return false;
                    }
                }

                // Extend parser with this directory's .gitignore for child checks
                if (gitIgnore != null)
                    gitIgnore = gitIgnore.ExtendForDirectory(fullPath, scanRootPath);

                // Same minimum-age rule as the standard scanner: a directory younger
                // than the threshold is not scanned and keeps its parent non-empty.
                if (runData.MinFolderAgeHours > 0)
                {
                    try
                    {
                        if (dirInfo.CreationTime.AddHours(runData.MinFolderAgeHours) >= DateTime.Now)
                        {
                            runData.AddLogMessage(TXT.Translate("Directory {0} skipped because creation time [{1}] is < {2} hours old", RedAssist.DQuote(fullPath), dirInfo.CreationTime.ToString(), runData.MinFolderAgeHours.ToString()));
                            return false;
                        }
                    }
                    catch { return false; }
                }
            }

            bool hasFiles = false;
            bool allSubdirsEmpty = true;

            if (childrenOf.ContainsKey(frn))
            {
                foreach (Frn childFrn in childrenOf[frn])
                {
                    if (!entries.ContainsKey(childFrn)) continue;
                    var child = entries[childFrn];

                    if (child.IsDirectory)
                    {
                        if (!CheckSubtreeEmpty(childFrn, volumeRoot, runData, worker, ref folderCount, emptyDirs, depth + 1, gitIgnore, scanRootPath))
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
