using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using RED.Match;
using TXT = RED.RedGetText;

namespace RED.Helper
{
	class RedExportScanResults : IDisposable
    {
        public RedExportScanResults() { }


        public void Dispose() { }

        public void ExportToFile(RedScanResultItemList v)
        {
            Export(v, toClipboard: false);
        }

        public void ExportToCliboard(RedScanResultItemList v)
        {
            Export(v, toClipboard: true);
        }

        public void Export(RedScanResultItemList v, bool toClipboard)
        {
            try
            {
                if (v != null && v.Count > 0)
                {
                    if (toClipboard)
                    {
                        List<string> exportData = GetExportText(v);
                        Clipboard.SetText(string.Join(Environment.NewLine, exportData.ToArray()), TextDataFormat.UnicodeText);
                    }
                    else
                    {
                        ExportScanResultsToFile(v);
                    }
                }
                else
                {
                    UiAssist.MsgBoxInfo(TXT.Translate("There are no scan results to export. Scan or import results first."));
                }
            }
            catch (Exception ex)
            {
                UiAssist.MsgBoxException(TXT.Translate("Could not export scan results"), ex);
            }
        }

        private void ExportScanResultsToFile(RedScanResultItemList v)
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = TXT.Translate("Export scan results");
                dlg.Filter = TXT.Translate("Text files|*.txt|CSV files|*.csv|JSON files|*.json|All files|*.*");
                dlg.FilterIndex = 1;
                dlg.DefaultExt = "txt";
                dlg.FileName = "RED++_EmptyDirectories";
                dlg.OverwritePrompt = true;

                if (dlg.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.FileName))
                    return;

                string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                if (ext == ".csv")
                {
                    WriteCsv(v, dlg.FileName);
                }
                else if (ext == ".json")
                {
                    WriteJson(v, dlg.FileName);
                }
                else
                {
                    File.WriteAllLines(dlg.FileName, GetExportText(v), Encoding.UTF8);
                }
            }
        }

        /// <summary>Public entry for the headless CLI -export path.</summary>
        public void WriteCsvFile(RedScanResultItemList v, string filename)
        {
            WriteCsv(v, filename);
        }

        /// <summary>Public entry for the headless CLI -export path.</summary>
        public void WriteJsonFile(RedScanResultItemList v, string filename)
        {
            WriteJson(v, filename);
        }

        private void WriteCsv(RedScanResultItemList v, string filename)
        {
            var lines = new List<string> { "\"Kind\",\"Path\",\"Status\",\"Reason\"" };
            for (int i = 0; i < v.Count; i++)
            {
                string kind = v[i].Kind == Match.ResultKind.File ? "file" : "directory";
                string escapedPath = v[i].FullPath.Replace("\"", "\"\"");
                string escapedReason = (v[i].StatusReason ?? string.Empty).Replace("\"", "\"\"");
                lines.Add(string.Format("\"{0}\",\"{1}\",\"{2}\",\"{3}\"", kind, escapedPath, v[i].SearchStatus, escapedReason));
            }
            File.WriteAllLines(filename, lines, Encoding.UTF8);
        }

        private void WriteJson(RedScanResultItemList v, string filename)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < v.Count; i++)
            {
                string kind = v[i].Kind == Match.ResultKind.File ? "file" : "directory";
                string escapedPath = EscapeJson(v[i].FullPath);
                string escapedReason = EscapeJson(v[i].StatusReason);
                sb.AppendFormat("  {{ \"kind\": \"{0}\", \"path\": \"{1}\", \"status\": \"{2}\", \"reason\": \"{3}\" }}", kind, escapedPath, v[i].SearchStatus, escapedReason);
                if (i < v.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("]");
            File.WriteAllText(filename, sb.ToString(), Encoding.UTF8);
        }

        private static string EscapeJson(string value)
        {
            if (value == null) return string.Empty;
            var sb = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append(@"\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append(@"\r"); break;
                    case '\n': sb.Append(@"\n"); break;
                    case '\t': sb.Append(@"\t"); break;
                    default:
                        if (char.IsControl(c)) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private List<string> GetExportText(RedScanResultItemList v)
        {
            List<string> respx = new List<string>();
            for (int i = 0; i < v.Count; i++)
            {
                respx.Add(v[i].FullPath);
            }
            return respx;
        }

        public void ExportToFile(List<string> v)
        {
            Export(v, toClipboard: false);
        }

        public void ExportToClipboard(List<string> v)
        {
            Export(v, toClipboard: true);
        }

        private void Export(List<string> v, bool toClipboard)
        {
            try
            {
                if (v != null && v.Count > 0)
                {
                    v.Sort();
                    if (toClipboard)
                    {
                        Clipboard.SetText(string.Join(Environment.NewLine, v.ToArray()), TextDataFormat.UnicodeText);
                    }
                    else
                    {
                        string filename = RedAssist.BrowseForSaveAsFilename("RED_EmptyDirectoryList.txt");
                        if (!string.IsNullOrWhiteSpace(filename))
                        {
                            File.WriteAllLines(filename, v);
                        }
                    }
                }
                else
                {
                    UiAssist.MsgBoxInfo(TXT.Translate("There are no directories to export. Scan or import results first."));
                }
            }
            catch (Exception ex)
            {
                UiAssist.MsgBoxException(TXT.Translate("Could not export directories"), ex);
            }
        }

        public void ExportToFile(TreeView tv)
        {
            Export(tv, toClipboard: false);
        }

        public void ExportToClipboard(TreeView tv)
        {
            Export(tv, toClipboard: true);
        }

        private void Export(TreeView tv, bool toClipboard)
        {
            try
            {
                List<string> exportData = BuildExportData(tv);
                if (exportData.Count > 0)
                {
                    if (toClipboard)
                    {
                        Clipboard.SetText(string.Join(Environment.NewLine, exportData.ToArray()), TextDataFormat.UnicodeText);
                    }
                    else
                    {
                        string filename = RedAssist.BrowseForSaveAsFilename("RED_EmptyDirectoryList+.txt");
                        if (!string.IsNullOrWhiteSpace(filename))
                        {
                            File.WriteAllLines(filename, exportData, Encoding.UTF8);
                        }
                    }
                }
                else
                {
                    UiAssist.MsgBoxInfo(TXT.Translate("There are no scan results to export. Scan or import results first."));
                }
            }
            catch (Exception ex)
            {
                UiAssist.MsgBoxException(TXT.Translate("Could not export tree view results"), ex);
            }
        }

        private List<string> BuildExportData(TreeView tv)
        {
            List<string> respx = new List<string>();
            if (tv != null && tv.Nodes.Count > 0)
            {
                WalkTreeviewNodes(tv.Nodes, respx);
            }
            return respx;
        }

        private void WalkTreeviewNodes(TreeNodeCollection nodes, List<string> exportData)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                TreeNode node = nodes[i];
                string npath = GetFullPathText(node);
                if (!string.IsNullOrWhiteSpace(npath))
                {
                    exportData.Add(npath);
                }
                if (node.Nodes.Count > 0)
                {
                    WalkTreeviewNodes(node.Nodes, exportData);
                }
            }
        }

        private string GetFullPathText(TreeNode node)
        {
            string npath = string.Empty;
            if (node != null && node.ForeColor == TreeManager.ColortoBeDeleted)
            {
                TreeNode pnode = node;
                do
                {
                    npath = pnode.Text + Path.DirectorySeparatorChar + npath;
                    pnode = pnode.Parent;
                } while (pnode != null);
            }
            return npath;
        }
    }
}
