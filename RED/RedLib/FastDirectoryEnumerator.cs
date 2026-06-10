using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace RED
{
    internal static class FastDirectoryEnumerator
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindFirstFileExW(
            string lpFileName,
            FINDEX_INFO_LEVELS fInfoLevelId,
            out WIN32_FIND_DATAW lpFindFileData,
            FINDEX_SEARCH_OPS fSearchOp,
            IntPtr lpSearchFilter,
            int dwAdditionalFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        private enum FINDEX_INFO_LEVELS { FindExInfoStandard = 0, FindExInfoBasic = 1 }
        private enum FINDEX_SEARCH_OPS { FindExSearchNameMatch = 0 }
        private const int FIND_FIRST_EX_LARGE_FETCH = 0x00000002;
        private const int ERROR_FILE_NOT_FOUND = 2;
        private const int ERROR_NO_MORE_FILES = 18;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

        /// <summary>
        /// Fail closed: an enumeration failure (access denied, path too long, dir
        /// vanished) must throw so callers classify the directory as Error — never
        /// return an empty list, which would make an unreadable directory look
        /// Empty and therefore delete-eligible.
        /// </summary>
        private static void ThrowIfEnumerationFailed(string path)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_FILE_NOT_FOUND || err == ERROR_NO_MORE_FILES)
            {
                return; // genuinely no entries
            }
            throw new System.ComponentModel.Win32Exception(err,
                string.Format("Cannot enumerate directory (error {0}): {1}", err, path));
        }

        // FILETIME fields must be 4-byte aligned (two DWORDs). Declaring them as
        // `long` makes .NET insert 4 padding bytes after dwFileAttributes, shifting
        // every later field — cFileName then starts 2 characters into the real name,
        // "." and ".." escape the dot-filter, and the scan recurses into itself.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        internal static FileInfo[] GetFiles(DirectoryInfo dir)
        {
            var results = new List<FileInfo>();
            WIN32_FIND_DATAW data;

            IntPtr handle = FindFirstFileExW(
                dir.FullName + @"\*",
                FINDEX_INFO_LEVELS.FindExInfoBasic,
                out data,
                FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero,
                FIND_FIRST_EX_LARGE_FETCH);

            if (handle == INVALID_HANDLE_VALUE)
            {
                ThrowIfEnumerationFailed(dir.FullName);
                return results.ToArray();
            }

            try
            {
                do
                {
                    if (string.IsNullOrEmpty(data.cFileName) || data.cFileName == "." || data.cFileName == "..") continue;
                    if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) continue;
                    results.Add(new FileInfo(Path.Combine(dir.FullName, data.cFileName)));
                } while (FindNextFileW(handle, out data));
            }
            finally
            {
                FindClose(handle);
            }

            return results.ToArray();
        }

        internal static DirectoryInfo[] GetDirectories(DirectoryInfo dir)
        {
            var results = new List<DirectoryInfo>();
            WIN32_FIND_DATAW data;

            IntPtr handle = FindFirstFileExW(
                dir.FullName + @"\*",
                FINDEX_INFO_LEVELS.FindExInfoBasic,
                out data,
                FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero,
                FIND_FIRST_EX_LARGE_FETCH);

            if (handle == INVALID_HANDLE_VALUE)
            {
                ThrowIfEnumerationFailed(dir.FullName);
                return results.ToArray();
            }

            try
            {
                do
                {
                    if (string.IsNullOrEmpty(data.cFileName) || data.cFileName == "." || data.cFileName == "..") continue;
                    if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0) continue;
                    results.Add(new DirectoryInfo(Path.Combine(dir.FullName, data.cFileName)));
                } while (FindNextFileW(handle, out data));
            }
            finally
            {
                FindClose(handle);
            }

            return results.ToArray();
        }

        internal struct EnumerationResult
        {
            public FileInfo[] Files;
            public DirectoryInfo[] Directories;
        }

        internal static EnumerationResult GetFilesAndDirectories(DirectoryInfo dir)
        {
            var files = new List<FileInfo>();
            var dirs = new List<DirectoryInfo>();
            WIN32_FIND_DATAW data;

            IntPtr handle = FindFirstFileExW(
                dir.FullName + @"\*",
                FINDEX_INFO_LEVELS.FindExInfoBasic,
                out data,
                FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero,
                FIND_FIRST_EX_LARGE_FETCH);

            if (handle == INVALID_HANDLE_VALUE)
            {
                ThrowIfEnumerationFailed(dir.FullName);
                return new EnumerationResult { Files = files.ToArray(), Directories = dirs.ToArray() };
            }

            try
            {
                do
                {
                    if (string.IsNullOrEmpty(data.cFileName) || data.cFileName == "." || data.cFileName == "..") continue;
                    string fullPath = Path.Combine(dir.FullName, data.cFileName);
                    if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0)
                        dirs.Add(new DirectoryInfo(fullPath));
                    else
                        files.Add(new FileInfo(fullPath));
                } while (FindNextFileW(handle, out data));
            }
            finally
            {
                FindClose(handle);
            }

            return new EnumerationResult { Files = files.ToArray(), Directories = dirs.ToArray() };
        }
    }
}
