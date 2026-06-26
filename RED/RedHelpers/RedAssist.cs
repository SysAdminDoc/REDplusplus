using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RED.Helper
{
    internal class RedAssist
    {
        /// <summary>
        /// GetBuildTime relies on a custom file being embedded as a resource.
        /// The file is updated via a custom pre-build action
        /// which sets the files contents to the date and time of the build.
        /// </summary>
        public static DateTime GetBuildTime()
        {
            DateTime buildtime = DateTime.MinValue;
            try
            {
#if DEBUG
                string buildTimeInfo = Properties.Resources.BuildTimeDebug;
#else
                string buildTimeInfo = Properties.Resources.BuildTimeRelease;
#endif
                DateTime dt;
                if (DateTime.TryParse(buildTimeInfo, out dt))
                {
                    buildtime = dt;
                }
            }
            catch (Exception)
            {
                buildtime = DateTime.MinValue;
            }
            return buildtime;
        }

        public static string BrowseForSaveAsFilename(string filename)
        {
            string respx = string.Empty;

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "Select File To Write To";
                dlg.Filter = "Text Files|*.txt|All Files|*.*";
                dlg.FilterIndex = 0;
                dlg.CheckPathExists = true;
                dlg.CheckFileExists = false;
                dlg.DefaultExt = "txt";
                dlg.OverwritePrompt = true;
                if (!string.IsNullOrWhiteSpace(filename))
                {
                    dlg.InitialDirectory = Path.GetDirectoryName(filename);
                    dlg.FileName = Path.GetFileName(filename);
                }
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    respx = dlg.FileName;
                }
            }

            return respx;
        }

        private static bool AreStringListsDifferent(List<string> list1, List<string> list2)
        {
            if (list1.Count != list2.Count)
            {
                return true;
            }

            for (int i = 0; i < list1.Count; i++)
            {
                if (list1[i] != (list2[i]))
                {
                    return true;
                }
            }

            return false;
        }

        internal static Point GetScreenValidLocation(Point location)
        {
            Point respx = location;
            if (!IsOnScreen(location))
            {
                Rectangle screenRect = Screen.GetWorkingArea(location);
                respx = screenRect.Location;
            }
            return respx;
        }

        internal static bool IsOnScreen(Point location)
        {
            Screen[] screens = Screen.AllScreens;
            foreach (Screen item in screens)
            {
                if (item.WorkingArea.Contains(location))
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool FilterListUpdate(List<string> lstNew, List<string> filterList)
        {
            bool isDataDirty = false;
            if (lstNew != null && filterList != null)
            {
                if (AreStringListsDifferent(filterList, lstNew))
                {
                    filterList.Clear();
                    if (lstNew != null)
                    {
                        filterList.AddRange(lstNew);
                    }
                    isDataDirty = true;
                }
            }
            return isDataDirty;
        }

        internal static string DQuote(string v)
        {
            return string.Format("\"{0}\"", SanitizeDisplay(v));
        }

        /// <summary>
        /// Replaces bidi/zero-width control characters with visible \uXXXX escapes.
        /// A folder name containing U+202E (right-to-left override) would otherwise
        /// reorder the rendered text, making a confirmation or log line display a
        /// different-looking path than the one actually deleted (MITRE T1036.002).
        /// </summary>
        internal static string SanitizeDisplay(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            System.Text.StringBuilder sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool invisible =
                    (c >= '\u202A' && c <= '\u202E') ||                // LRE/RLE/PDF/LRO/RLO
                    (c >= '\u2066' && c <= '\u2069') ||                // LRI/RLI/FSI/PDI
                    (c >= '\u2060' && c <= '\u2064') ||                // Word Joiner, invisible operators
                    c == '\u200B' || c == '\u200C' || c == '\u200D' || // ZWSP/ZWNJ/ZWJ
                    c == '\u200E' || c == '\u200F' ||                  // LRM/RLM
                    c == '\uFEFF' ||                                   // BOM / ZWNBSP
                    (char.IsControl(c) && c != '\t');

                if (invisible)
                {
                    if (sb == null)
                    {
                        sb = new System.Text.StringBuilder(text.Substring(0, i), text.Length + 8);
                    }
                    sb.Append("\\u").Append(((int)c).ToString("X4"));
                }
                else if (sb != null)
                {
                    sb.Append(c);
                }
            }
            return sb == null ? text : sb.ToString();
        }

        internal static bool IsDrivePathRemovable(string path)
        {
            bool respx = false;

            if (!string.IsNullOrWhiteSpace(path))
            {
                var drive = Path.GetPathRoot(path);
                if (drive != null)
                {
                    var di = new DriveInfo(drive);
                    if (di != null)
                    {
                        if (di.DriveType == DriveType.Removable)
                        {
                            respx = true;
                        }
                    }
                }
            }

            return respx;
        }

        /// <summary>
        /// True for a path with no working Recycle Bin: a UNC share, or a Network /
        /// Removable drive. Deleting there is permanent (the shell bypasses the bin), so
        /// the UI/CLI should say "deleted", not "recycled" — undo still recreates the
        /// empty directories/files regardless.
        /// </summary>
        internal static bool IsNoRecycleBinPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            // UNC (\\server\share or \\?\UNC\server\share) never has a Recycle Bin.
            string trimmed = path.TrimStart();
            if (trimmed.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) && !trimmed.StartsWith(@"\\?\", StringComparison.Ordinal)) return true;

            try
            {
                string root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root)) return false;
                var di = new DriveInfo(root);
                return di.DriveType == DriveType.Network || di.DriveType == DriveType.Removable;
            }
            catch
            {
                return false;
            }
        }
    }
}